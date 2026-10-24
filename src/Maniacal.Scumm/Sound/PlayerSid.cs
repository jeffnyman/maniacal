using System.Buffers.Binary;

namespace Maniacal.Scumm.Sound;

/// <summary>
/// The C64's music and sound player: songs on the three SID voices, and sounds that take a voice,
/// a pulse width or the filter by priority, a low-priority background sound kept up to date while
/// it waits for a voice. Ticked once a frame, 60 times a second as on an NTSC C64. Mirrors ScummVM's
/// <c>Player_SID</c>, itself ported from the C64 engine (the addresses in its comments, kept here).
/// </summary>
/// <remarks>
/// <para>A song's byte 4 is 7, its voices; its byte 5 the step of its note lengths, and from 6 the
/// start of each voice's notes. A sound's byte 4 is its priority, byte 5 what it takes (a voice,
/// two or three, their pulse widths, the filter), and from 6 where each part's commands start.</para>
/// <para>Channels 0 to 2 are the voices, 3 the filter and 4 to 6 the voices' pulse widths. Songs
/// play through three channel buffers of the player's own, resources 3 to 5, whose commands the
/// song's notes rewrite. The resource locking of the original, which changes nothing here, is left
/// out.</para>
/// </remarks>
public sealed class PlayerSid : MusicPlayer
{
    // the player's tick, the C64's NTSC frame rate
    private const int TicksPerSecond = 60, FixpShift = 16;

    private static readonly int[] SidRegisterOffsets = [0, 7, 14, 21, 2, 9, 16];
    private static readonly int[] SongChannelOffsets = [6, 8, 10];
    private static readonly int[] ChannelResources = [3, 4, 5];

    private readonly Sid _sid;
    private readonly object _sync = new();
    private readonly int _samplesPerTick;
    private int _nextTick;

    // the sounds the game has handed over, by number, and the three channel buffers (resources 3 to 5)
    private readonly Dictionary<int, byte[]> _resources = [];
    private readonly byte[][] _channelBuffers = new byte[3][];

    private int _musicTimer;
    private Ptr _music;
    private int _resIdSong;
    private int _statusBits1A, _statusBits1B, _busyChannelBits;
    private int _sidReg23, _sidReg23Stuff, _sidReg24;
    private readonly Ptr[] _chanFileData = new Ptr[3];
    private readonly int[] _chanDataOffset = new int[3];
    private readonly Ptr[] _songPosPtr = new Ptr[7];
    private readonly int[] _freqReg = new int[7];
    private readonly int[] _vec6 = new int[7];
    private readonly int[] _songFileOrChanBufOffset = new int[7];
    private readonly int[] _freqDelta = new int[7];
    private readonly int[] _freqDeltaCounter = new int[7];
    private readonly Ptr[] _swapSongPosPtr = new Ptr[3];
    private readonly int[] _swapVec8 = new int[3], _swapVec10 = new int[3], _swapFreqReg = new int[3], _swapVec11 = new int[3];
    private readonly Ptr[] _vec20 = new Ptr[7];
    private readonly Ptr[] _swapVec20 = new Ptr[3];
    private Ptr _songFileOrChanBufData, _actSongFileData;
    private readonly int[] _stepTbl = new int[33];
    private bool _initializing, _soundInQueue, _isVoiceChannel, _isMusicPlaying, _swapVarLoaded, _bgSoundActive, _filterUsed;
    private int _bgSoundResId, _freeChannelCount, _usedChannelBits;
    private readonly int[] _attackReg = new int[3], _sustainReg = new int[3];
    private int _var481A, _songChannelBits;
    private bool _pulseWidthSwapped, _swapPrepared, _filterSwapped, _keepSwapVars;
    private int _sidReg24HiNibble;
    private readonly int[] _phaseBit = new int[3];
    private readonly bool[] _releasePhase = new bool[3];
    private readonly int[] _soundQueue = [-1, -1, -1, -1, -1, -1, -1];
    private readonly int[] _channelMap = new int[7];
    private readonly int[] _songPosUpdateCounter = new int[7];
    private readonly int[] _chanPrio = new int[7];
    private readonly int[] _waveCtrlReg = new int[7];
    private readonly int[] _swapAttack = new int[2], _swapSustain = new int[2];
    private readonly int[] _swapSongPrio = new int[3], _swapVec479C = new int[3], _swapSongPosUpdateCounter = new int[3], _swapWaveCtrlReg = new int[3];
    private bool _actFilterHasLowerPrio;
    private int _chansWithLowerPrioCount, _minChanPrio, _minChanPrioIndex;

    /// <summary>Mirrors the constructor, <c>initSID</c> and <c>resetSID</c>.</summary>
    public PlayerSid(int sampleRate)
        : base(sampleRate)
    {
        for (int i = 0; i < 3; i++)
        {
            _channelBuffers[i] = SidTables.ChannelBuffers.Slice(i * 45, 45).ToArray();
        }

        _sid = new Sid(sampleRate);

        // synchronise the waveform generators (initSID)
        foreach (int value in new[] { 0x08, 0x00 })
        {
            SidWrite(4, value);
            SidWrite(11, value);
            SidWrite(18, value);
        }

        // EmulatedChip::setCallbackFrequency
        _samplesPerTick = (sampleRate / TicksPerSecond << FixpShift) + (sampleRate % TicksPerSecond << FixpShift) / TicksPerSecond;
        ResetSid();
    }

