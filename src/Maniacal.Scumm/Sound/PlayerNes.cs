namespace Maniacal.Scumm.Sound;

/// <summary>
/// The NES release's sound: its music and sound effects played on the NES's sound chip
/// (<see cref="NesApu"/>), the player ticked 60 times a second between samples. Mirrors ScummVM's
/// <c>Player_NES</c>, but for the title screens' twinkles (<c>startTitleTwinkleGroup</c>), which
/// belong to title screens Maniacal does not show yet.
/// </summary>
/// <remarks>
/// <para>A sound resource, after its size word, names its slot (0 or 1 an effect, 4 an effect
/// that waits for no music, 2 a song) and its priority: a sound of lower priority than the one in
/// its slot is not played. An effect is register writes for a channel, each four bytes after the
/// channel, and frame counts to wait, 0xFE to loop and 0xFF to end. A song is its notes'
/// instruments and pitches, then bytes that start or release notes or wait.</para>
/// <para>While an effect plays the music is silenced, its channels taken back after.</para>
/// </remarks>
internal sealed class PlayerNes : MusicPlayer
{
    private const int Slots = 3, Channels = 4, MaxVolume = 0x7F, FramesPerSecond = 60, MusicSlot = 2;

    private readonly object _sync = new();
    private readonly NesApu _apu;
    private readonly int _samplesPerFrame;
    private int _currentSample;

    private readonly Slot[] _slots = [new(), new(), new()];
    private readonly MusicChannel[] _channels = [new(), new(), new(), new()];
    private bool _sfxPlaying, _sfxWasPlaying;

    // the song's notes: their instruments and their pitches (auxData1, auxData2)
    private byte[] _song = [];
    private int _noteCount, _instruments, _pitches;

    private sealed class Slot
    {
        public int FramesLeft;
        public int Id = -1;
        public int Type;
        public byte[] Data = [];
        public int Base;
        public int Offset;

        public byte Next() => Data[Base + Offset++];
    }

    private sealed class MusicChannel
    {
        public int Command, FrameDelay, Pitch, Volume, VolumeDelta, EnvelopeFlags, CommandLock;
    }

    public PlayerNes(int sampleRate)
        : base(sampleRate)
    {
        _apu = new NesApu(sampleRate);
        _samplesPerFrame = sampleRate / FramesPerSecond;
        WriteControl(0);
    }

    /// <summary>The NES's songs keep no timer for the scripts (<c>MusicEngine::getMusicTimer</c>).</summary>
    public override int MusicTimer => 0;

    /// <summary>
    /// Plays a sound in its slot, unless one of higher priority plays there; an effect for slot 4
    /// plays in slot 0, but not while a song plays. A song resets its channels. Mirrors <c>startSound</c>.
    /// </summary>
    public override void StartSound(int number, byte[] data)
    {
        lock (_sync)
        {
            const int Skip = 2;
            if (data.Length < Skip + 2)
            {
                return;
            }

            int type = data[Skip + 1];
            int channel = data[Skip];
            if (channel == 4)
            {
                if (_slots[MusicSlot].FramesLeft != 0)
                {
                    return;
                }

                channel = 0;
            }

            if (channel >= Slots || type < _slots[channel].Type)
            {
                return;
            }

            var slot = _slots[channel];
            slot.Type = type;
            slot.Id = number;
            slot.Data = data;
            slot.Base = Skip;
            slot.Offset = 2;
            slot.FramesLeft = 1;
            CheckSilenceChannels(channel);
            if (channel == MusicSlot)
            {
                _song = data;
                _noteCount = data[Skip + 2];
                _instruments = Skip + 3;
                _pitches = _instruments + _noteCount;
                slot.Base = _pitches + _noteCount;
                slot.Offset = 0;
                foreach (var c in _channels)
                {
                    c.CommandLock = 0;
                }
            }
        }
    }

    /// <summary>Mirrors <c>stopSound</c>.</summary>
    public override void StopSound(int number)
    {
        if (number == -1)
        {
            return;
        }

        lock (_sync)
        {
            for (int i = 0; i < Slots; i++)
            {
                if (_slots[i].Id != number)
                {
                    continue;
                }

                _sfxPlaying = false;
                _slots[i].FramesLeft = 0;
                _slots[i].Type = 0;
                _slots[i].Id = -1;
                CheckSilenceChannels(i);
            }
        }
    }

    /// <summary>Mirrors <c>stopAllSounds</c>.</summary>
    public override void StopAllSounds()
    {
        lock (_sync)
        {
            foreach (var slot in _slots)
            {
                slot.FramesLeft = 0;
                slot.Type = 0;
                slot.Id = -1;
            }

            _sfxPlaying = false;
            CheckSilenceChannels(0);
        }
    }

