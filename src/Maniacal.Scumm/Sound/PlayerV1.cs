using System.Buffers.Binary;

namespace Maniacal.Scumm.Sound;

/// <summary>
/// The music and sound player of version 1: a sound is a chain of chunks, each a different kind
/// of effect (notes, a sweep of the pitch, clicks, noise), stepped at a rate the chunk sets with
/// the timer's divisor. The PC speaker has one voice; the PCjr has three, with attack, decay and
/// sustain, and noise. Mirrors ScummVM's <c>Player_V1</c>.
/// </summary>
/// <remarks>
/// For the speaker the sound's data starts at byte 6 of the resource, for the PCjr where the word
/// at byte 4 says; its first byte is the priority, with bit 7 set when it may be resumed.
/// </remarks>
public sealed class PlayerV1 : SpeakerSynth
{
    private const int TimerBase = 1193000;

    // ScummVM's Player_V2 clock, which version 1 runs at until a sound sets its own
    private const int DefaultTicksPerSecond = 236;

    private readonly Voice[] _voices = [new(), new(), new(), new()];
    private readonly uint _multiplexStep;
    private int _nextChunk = -1;
    private int _repeatChunk = -1;
    private int _chunkType;
    private uint _repeatCounter;
    private int _forcedLevel;
    private ushort _randomShift;
    private uint _timeLeft;
    private uint _start;
    private uint _end;
    private int _delta;
    private uint _timeLeft2;
    private uint _start2;
    private int _delta2;

    // what the slide of PCjr chunk 1 changes: a voice and whether its volume or its divisor
    private (int Voice, bool Volume) _target;
    private (int Voice, bool Volume) _target2;

    public PlayerV1(int sampleRate, bool pcjr)
        : base(sampleRate, pcjr, DefaultTicksPerSecond)
    {
        _multiplexStep = (uint)(((long)sampleRate << FixpShift) / TimerBase);
        ClearVoices();
    }

    /// <summary>Version 1 has no music timer. Mirrors <c>Player_V1::getMusicTimer</c>.</summary>
    public override int MusicTimer => 0;

    /// <summary>
    /// A sound of at least the playing one's priority starts at once; the one it interrupts waits
    /// to play after it if it may be resumed. Mirrors <c>Player_V1::startSound</c>.
    /// </summary>
    public override void StartSound(int number, byte[] data)
    {
        lock (Sync)
        {
            if (data.Length < 8)
            {
                return;
            }

            int offset = Pcjr ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4)) : 6;
            if (offset >= data.Length)
            {
                return;
            }