    /// <summary>The timer a song's notes set, read and cleared. Mirrors <c>getMusicTimer</c>.</summary>
    public override int MusicTimer
    {
        get
        {
            lock (_sync)
            {
                int result = _musicTimer;
                _musicTimer = 0;
                return result;
            }
        }
    }

    /// <summary>
    /// Plays a resource: a song (byte 4 is 7, its three voices) starts at once; a sound stops if it
    /// plays and starts again, as its priority allows. Mirrors <c>startSound</c>.
    /// </summary>
    public override void StartSound(int number, byte[] data)
    {
        lock (_sync)
        {
            _resources[number] = data;
            if (data[4] == 0x07)
            {
                InitMusic(number);
            }
            else
            {
                StopSoundIntern(number);
                InitSound(number);
            }
        }
    }

    public override void StopSound(int number)
    {
        if (number == -1)
        {
            return;
        }

        lock (_sync)
        {
            StopSoundIntern(number);
        }
    }

    public override void StopAllSounds()
    {
        lock (_sync)
        {
            ResetPlayerState();
        }
    }

    /// <summary>Whether a song or sound plays, or waits to start. Mirrors <c>getSoundStatus</c>.</summary>
    public override bool IsPlaying(int number)
    {
        lock (_sync)
        {
            if (_resIdSong == number && _isMusicPlaying)
            {
                return true;
            }

            for (int i = 0; i < 4; i++)
            {
                if (number == _soundQueue[i] || number == _channelMap[i])
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Renders, ticking the player once a frame between runs of samples. Mirrors <c>EmulatedChip::readBuffer</c>.</summary>
    protected override void RenderMono(Span<short> buffer)
    {
        lock (_sync)
        {
            do
            {
                int step = Math.Min(buffer.Length, _nextTick >> FixpShift);
                _sid.Generate(buffer[..step]);
                _nextTick -= step << FixpShift;
                if ((_nextTick >> FixpShift) == 0)
                {
                    try
                    {
                        OnTimer();
                    }
                    catch (IndexOutOfRangeException)
                    {
                        // commands that run off their resource stop everything, where the original read on into whatever followed
                        ResetPlayerState();
                    }

                    _nextTick += _samplesPerTick;
                }

                buffer = buffer[step..];
            }
            while (buffer.Length > 0);
        }
    }

    private void SidWrite(int register, int value) => _sid.Write(register, value & 0xFF);

    private Ptr GetResource(int resId) => resId switch
    {
        0 => default,
        3 or 4 or 5 => new Ptr(_channelBuffers[resId - 3], 0),
        _ => _resources.TryGetValue(resId, out var data) ? new Ptr(data, 0) : default,
    };

    /// <summary>The song's next notes, on each voice whose note is done. Mirrors <c>handleMusicBuffer</c> ($33cd).</summary>
    private void HandleMusicBuffer()
    {
        int channel = 2;
        while (channel >= 0)
        {
            if ((_statusBits1A & Bit(channel)) == 0 || (_busyChannelBits & Bit(channel)) != 0)
            {
                channel--;
                continue;
            }

            if (SetupSongFileData() == 1)
            {
                return;
            }

            var chanFileDataPtr = _chanFileData[channel];
            int freq = 0;
            bool keepFreq = false;
            int y = 0;
            int curByte = chanFileDataPtr[y++];

            // a note's frequency, or 0 for the voice's end, or 0xFF to hold
            if (curByte == 0)
            {
                Func3674(channel);
                if (!_isMusicPlaying)
                {
                    return;
                }

                continue;
            }
            else if (curByte == 0xFF)
            {
                keepFreq = true;
            }
            else
            {
                freq = SidTables.NoteFrequencies[curByte];
            }

            int local1 = 0;
            curByte = chanFileDataPtr[y++];
            bool isLastCmdByte = (curByte & 0x80) != 0;
            int curStepSum = _stepTbl[curByte & 0x7F];
            for (int i = 0; !isLastCmdByte && i < 2; i++)
            {
                curByte = chanFileDataPtr[y++];
                isLastCmdByte = (curByte & 0x80) != 0;
                if ((curByte & 0x40) != 0)
                {
                    // Zak McKracken's theme only
                    _musicTimer = curByte & 0x3F;
                }
                else
                {
                    local1 = curByte & 0x3F;
                }
            }

            _chanFileData[channel] += y;
            _chanDataOffset[channel] = (_chanDataOffset[channel] + y) & 0xFFFF;

            var chanBuf = GetResource(ChannelResources[channel]);
            if (local1 != 0)
            {
                // an instrument: its frequency delta, attack, sustain, phase bit and commands
                int offset = _actSongFileData.Word(local1 * 2 + 12);
                var instrument = _actSongFileData + offset;
                for (int i = 0; i < 5; i++)
                {
                    chanBuf[15 + i] = instrument[i];
                }

                _phaseBit[channel] = instrument[4];
                for (int i = 0; i < 17; i++)
                {
                    chanBuf[25 + i] = instrument[5 + i];
                }
            }

            if (keepFreq)
            {
                if (!_releasePhase[channel])
                {
                    chanBuf[10] &= 0xFE;
                }

                _releasePhase[channel] = true;
            }
            else
            {
                if (_releasePhase[channel])
                {
                    chanBuf[19] = _phaseBit[channel];
                    chanBuf[10] |= 0x01;
                }

                chanBuf[11] = freq & 0xFF;
                chanBuf[12] = (freq >> 8) & 0xFF;
                _releasePhase[channel] = false;
            }

            chanBuf[13] = curStepSum & 0xFF;
            chanBuf[14] = (curStepSum >> 8) & 0xFF;

            _soundQueue[channel] = ChannelResources[channel];
            ProcessSongData(channel);
            _soundQueue[channel + 4] = ChannelResources[channel];
            ProcessSongData(channel + 4);
            channel--;
        }
    }

    /// <summary>1 with no song, -1 when the song is new (its voices' positions taken again), else 0. Mirrors <c>setupSongFileData</c> ($36cb).</summary>
    private int SetupSongFileData()
    {
        if (_music.IsNull)
        {
            for (int i = 2; i >= 0; i--)
            {
                if ((_songChannelBits & Bit(i)) != 0)
                {
                    Func3674(i);
                }
            }

            return 1;
        }

        _songFileOrChanBufData = _music;
        if (_music == _actSongFileData)
        {
            return 0;
        }

        _actSongFileData = _music;
        for (int i = 0; i < 3; i++)
        {
            _chanFileData[i] = _music + _chanDataOffset[i];
        }

        return -1;
    }

    /// <summary>A song's voice ends; with the last, the song does. Mirrors <c>func_3674</c> ($3674).</summary>
    private void Func3674(int channel)
    {
        _statusBits1B &= ~Bit(channel);
        if (_statusBits1B == 0)
        {
            _isMusicPlaying = false;
        }

        _chanPrio[channel] = 2;
        _statusBits1A &= ~Bit(channel);
        _phaseBit[channel] = 0;
        Func4F45(channel);
    }

    /// <summary>Mirrors <c>resetPlayerState</c> ($48f7).</summary>
    private void ResetPlayerState()
    {
        for (int i = 6; i >= 0; i--)
        {
            ReleaseChannel(i);
        }

        _isMusicPlaying = false;
        _statusBits1B = 0;
        _statusBits1A = 0;
        _freeChannelCount = 3;
        _swapPrepared = false;
        _filterSwapped = false;
        _pulseWidthSwapped = false;
    }

    /// <summary>Mirrors <c>resetSID</c> ($48D8): the voices silent, no filter, full volume.</summary>
    private void ResetSid()
    {
        _sidReg24 = 0x0F;
        SidWrite(4, 0);
        SidWrite(11, 0);
        SidWrite(18, 0);
        SidWrite(23, 0);
        SidWrite(21, 0);
        SidWrite(22, 0);
        SidWrite(24, _sidReg24);
        ResetPlayerState();
    }

    /// <summary>
    /// A frame: sounds that were queued start, every busy channel steps, a waiting background
    /// sound steps out of sight, the voices' waveforms are set, and the song moves on. Mirrors
    /// <c>onTimer</c> ($481B).
    /// </summary>
    private void OnTimer()
    {
        if (_initializing)
        {
            return;
        }

        if (_soundInQueue)
        {
            for (int i = 6; i >= 0; i--)
            {
                if (_soundQueue[i] != -1)
                {
                    ProcessSongData(i);
                }
            }

            _soundInQueue = false;
        }

        if (_busyChannelBits == 0)
        {
            return;
        }

        for (int i = 6; i >= 0; i--)
        {
            if ((_busyChannelBits & Bit(i)) != 0)
            {
                UpdateFreq(i);
            }
        }

        if (_swapPrepared)
        {
            SwapVars(0, 0);
            _swapVarLoaded = true;
            UpdateFreq(0);
            SwapVars(0, 0);
            if (_pulseWidthSwapped)
            {
                SwapVars(4, 1);
                UpdateFreq(4);
                SwapVars(4, 1);
            }

            _swapVarLoaded = false;
        }

        for (int i = 6; i >= 0; i--)
        {
            if ((_busyChannelBits & Bit(i)) != 0)
            {
                SetSidWaveCtrlReg(i);
            }
        }

        if (_isMusicPlaying)
        {
            HandleMusicBuffer();
        }
    }

    /// <summary>A channel takes the queued resource and starts its commands. Mirrors <c>processSongData</c> ($4939).</summary>
    private void ProcessSongData(int channel)
    {
        _channelMap[channel] = _soundQueue[channel];
        _soundQueue[channel] = -1;
        _songPosUpdateCounter[channel] = 0;
        _isVoiceChannel = channel < 3;
        _songFileOrChanBufOffset[channel] = _vec6[channel];
        SetupSongPtr(channel);
        if (_songFileOrChanBufData.IsNull)
        {
            ReleaseResourceUnk(channel);
            return;
        }

        _vec20[channel] = _songFileOrChanBufData;
        _songPosPtr[channel] = _songFileOrChanBufData + _songFileOrChanBufOffset[channel];
        var ptr1 = _songPosPtr[channel];

        int y = -1;
        if (channel < 4)
        {
            y++;
            if (channel == 3)
            {
                ReadSetSidFilterAndProps(ref y, ptr1);
            }
            else if ((_statusBits1A & Bit(channel)) != 0)
            {
                y++;
            }
            else
            {
                _waveCtrlReg[channel] = ptr1[y];
                y++;
                if ((ptr1[y] & 0x0F) != 0)
                {
                    _sidReg23 |= Bit(channel);
                }
                else
                {
                    _sidReg23 &= ~Bit(channel);
                }

                SidWrite(23, _sidReg23);
            }
        }

        SaveSongPos(y, channel);
        _busyChannelBits |= Bit(channel);
        ReadSongChunk(channel);
    }

    /// <summary>Mirrors <c>readSetSIDFilterAndProps</c> ($49e7): the voices into the filter, and its mode and volume.</summary>
    private void ReadSetSidFilterAndProps(ref int offset, Ptr data)
    {
        _sidReg23 |= data[offset];
        SidWrite(23, _sidReg23);
        offset++;
        _sidReg24 = data[offset];
        SidWrite(24, _sidReg24);
    }

    private void SaveSongPos(int y, int channel)
    {
        y++;
        _songPosPtr[channel] += y;
        _songFileOrChanBufOffset[channel] = (_songFileOrChanBufOffset[channel] + y) & 0xFFFF;
    }

    /// <summary>A channel's frequency slides on, or its next commands are read. Mirrors <c>updateFreq</c>.</summary>
    private void UpdateFreq(int channel)
    {
        _isVoiceChannel = channel < 3;
        _freqDeltaCounter[channel]--;
        if (_freqDeltaCounter[channel] < 0)
        {
            ReadSongChunk(channel);
        }
        else
        {
            _freqReg[channel] = (_freqReg[channel] + _freqDelta[channel]) & 0xFFFF;
        }

        SetSidFreqAs(channel);
    }

    private void ResetFreqDelta(int channel)
    {
        _freqDeltaCounter[channel] = 0;
        _freqDelta[channel] = 0;
    }

    /// <summary>
    /// A channel's next commands: the gate, the end, a frequency with its slide, attack and
    /// sustain, pulse width, filter, a slide length, the waveform, and loops back. Mirrors
    /// <c>readSongChunk</c> ($4a6b).
    /// </summary>
    private void ReadSongChunk(int channel)
    {
        while (true)
        {
            if (SetupSongPtr(channel) == 1)
            {
                ReleaseResourceUnk(1);
                return;
            }

            var ptr1 = _songPosPtr[channel];
            int cmd = ptr1[0];
            if (cmd == 0)
            {
                _songPosUpdateCounter[channel] = 0;
                _var481A = -1;
                ReleaseChannel(channel);
                return;
            }

            if (_isVoiceChannel)
            {
                if ((cmd & 0x01) != 0)
                {
                    _waveCtrlReg[channel] |= 0x01;
                }
                else
                {
                    _waveCtrlReg[channel] &= 0xFE;
                }
            }

            if ((cmd & 0x02) != 0)
            {
                _var481A = -1;
                ReleaseChannel(channel);
                return;
            }

            int y = 0;
            if ((cmd & 0x04) != 0)
            {
                y += 2;
                _freqReg[channel] = ptr1.Word(y - 1);
                if ((cmd & 0x40) == 0)
                {
                    y += 2;
                    _freqDeltaCounter[channel] = ptr1.Word(y - 1);
                    y += 2;
                    _freqDelta[channel] = ptr1.Word(y - 1);
                }
                else
                {
                    ResetFreqDelta(channel);
                }
            }
            else
            {
                ResetFreqDelta(channel);
            }

            if (_isVoiceChannel && (cmd & 0x08) != 0)
            {
                _waveCtrlReg[channel] &= 0xFE;
                SetSidWaveCtrlReg(channel);
                y++;
                _attackReg[channel] = ptr1[y];
                y++;
                _sustainReg[channel] = ptr1[y];
                _waveCtrlReg[channel] |= cmd & 0x01;
            }

            if ((cmd & 0x10) != 0)
            {
                y++;
                int curByte = ptr1[y];
                if (_isVoiceChannel && (curByte & 0x01) != 0)
                {
                    int register = SidRegisterOffsets[channel + 4];
                    y += 2;
                    SidWrite(register, ptr1[y - 1]);
                    SidWrite(register + 1, ptr1[y]);
                }

                if ((curByte & 0x02) != 0)
                {
                    y++;
                    ReadSetSidFilterAndProps(ref y, ptr1);
                    y += 2;
                    SidWrite(21, ptr1[y - 1]);
                    SidWrite(22, ptr1[y]);
                }

                if ((curByte & 0x04) != 0)
                {
                    ResetFreqDelta(channel);
                    y += 2;
                    _freqDeltaCounter[channel] = ptr1.Word(y - 1);
                }
            }

            if ((cmd & 0x20) != 0)
            {
                y++;
                _waveCtrlReg[channel] = (_waveCtrlReg[channel] & 0x0F) | ptr1[y];
            }

            if ((cmd & 0x80) == 0)
            {
                SaveSongPos(y, channel);
                return;
            }

            // a loop back: so many bytes, so many times
            if (_songPosUpdateCounter[channel] == 1)
            {
                y += 2;
                _songPosUpdateCounter[channel]--;
                SaveSongPos(y, channel);
            }
            else
            {
                y++;
                _songPosPtr[channel] -= ptr1[y];
                _songFileOrChanBufOffset[channel] = (_songFileOrChanBufOffset[channel] - ptr1[y]) & 0xFFFF;
                y++;
                _songPosUpdateCounter[channel] = _songPosUpdateCounter[channel] == 0 ? ptr1[y] : (_songPosUpdateCounter[channel] - 1) & 0xFF;
            }
        }
    }

    /// <summary>Mirrors <c>setSIDFreqAS</c> ($4be6): the frequency or pulse width, and a voice's attack and sustain.</summary>
    private void SetSidFreqAs(int channel)
    {
        if (_swapVarLoaded)
        {
            return;
        }

        int register = SidRegisterOffsets[channel];
        SidWrite(register, _freqReg[channel] & 0xFF);
        SidWrite(register + 1, _freqReg[channel] >> 8);
        if (channel < 3)
        {
            SidWrite(register + 5, _attackReg[channel]);
            SidWrite(register + 6, _sustainReg[channel]);
        }
    }

    /// <summary>Mirrors <c>setSIDWaveCtrlReg</c> ($4C0D).</summary>
    private void SetSidWaveCtrlReg(int channel)
    {
        if (channel < 3)
        {
            SidWrite(SidRegisterOffsets[channel] + 4, _waveCtrlReg[channel]);
        }
    }

    /// <summary>1 when the channel's resource is gone (and released), -1 when it changed (the position taken again in it), else 0. Mirrors <c>setupSongPtr</c> ($4C1C).</summary>
    private int SetupSongPtr(int channel)
    {
        int resId = _channelMap[channel];
        if (GetResource(resId).IsNull)
        {
            ReleaseResourceUnk(resId);
            if (resId == _bgSoundResId)
            {
                _bgSoundResId = 0;
                _bgSoundActive = false;
                _swapPrepared = false;
                _pulseWidthSwapped = false;
            }

            return 1;
        }

        _songFileOrChanBufData = GetResource(resId);
        if (_songFileOrChanBufData == _vec20[channel])
        {
            return 0;
        }

        _vec20[channel] = _songFileOrChanBufData;
        _songPosPtr[channel] = _songFileOrChanBufData + _songFileOrChanBufOffset[channel];
        return -1;
    }

    private void CountFreeChannels()
    {
        _freeChannelCount = 0;
        for (int i = 0; i < 3; i++)
        {
            if ((_usedChannelBits & Bit(i)) == 0)
            {
                _freeChannelCount++;
            }
        }
    }

    /// <summary>
    /// A channel is let go: a background sound's state is kept aside, or a waiting background
    /// sound takes the voice back; else the voice is free. Mirrors <c>func_4F45</c> ($4F45).
    /// </summary>
    private void Func4F45(int channel)
    {
        if (_swapVarLoaded)
        {
            if (channel == 0)
            {
                _swapPrepared = false;
                ResetSwapVars();
            }

            _pulseWidthSwapped = false;
        }
        else
        {
            if (channel == 3)
            {
                _filterUsed = false;
            }

            if (_chanPrio[channel] == 1)
            {
                if (_var481A == 1)
                {
                    PrepareSwapVars(channel);
                }
                else if (channel < 3)
                {
                    ClearSidWaveform(channel);
                }
            }
            else if (channel < 3 && _bgSoundActive && _swapPrepared && !(_filterSwapped && _filterUsed))
            {
                _busyChannelBits |= Bit(channel);
                UseSwapVars(channel);
                _waveCtrlReg[channel] |= 0x01;
                SetSidWaveCtrlReg(channel);
                return;
            }

            _chanPrio[channel] = 0;
            _usedChannelBits &= ~Bit(channel);
            CountFreeChannels();
        }

        _channelMap[channel] = 0;
    }

    /// <summary>Mirrors <c>releaseResource</c> ($5031): the resource's channels stop; a stopped background sound is forgotten.</summary>
    private void ReleaseResource(int resIndex)
    {
        ReleaseResChannels(resIndex);
        if (resIndex == _bgSoundResId && _var481A == -1)
        {
            _bgSoundResId = 0;
            _bgSoundActive = false;
            _swapPrepared = false;
            _pulseWidthSwapped = false;
            ResetSwapVars();
        }
    }

    private void ReleaseResChannels(int resIndex)
    {
        for (int i = 3; i >= 0; i--)
        {
            if (resIndex == _channelMap[i])
            {
                ReleaseChannel(i);
            }
        }
    }

    /// <summary>Mirrors <c>stopSound_intern</c> ($5093).</summary>
    private void StopSoundIntern(int soundResId)
    {
        for (int i = 0; i < 7; i++)
        {
            if (soundResId == _soundQueue[i])
            {
                _soundQueue[i] = -1;
            }
        }

        _var481A = -1;
        ReleaseResource(soundResId);
    }

    private void ReleaseResourceUnk(int resIndex)
    {
        _var481A = -1;
        ReleaseResource(resIndex);
    }

    /// <summary>A channel stops; a voice's waveform is cleared, its pulse width channel stops, and the filter when no voice uses it. Mirrors <c>releaseChannel</c>.</summary>
    private void ReleaseChannel(int channel)
    {
        StopChannel(channel);
        if (channel >= 4)
        {
            return;
        }

        if (channel < 3)
        {
            _sidReg23Stuff = _sidReg23;
            ClearSidWaveform(channel);
        }

        Func4F45(channel);
        if (channel >= 3)
        {
            return;
        }

        if (_sidReg23 != _sidReg23Stuff && (_sidReg23 & 0x07) == 0 && _filterUsed)
        {
            Func4F45(3);
            StopChannel(3);
        }

        StopChannel(channel + 4);
    }

    private void ClearSidWaveform(int channel)
    {
        if (!_isMusicPlaying && _var481A == -1)
        {
            _waveCtrlReg[channel] &= 0x0E;
            SetSidWaveCtrlReg(channel);
        }
    }

    private void StopChannel(int channel)
    {
        _songPosUpdateCounter[channel] = 0;
        _busyChannelBits &= ~Bit(channel);
        if (channel >= 4)
        {
            _channelMap[channel] = 0;
        }
    }

    /// <summary>A channel's state and the background sound's change places. Mirrors <c>swapVars</c> ($51a5).</summary>
    private void SwapVars(int channel, int swapIndex)
    {
        if (channel < 3)
        {
            (_attackReg[channel], _swapAttack[swapIndex]) = (_swapAttack[swapIndex], _attackReg[channel]);
            (_sustainReg[channel], _swapSustain[swapIndex]) = (_swapSustain[swapIndex], _sustainReg[channel]);
        }

        (_chanPrio[channel], _swapSongPrio[swapIndex]) = (_swapSongPrio[swapIndex], _chanPrio[channel]);
        (_channelMap[channel], _swapVec479C[swapIndex]) = (_swapVec479C[swapIndex], _channelMap[channel]);
        (_songPosUpdateCounter[channel], _swapSongPosUpdateCounter[swapIndex]) = (_swapSongPosUpdateCounter[swapIndex], _songPosUpdateCounter[channel]);
        (_waveCtrlReg[channel], _swapWaveCtrlReg[swapIndex]) = (_swapWaveCtrlReg[swapIndex], _waveCtrlReg[channel]);
        (_songPosPtr[channel], _swapSongPosPtr[swapIndex]) = (_swapSongPosPtr[swapIndex], _songPosPtr[channel]);
        (_freqReg[channel], _swapFreqReg[swapIndex]) = (_swapFreqReg[swapIndex], _freqReg[channel]);
        (_freqDeltaCounter[channel], _swapVec11[swapIndex]) = (_swapVec11[swapIndex], _freqDeltaCounter[channel]);
        (_freqDelta[channel], _swapVec10[swapIndex]) = (_swapVec10[swapIndex], _freqDelta[channel]);
        (_vec20[channel], _swapVec20[swapIndex]) = (_swapVec20[swapIndex], _vec20[channel]);
        (_songFileOrChanBufOffset[channel], _swapVec8[swapIndex]) = (_swapVec8[swapIndex], _songFileOrChanBufOffset[channel]);
    }

    private void ResetSwapVars()
    {
        Array.Clear(_swapAttack);
        Array.Clear(_swapSustain);
        Array.Clear(_swapSongPrio);
        Array.Clear(_swapVec479C);
        Array.Clear(_swapSongPosUpdateCounter);
        Array.Clear(_swapWaveCtrlReg);
        Array.Clear(_swapSongPosPtr);
        Array.Clear(_swapFreqReg);
        Array.Clear(_swapVec11);
        Array.Clear(_swapVec10);
        Array.Clear(_swapVec20);
        Array.Clear(_swapVec8);
    }

    /// <summary>A background sound giving up its voice or filter keeps its state aside. Mirrors <c>prepareSwapVars</c> ($52E5).</summary>
    private void PrepareSwapVars(int channel)
    {
        if (channel >= 4)
        {
            return;
        }

        if (channel < 3)
        {
            if (!_keepSwapVars)
            {
                ResetSwapVars();
            }

            SwapVars(channel, 0);
            if ((_busyChannelBits & Bit(channel + 4)) != 0)
            {
                SwapVars(channel + 4, 1);
                _pulseWidthSwapped = true;
            }
        }
        else
        {
            _sidReg24HiNibble = _sidReg24 & 0x70;
            ResetSwapVars();
            _keepSwapVars = true;
            SwapVars(3, 2);
            _filterSwapped = true;
        }

        _swapPrepared = true;
    }

    /// <summary>The background sound takes a free voice back, with its pulse width and filter. Mirrors <c>useSwapVars</c> ($5342).</summary>
    private void UseSwapVars(int channel)
    {
        if (channel >= 3)
        {
            return;
        }

        SwapVars(channel, 0);
        SetSidFreqAs(channel);
        if (_pulseWidthSwapped)
        {
            SwapVars(channel + 4, 1);
            SetSidFreqAs(channel + 4);
        }

        if (_filterSwapped)
        {
            SwapVars(3, 2);
            _sidReg23 = (_sidReg23Stuff & 0xF0) | Bit(channel);
            SidWrite(23, _sidReg23);
            _sidReg24 = (_sidReg24 & 0x0F) | _sidReg24HiNibble;
            SidWrite(24, _sidReg24);
            SidWrite(21, _freqReg[3] & 0xFF);
            SidWrite(22, _freqReg[3] >> 8);
        }
        else
        {
            _sidReg23 = _sidReg23Stuff & ~Bit(channel);
            SidWrite(23, _sidReg23);
        }

        _swapPrepared = false;
        _pulseWidthSwapped = false;
        _keepSwapVars = false;
        _sidReg24HiNibble = 0;
        _filterSwapped = false;
    }

    private void ReserveChannel(int channel, int priority)
    {
        if (channel == 3)
        {
            _filterUsed = true;
        }
        else if (channel < 3)
        {
            _usedChannelBits |= Bit(channel);
            CountFreeChannels();
        }

        _chanPrio[channel] = priority & 0xFF;
    }

    /// <summary>A song starts: each of its voices from its notes, the first notes read at once. Mirrors <c>initMusic</c> ($7de6).</summary>
    private void InitMusic(int songResIndex)
    {
        _resIdSong = songResIndex;
        _music = GetResource(_resIdSong);
        if (_music.IsNull)
        {
            return;
        }

        var songFileDataPtr = _music;
        _actSongFileData = _music;
        _initializing = true;
        _soundInQueue = false;
        _isMusicPlaying = false;
        ResetPlayerState();
        BuildStepTbl(songFileDataPtr[5]);

        _songChannelBits = songFileDataPtr[4];
        for (int i = 2; i >= 0; i--)
        {
            if ((_songChannelBits & Bit(i)) != 0)
            {
                Func7EAE(i, songFileDataPtr);
            }
        }

        _isMusicPlaying = true;
        _sidReg23 &= 0xF0;
        SidWrite(23, _sidReg23);
        HandleMusicBuffer();
        _initializing = false;
        _soundInQueue = true;
    }

    /// <summary>A song's voice: where its notes start, and its buffer's commands. Mirrors <c>func_7eae</c> and <c>func_819b</c>.</summary>
    private void Func7EAE(int channel, Ptr songFileDataPtr)
    {
        _chanDataOffset[channel] = songFileDataPtr.Word(SongChannelOffsets[channel]);
        _chanFileData[channel] = songFileDataPtr + _chanDataOffset[channel];
        _vec6[channel + 4] = 0x0019;
        _vec6[channel] = 0x0008;

        ReserveChannel(channel, 127);
        _statusBits1B |= Bit(channel);
        _statusBits1A |= Bit(channel);
        _waveCtrlReg[channel] = 0;
    }

    /// <summary>The song's note lengths: a step, less 2 for the shortest. Mirrors <c>buildStepTbl</c> ($82B4).</summary>
    private void BuildStepTbl(int step)
    {
        _stepTbl[0] = 0;
        _stepTbl[1] = (step - 2) & 0xFFFF;
        for (int i = 2; i < 33; i++)
        {
            _stepTbl[i] = (_stepTbl[i - 1] + step) & 0xFFFF;
        }
    }

    private int ReserveSoundFilter(int priority)
    {
        ReserveChannel(3, priority);
        return 3;
    }

    private int ReserveSoundVoice(int priority)
    {
        for (int i = 2; i >= 0; i--)
        {
            if ((_usedChannelBits & Bit(i)) == 0)
            {
                ReserveChannel(i, priority);
                return i;
            }
        }

        return 0;
    }

    /// <summary>How many voices play something of lower priority, the lowest, and whether the filter's sound is lower. Mirrors <c>findLessPrioChannels</c> ($4ED8).</summary>
    private void FindLessPrioChannels(int soundPrio)
    {
        _minChanPrio = 127;
        _chansWithLowerPrioCount = 0;
        for (int i = 2; i >= 0; i--)
        {
            if ((_usedChannelBits & Bit(i)) != 0)
            {
                if (_chanPrio[i] < soundPrio)
                {
                    _chansWithLowerPrioCount++;
                }

                if (_chanPrio[i] < _minChanPrio)
                {
                    _minChanPrio = _chanPrio[i];
                    _minChanPrioIndex = i;
                }
            }
        }

        if (_chansWithLowerPrioCount == 0)
        {
            return;
        }

        _actFilterHasLowerPrio = soundPrio >= _chanPrio[3];
    }

    private void ReleaseResourceBySound(int resId)
    {
        _var481A = 1;
        ReleaseResource(resId);
    }

    private void ReadVec6Data(int x, ref int offset, Ptr songFilePtr, int chanResId)
    {
        _vec6[x] = songFilePtr.Word(offset);
        offset += 2;
        _soundQueue[x] = chanResId;
    }

    /// <summary>
    /// A sound takes the voices, pulse widths and filter it asks for, from sounds of lower priority
    /// if it must, or does not play; its parts start on the next frame. Mirrors <c>initSound</c> ($4D0A).
    /// </summary>
    private int InitSound(int soundResId)
    {
        _initializing = true;
        if (_isMusicPlaying && (_statusBits1A & 0x07) == 0x07)
        {
            _initializing = false;
            return -2;
        }

        var songFilePtr = GetResource(soundResId);
        if (songFilePtr.IsNull)
        {
            _initializing = false;
            return 1;
        }

        int soundPrio = songFilePtr[4];
        if (soundPrio == 1)
        {
            _bgSoundResId = soundResId;
            _bgSoundActive = true;
        }

        int requestedChannels = 0;
        if ((songFilePtr[5] & 0x40) == 0)
        {
            requestedChannels++;
            if ((songFilePtr[5] & 0x02) != 0)
            {
                requestedChannels++;
            }

            if ((songFilePtr[5] & 0x08) != 0)
            {
                requestedChannels++;
            }
        }

        bool filterNeeded = (songFilePtr[5] & 0x20) != 0;
        bool filterBlocked = _filterUsed && filterNeeded;
        if (filterBlocked || _freeChannelCount < requestedChannels)
        {
            FindLessPrioChannels(soundPrio);
            if (_freeChannelCount + _chansWithLowerPrioCount < requestedChannels || (filterBlocked && !_actFilterHasLowerPrio))
            {
                _initializing = false;
                return -1;
            }

            if (filterBlocked)
            {
                if (soundPrio < _chanPrio[3])
                {
                    _initializing = false;
                    return -1;
                }

                ReleaseResourceBySound(_channelMap[3] & 0xFF);
            }

            while (_freeChannelCount < requestedChannels || (filterNeeded && _filterUsed))
            {
                FindLessPrioChannels(soundPrio);
                if (_minChanPrio >= soundPrio)
                {
                    _initializing = false;
                    return -1;
                }

                ReleaseResourceBySound(_channelMap[_minChanPrioIndex] & 0xFF);
            }
        }

        int soundByte5 = songFilePtr[5];
        int x = (soundByte5 & 0x40) != 0 ? ReserveSoundFilter(soundPrio) : ReserveSoundVoice(soundPrio);
        int first = x;
        int y = 6;
        if ((soundByte5 & 0x01) != 0)
        {
            x += 4;
            ReadVec6Data(x, ref y, songFilePtr, soundResId);
        }

        if ((soundByte5 & 0x02) != 0)
        {
            x = ReserveSoundVoice(soundPrio);
            ReadVec6Data(x, ref y, songFilePtr, soundResId);
        }

        if ((soundByte5 & 0x04) != 0)
        {
            x += 4;
            ReadVec6Data(x, ref y, songFilePtr, soundResId);
        }

        if ((soundByte5 & 0x08) != 0)
        {
            x = ReserveSoundVoice(soundPrio);
            ReadVec6Data(x, ref y, songFilePtr, soundResId);
        }

        if ((soundByte5 & 0x10) != 0)
        {
            x += 4;
            ReadVec6Data(x, ref y, songFilePtr, soundResId);
        }

        if ((soundByte5 & 0x20) != 0)
        {
            x = ReserveSoundFilter(soundPrio);
            ReadVec6Data(x, ref y, songFilePtr, soundResId);
        }

        _vec6[first] = y;
        _soundQueue[first] = soundResId;
        _initializing = false;
        _soundInQueue = true;
        return soundResId;
    }

    private static int Bit(int channel) => 1 << channel;

    /// <summary>A pointer into a resource, as the original's are: its bytes and where in them.</summary>
    private readonly record struct Ptr(byte[]? Data, int Offset)
    {
        public bool IsNull => Data is null;

        public int this[int index]
        {
            get => Data![Offset + index];
            set => Data![Offset + index] = (byte)value;
        }

        public int Word(int index) => BinaryPrimitives.ReadUInt16LittleEndian(Data.AsSpan(Offset + index, 2));

        public static Ptr operator +(Ptr p, int n) => p with { Offset = p.Offset + n };

        public static Ptr operator -(Ptr p, int n) => p with { Offset = p.Offset - n };
    }
}