    /// <summary>Whether a sound is in a slot. Mirrors <c>getSoundStatus</c>.</summary>
    public override bool IsPlaying(int number)
    {
        lock (_sync)
        {
            return _slots.Any(s => s.Id == number);
        }
    }

    /// <summary>The chip's samples, the player ticked once a frame among them. Mirrors <c>readBuffer</c>, at full volume.</summary>
    protected override void RenderMono(Span<short> buffer)
    {
        lock (_sync)
        {
            for (int n = 0; n < buffer.Length; n++)
            {
                buffer[n] = _apu.GetSample();
                if (++_currentSample == _samplesPerFrame)
                {
                    _currentSample = 0;
                    try
                    {
                        Tick();
                    }
                    catch (IndexOutOfRangeException)
                    {
                        // a sound that reads past its resource stops everything, where the original read on into whatever followed
                        foreach (var slot in _slots)
                        {
                            slot.FramesLeft = 0;
                            slot.Type = 0;
                            slot.Id = -1;
                        }

                        WriteControl(0);
                    }
                }
            }
        }
    }

    /// <summary>A frame: the first effect playing, then the song. Mirrors <c>sound_play</c>.</summary>
    private void Tick()
    {
        if (_slots[0].FramesLeft != 0)
        {
            PlayEffect(0);
        }
        else if (_slots[1].FramesLeft != 0)
        {
            PlayEffect(1);
        }

        PlayMusic();
    }

    // the channels go quiet when no slot below this one plays (checkSilenceChannels)
    private void CheckSilenceChannels(int channel)
    {
        for (channel--; channel >= 0; channel--)
        {
            if (_slots[channel].FramesLeft != 0)
            {
                return;
            }
        }

        WriteControl(0);
    }

    /// <summary>An effect's next writes, up to a wait, a loop or its end. Mirrors <c>playSFX</c>.</summary>
    private void PlayEffect(int number)
    {
        var slot = _slots[number];
        if (--slot.FramesLeft != 0)
        {
            return;
        }

        while (true)
        {
            int a = slot.Next();
            if (a < 16)
            {
                a >>= 2;
                WriteControl((byte)(_apu.ReadStatus() | (1 << a)));
                _sfxPlaying = true;
                for (int register = 0; register < 4; register++)
                {
                    WriteChannel(a, register, slot.Next());
                }
            }
            else if (a == 0xFE)
            {
                slot.Offset = 2;
            }
            else if (a == 0xFF)
            {
                slot.Id = -1;
                slot.Type = 0;
                _sfxPlaying = false;
                WriteControl(0);
                if (number == 0 && _slots[1].FramesLeft != 0)
                {
                    _slots[1].FramesLeft = 1;
                    _sfxPlaying = true;
                }

                return;
            }
            else
            {
                slot.FramesLeft = slot.Next();
                return;
            }
        }
    }

    /// <summary>
    /// A frame of the song: when its wait is over, its next bytes start and release notes, up to
    /// the next wait; then each channel's command runs when its delay is over, and its volume
    /// slides. An effect playing silences the song. Mirrors <c>playMusic</c>.
    /// </summary>
    private void PlayMusic()
    {
        var music = _slots[MusicSlot];
        if (music.FramesLeft == 0)
        {
            return;
        }

        if (_sfxWasPlaying && !_sfxPlaying)
        {
            for (int x = 1; x >= 0; x--)
            {
                if (_channels[x].CommandLock != 0)
                {
                    _channels[x].Command = _channels[x].CommandLock;
                    _channels[x].FrameDelay = 1;
                }
            }
        }

        _sfxWasPlaying = _sfxPlaying;
        if (--music.FramesLeft == 0)
        {
            while (true)
            {
                int b = music.Next();
                if (b == 0xFF)
                {
                    music.Id = -1;
                    music.Type = 0;
                    b = 0;
                }
                else if (b == 0xFE)
                {
                    music.Offset = 0;
                    continue;
                }
                else
                {
                    if (b < _noteCount)
                    {
                        int instrument = _song[_instruments + b];
                        var channel = _channels[NesSoundTables.InstrumentChannels[instrument]];
                        channel.Pitch = _song[_pitches + b];
                        channel.CommandLock = NesSoundTables.StartCommands[instrument];
                        channel.Command = NesSoundTables.StartCommands[instrument];
                        channel.FrameDelay = 1;
                        continue;
                    }

                    b -= _noteCount;
                    if (b < 16)
                    {
                        var channel = _channels[NesSoundTables.InstrumentChannels[b]];
                        channel.CommandLock = 0;
                        channel.Command = NesSoundTables.ReleaseCommands[b];
                        channel.FrameDelay = 1;
                        continue;
                    }

                    b -= 16;
                }

                music.FramesLeft = b;
                break;
            }
        }

        for (int x = Channels - 1; x >= 0; x--)
        {
            var c = _channels[x];
            if (_slots[0].FramesLeft != 0 || _slots[1].FramesLeft != 0)
            {
                c.Volume = 0;
                c.FrameDelay = 0;
                continue;
            }

            if (c.FrameDelay != 0 && --c.FrameDelay == 0)
            {
                RunCommand(x, c);
            }

            c.Volume = Math.Clamp(c.Volume + c.VolumeDelta, 0, MaxVolume);
            WriteChannel(x, 0, (byte)((c.Volume >> 3) | c.EnvelopeFlags));
        }
    }