            int currentPriority = CurrentData is { Length: > 0 } current ? current[0] & 0x7F : 0;
            int priority = data[offset] & 0x7F;
            if (CurrentNumber == 0 || currentPriority <= priority)
            {
                if (CurrentData is { Length: > 0 } playing && (playing[0] & 0x80) != 0)
                {
                    NextNumber = CurrentNumber;
                    NextData = playing;
                }

                ChainSound(number, data[offset..]);
            }
        }
    }

    public override void StopSound(int number)
    {
        lock (Sync)
        {
            if (NextNumber == number)
            {
                NextNumber = 0;
                NextData = null;
            }

            if (CurrentNumber == number)
            {
                ClearVoices();
                _repeatChunk = _nextChunk = -1;
                CurrentNumber = 0;
                CurrentData = null;
                ChainNextSound();
            }
        }
    }

    public override void StopAllSounds()
    {
        lock (Sync)
        {
            ClearVoices();
            _repeatChunk = _nextChunk = -1;
            NextNumber = CurrentNumber = 0;
            NextData = CurrentData = null;
        }
    }

    /// <summary>Mirrors <c>Player_V1::chainSound</c>: the chunks start after the priority (and, for the speaker, a word more).</summary>
    protected override void ChainSound(int number, byte[]? data)
    {
        ClearVoices();
        CurrentNumber = number;
        CurrentData = data;
        if (data is null)
        {
            _repeatChunk = _nextChunk = -1;
            return;
        }

        _repeatChunk = _nextChunk = Pcjr ? 2 : 4;
        if (Pcjr)
        {
            ParsePcjrChunk();
        }
        else
        {
            ParseSpeakerChunk();
        }
    }

    protected override void NextTick()
    {
        if (_nextChunk >= 0)
        {
            if (Pcjr)
            {
                NextPcjrCommand();
            }
            else
            {
                NextSpeakerCommand();
            }
        }
    }

    /// <summary>A tone at the first voice's divisor, or a forced level for clicks. Mirrors <c>Player_V1::generateSpkSamples</c>.</summary>
    protected override void GenerateSpeakerSamples(Span<short> samples)
    {
        samples.Clear();
        if (_voices[0].Frequency == 0)
        {
            if (_forcedLevel != 0)
            {
                samples.Fill((short)(_forcedLevel * VolumeTable[0]));
            }
            else if (Level == 0)
            {
                return;
            }
        }
        else
        {
            SquareGenerator(0, (int)_voices[0].Frequency, 0, 0, samples);
        }

        LowPassFilter(samples);
    }

    /// <summary>Mirrors <c>Player_V1::generatePCjrSamples</c>.</summary>
    protected override void GeneratePcjrSamples(Span<short> samples)
    {
        samples.Clear();
        bool sounding = false;
        if (_forcedLevel != 0)
        {
            samples.Fill((short)(_forcedLevel * VolumeTable[0]));
            sounding = true;
        }

        for (int i = 1; i < 3; i++)
        {
            uint frequency = _voices[i].Frequency;
            if (frequency != 0)
            {
                for (int j = 0; j < i; j++)
                {
                    if (frequency == _voices[j].Frequency)
                    {
                        SyncPhase(i, j);
                    }
                }
            }
        }

        for (int i = 0; i < 4; i++)
        {
            uint frequency = _voices[i].Frequency;
            int volume = (int)Math.Min(_voices[i].Volume, 15u);
            if (VolumeTable[volume] == 0)
            {
                IdleTimer(i, samples.Length);
            }
            else if (i < 3)
            {
                sounding = true;
                SquareGenerator(i, (int)frequency, volume, 0, samples);
            }
            else
            {
                int feedback = (frequency & 4) != 0 ? WhiteNoise : PeriodicNoise;
                uint n = frequency & 3;
                frequency = n == 3 ? 2 * _voices[2].Frequency : 1u << (int)(5 + n);
                sounding = true;
                SquareGenerator(i, (int)frequency, volume, feedback, samples);
            }
        }

        if (Level != 0 || sounding)
        {
            LowPassFilter(samples);
        }
    }

    /// <summary>A word of the sound; past its end (or before its start), 0xFFFF, which ends it, as the original would only by luck.</summary>
    private ushort Word(int offset)
        => CurrentData is { } data && offset >= 0 && offset + 2 <= data.Length ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset)) : (ushort)0xFFFF;

    private void ClearVoices()
    {
        foreach (var voice in _voices)
        {
            voice.Frequency = 0;
            voice.Volume = 15;
        }
    }

    /// <summary>The sequencer steps at the timer's rate over a divisor, 0 meaning 65536. Mirrors <c>Player_V1::set_mplex</c>.</summary>
    private void SetMultiplex(uint multiplex)
    {
        if (multiplex == 0)
        {
            multiplex = 65536;
        }

        TickLength = _multiplexStep * multiplex;
    }

    /// <summary>The sound ends; the queued one, if any, starts.</summary>
    private void EndSound()
    {
        CurrentNumber = 0;
        CurrentData = null;
        _nextChunk = -1;
        ChainNextSound();
    }

    /// <summary>
    /// Starts the next speaker chunk: -1 ends the sound, -2 marks where to repeat from, -3 repeats,
    /// 0 is a run of notes, 1 a sweep of the pitch, 2 clicks speeding up or slowing down, 3 noise.
    /// Mirrors <c>Player_V1::parseSpeakerChunk</c>.
    /// </summary>
    private void ParseSpeakerChunk()
    {
        SetMultiplex(3000);
        _forcedLevel = 0;

        while (true)
        {
            _chunkType = Word(_nextChunk);
            _nextChunk += 2;
            switch (_chunkType)
            {
                case 0xFFFF:
                    _voices[0].Frequency = 0;
                    EndSound();
                    return;
                case 0xFFFE:
                    _repeatChunk = _nextChunk;
                    continue;
                case 0xFFFD:
                    _nextChunk = _repeatChunk;
                    continue;
                case 0xFFFC:
                    continue;
                case 0:
                    _timeLeft = 1;
                    SetMultiplex(Word(_nextChunk));
                    _nextChunk += 2;
                    return;
                case 1:
                    SetMultiplex(Word(_nextChunk));
                    _start = Word(_nextChunk + 2);
                    _end = Word(_nextChunk + 4);
                    _delta = (short)Word(_nextChunk + 6);
                    _repeatCounter = Word(_nextChunk + 8);
                    _voices[0].Frequency = _start;
                    _nextChunk += 10;
                    return;
                case 2 or 3:
                    _start = Word(_nextChunk);
                    _end = Word(_nextChunk + 2);
                    _delta = (short)Word(_nextChunk + 4);
                    _voices[0].Frequency = 0;
                    _nextChunk += 6;
                    _forcedLevel = -1;
                    return;
                default:
                    return;
            }
        }
    }

    /// <summary>A tick of the speaker chunk playing. Mirrors <c>Player_V1::nextSpeakerCmd</c>.</summary>
    private void NextSpeakerCommand()
    {
        switch (_chunkType)
        {
            case 0:
                if (--_timeLeft != 0)
                {
                    return;
                }

                _timeLeft = Word(_nextChunk);
                _nextChunk += 2;
                if (_timeLeft == 0xFFFB)
                {
                    _timeLeft = Word(_nextChunk);
                    _nextChunk += 2;
                }

                if (_timeLeft == 0)
                {
                    ParseSpeakerChunk();
                }
                else
                {
                    _voices[0].Frequency = Word(_nextChunk);
                    _nextChunk += 2;
                }

                break;

            case 1:
                _voices[0].Frequency = (uint)((_voices[0].Frequency + _delta) & 0xFFFF);
                if (_voices[0].Frequency == _end)
                {
                    if (--_repeatCounter == 0)
                    {
                        ParseSpeakerChunk();
                        return;
                    }

                    _voices[0].Frequency = _start;
                }

                break;

            case 2:
                _start = (uint)((_start + _delta) & 0xFFFF);
                if (_start == _end)
                {
                    ParseSpeakerChunk();
                    return;
                }

                SetMultiplex(_start);
                _forcedLevel = -_forcedLevel;
                break;

            case 3:
                {
                    _start = (uint)((_start + _delta) & 0xFFFF);
                    if (_start == _end)
                    {
                        ParseSpeakerChunk();
                        return;
                    }

                    ushort shift = (ushort)(_randomShift + 0x9248);
                    shift = (ushort)((shift >> 3) | (shift << 13));
                    _randomShift = shift;
                    SetMultiplex((_start & shift) | 0x180);
                    _forcedLevel = -_forcedLevel;
                    break;
                }
        }
    }

    /// <summary>
    /// Starts the next PCjr chunk: 0 gives each voice a run of notes with an envelope, 1 slides a
    /// divisor or volume (with a second slide alongside), 2 clicks, 3 shapes the noise's volume.
    /// Mirrors <c>Player_V1::parsePCjrChunk</c>.
    /// </summary>
    private void ParsePcjrChunk()
    {
        SetMultiplex(3000);
        _forcedLevel = 0;

        while (true)
        {
            _chunkType = Word(_nextChunk);
            _nextChunk += 2;
            switch (_chunkType)
            {
                case 0xFFFF:
                    ClearVoices();
                    _repeatChunk = -1;
                    EndSound();
                    return;
                case 0xFFFE:
                    _repeatChunk = _nextChunk;
                    continue;
                case 0xFFFD:
                    _nextChunk = _repeatChunk;
                    continue;
                case 0xFFFC:
                    continue;

                case 0:
                    SetMultiplex(Word(_nextChunk));
                    _nextChunk += 2;
                    for (int i = 0; i < 4; i++)
                    {
                        int at = Word(_nextChunk);
                        _nextChunk += 2;
                        var voice = _voices[i];
                        if (at == 0xFFFF)
                        {
                            voice.Commands = -1;
                            continue;
                        }

                        voice.Attack = Word(at);
                        voice.Decay = Word(at + 2);
                        voice.Level = Word(at + 4);
                        voice.Sustain1 = Word(at + 6);
                        voice.Sustain2 = Word(at + 8);
                        voice.NoteLength = 1;
                        voice.Volume = 15;
                        voice.Commands = at + 10;
                    }

                    return;

                case 1:
                    {
                        SetMultiplex(Word(_nextChunk));
                        int at = Word(_nextChunk + 2);
                        _voices[0].Commands = at != 0xFFFF ? at : -1;
                        int target = Word(_nextChunk + 4);
                        _start = Word(_nextChunk + 6);
                        _delta = (short)Word(_nextChunk + 8);
                        _timeLeft = Word(_nextChunk + 10);
                        _nextChunk += 12;
                        if (target >= 0xE0)
                        {
                            _voices[3].Frequency = (uint)(target & 0xF);
                            _target = (3, true);
                        }
                        else
                        {
                            int voice = (target & 0x60) >> 5;
                            _target = (voice, false);
                            _voices[voice].Volume = 0;
                        }

                        Set(_target, _start);
                        if (_voices[0].Commands >= 0)
                        {
                            int commands = _voices[0].Commands;
                            int second = Word(commands);
                            _start2 = Word(commands + 2);
                            _delta2 = (short)Word(commands + 4);
                            _timeLeft2 = Word(commands + 6);
                            _voices[0].Commands += 8;
                            if (_target == (3, true))
                            {
                                second = (second & 0x70) >> 4;
                                _target2 = (second >> 1, (second & 1) != 0);
                            }
                            else
                            {
                                int voice = (second & 0x60) >> 5;
                                _target2 = (voice, false);
                                _voices[voice].Volume = 0;
                            }

                            Set(_target2, _start2);
                        }

                        return;
                    }

                case 2:
                    _start = Word(_nextChunk);
                    _end = Word(_nextChunk + 2);
                    _delta = (short)Word(_nextChunk + 4);
                    _voices[0].Frequency = 0;
                    _nextChunk += 6;
                    _forcedLevel = -1;
                    return;

                case 3:
                    {
                        SetMultiplex(Word(_nextChunk));
                        int noise = Word(_nextChunk + 2);
                        _voices[3].Frequency = (uint)(noise & 0xF);
                        if ((noise & 3) == 3)
                        {
                            _nextChunk += 2;
                            _voices[2].Frequency = Word(_nextChunk + 2);
                        }

                        _voices[3].Volume = Word(_nextChunk + 4);
                        _repeatCounter = Word(_nextChunk + 6);
                        _delta = (short)Word(_nextChunk + 8);
                        _nextChunk += 10;
                        return;
                    }

                default:
                    return;
            }
        }
    }

    /// <summary>A tick of the PCjr chunk playing. Mirrors <c>Player_V1::nextPCjrCmd</c>.</summary>
    private void NextPcjrCommand()
    {
        switch (_chunkType)
        {
            case 0:
                for (int i = 0; i < 4; i++)
                {
                    var voice = _voices[i];
                    if (voice.Commands < 0)
                    {
                        continue;
                    }

                    if (--voice.NoteLength == 0)
                    {
                        int length = Word(voice.Commands);
                        if (length >= 0xFFFE)
                        {
                            if (length == 0xFFFE)
                            {
                                _nextChunk = 2;
                            }

                            ParsePcjrChunk();
                            return;
                        }

                        voice.NoteLength = (uint)(4 * length);
                        int frequency = Word(voice.Commands + 2);
                        if (frequency == 0)
                        {
                            voice.HullCounter = 4;
                            voice.SustainCounter = (int)voice.Sustain2;
                        }
                        else
                        {
                            voice.HullCounter = 1;
                            voice.Frequency = (uint)frequency;
                        }

                        voice.Commands += 4;
                    }

                    switch (voice.HullCounter)
                    {
                        case 1:
                            voice.Volume -= voice.Attack;
                            if ((int)voice.Volume <= 0)
                            {
                                voice.Volume = 0;
                                voice.HullCounter++;
                            }

                            break;
                        case 2:
                            voice.Volume += voice.Decay;
                            if (voice.Volume >= voice.Level)
                            {
                                voice.Volume = voice.Level;
                                voice.HullCounter++;
                            }

                            break;
                        case 4:
                            if (--voice.SustainCounter < 0)
                            {
                                voice.SustainCounter = (int)voice.Sustain2;
                                voice.Volume += voice.Sustain1;
                                if ((int)voice.Volume >= 15)
                                {
                                    voice.Volume = 15;
                                    voice.HullCounter++;
                                }
                            }

                            break;
                    }
                }

                break;

            case 1:
                _start = (uint)(_start + _delta);
                Set(_target, _start);
                if (--_timeLeft == 0)
                {
                    _start = Word(_nextChunk);
                    _nextChunk += 2;
                    if (_start == 0xFFFF)
                    {
                        ParsePcjrChunk();
                        return;
                    }

                    _delta = (short)Word(_nextChunk);
                    _timeLeft = Word(_nextChunk + 2);
                    _nextChunk += 4;
                    Set(_target, _start);
                }

                if (_voices[0].Commands >= 0)
                {
                    _start2 = (uint)(_start2 + _delta2);
                    Set(_target2, _start2);
                    if (--_timeLeft2 == 0)
                    {
                        int commands = _voices[0].Commands;
                        _start2 = Word(commands);
                        if (_start2 == 0xFFFF)
                        {
                            _nextChunk = commands + 2;
                            ParsePcjrChunk();
                            return;
                        }

                        _delta2 = (short)Word(commands + 2);
                        _timeLeft2 = Word(commands + 4);
                        _voices[0].Commands += 6;
                    }
                }

                break;

            case 2:
                _start = (uint)(_start + _delta);
                if (_start == _end)
                {
                    ParsePcjrChunk();
                    return;
                }

                SetMultiplex(_start);
                _forcedLevel = -_forcedLevel;
                break;

            case 3:
                {
                    int volume = (int)_voices[3].Volume + _delta;
                    if (volume >= 15)
                    {
                        _voices[3].Volume = 15;
                    }
                    else if (volume <= 0)
                    {
                        _voices[3].Volume = 0;
                    }
                    else
                    {
                        _voices[3].Volume = (uint)volume;
                        break;
                    }

                    if (--_repeatCounter == 0)
                    {
                        ParsePcjrChunk();
                        return;
                    }

                    _delta = Word(_nextChunk);
                    _nextChunk += 2;
                    break;
                }
        }
    }

    private void Set((int Voice, bool Volume) target, uint value)
    {
        if (target.Volume)
        {
            _voices[target.Voice].Volume = value;
        }
        else
        {
            _voices[target.Voice].Frequency = value;
        }
    }

    /// <summary>A voice of version 1. Mirrors <c>channel_data_v1</c>; offsets into the sound stand for its pointers, -1 for none.</summary>
    private sealed class Voice
    {
        public uint Frequency;
        public uint Volume;
        public int Commands = -1;
        public uint NoteLength;
        public uint HullCounter;
        public uint Attack;
        public uint Decay;
        public uint Level;
        public uint Sustain1;
        public uint Sustain2;
        public int SustainCounter;
    }
}