    // a channel's note at its pitch, or a pitch away (freqTable)
    private void WritePitch(int x, int pitch)
    {
        WriteChannel(x, 2, (byte)(NesSoundTables.Pitches[pitch] & 0xFF));
        WriteChannel(x, 3, (byte)(NesSoundTables.Pitches[pitch] >> 8));
    }

    // a note starting: its envelope flags, full volume, its registers, the channel on
    private void StartNote(int x, MusicChannel c, int envelope, int sweep, int volumeDelta = 0, int volume = 0x6F)
    {
        c.EnvelopeFlags = envelope;
        c.Volume = volume;
        c.VolumeDelta = volumeDelta;
        WriteChannel(x, 0, 0x00);
        WriteChannel(x, 1, (byte)sweep);
        WriteControl((byte)(_apu.ReadStatus() | (1 << x)));
        WritePitch(x, c.Pitch);
    }

    /// <summary>One of the instruments' commands, most chaining to the next. Mirrors the switch of <c>playMusic</c>.</summary>
    private void RunCommand(int x, MusicChannel c)
    {
        switch (c.Command)
        {
            case 0x00 or 0x13:
                c.VolumeDelta = -10;
                return;
            case 0x01 or 0x03 or 0x08 or 0x16:
                StartNote(x, c, 0x30, 0x7F);
                break;
            case 0x02:
                StartNote(x, c, 0xB0, 0x84);
                break;
            case 0x04:
                StartNote(x, c, 0x80, 0x7F);
                break;
            case 0x05:
                c.EnvelopeFlags = 0xF0;
                c.Volume = 0x6F;
                c.VolumeDelta = -15;
                WriteChannel(x, 1, 0x7F);
                WriteControl((byte)(_apu.ReadStatus() | (1 << x)));
                WritePitch(x, c.Pitch);
                break;
            case 0x06:
                c.Pitch += 0x18;
                StartNote(x, c, 0x80, 0x7F);
                break;
            case 0x07:
                WritePitch(x, c.Pitch - 0x0C);
                break;
            case 0x09:
                c.VolumeDelta = -2;
                WriteChannel(x, 1, 0x7F);
                WritePitch(x, c.Pitch);
                break;
            case 0x0A:
                WriteChannel(x, 1, 0x86);
                WritePitch(x, c.Pitch);
                break;
            case 0x0B or 0x1A:
                StartNote(x, c, 0x70, 0x7F);
                break;
            case 0x0C:
                c.EnvelopeFlags = 0xB0;
                break;
            case 0x0D or 0x0E or 0x10:
                c.EnvelopeFlags = 0x30;
                c.Volume = 0x5F;
                c.VolumeDelta = c.Command == 0x0D ? -22 : -6;
                WriteChannel(x, 0, 0x00);
                WriteControl((byte)(_apu.ReadStatus() | (1 << x)));
                WriteChannel(x, 2, (byte)(c.Pitch & 0xF));
                WriteChannel(x, 3, 0xFF);
                break;
            case 0x0F:
                break;
            case 0x11:
                WriteChannel(x, 2, (byte)(c.Pitch & 0xF));
                WriteChannel(x, 3, 0xFF);
                break;
            case 0x12:
                WriteChannel(x, 2, (byte)((c.Pitch + 3) & 0xF));
                WriteChannel(x, 3, 0xFF);
                break;
            case 0x14:
                c.VolumeDelta = -12;
                WriteChannel(x, 1, 0x8C);
                break;
            case 0x15:
                c.VolumeDelta = -12;
                WriteChannel(x, 1, 0x84);
                break;
            case 0x17:
                c.Pitch += 0x0C;
                StartNote(x, c, 0x80, 0x7F);
                break;
            case 0x18:
                c.EnvelopeFlags = 0x70;
                break;
            case 0x19:
                c.EnvelopeFlags = 0xB0;
                break;
            case 0x1B:
                c.EnvelopeFlags = 0x00;
                c.VolumeDelta = -10;
                return;
            default:
                return;
        }

        // chainCommand
        int command = c.Command;
        c.Command = NesSoundTables.NextCommands[command];
        c.FrameDelay = NesSoundTables.NextDelays[command];
    }

    private void WriteChannel(int channel, int register, byte value) => _apu.WriteRegister(4 * channel + register, value);

    private void WriteControl(byte value) => _apu.WriteRegister(0x15, value);
}
