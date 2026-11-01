using System.Buffers.Binary;

namespace Maniacal.Scumm.Sound;

/// <summary>
/// One of the Amiga's sounds as <c>Player_V2A</c> plays it: started on the mixer's channels, its
/// own ids being the sound's with the channel's number in the second byte, stepped 60 times a
/// second until it says it is done, and stopped. Mirrors <c>V2A_Sound</c> and its classes for
/// Maniac Mansion's sounds; their integer widths are ScummVM's.
/// </summary>
internal abstract class V2aSound
{
    /// <summary>The Amiga's colour clock, which the sounds' periods divide (<c>BASE_FREQUENCY</c>).</summary>
    protected const int BaseFrequency = 3579545;

    protected int Id { get; private set; }

    protected AmigaMixer Mixer { get; private set; } = null!;

    private readonly int _channels;

    protected V2aSound(int channels) => _channels = channels;

    public void Start(AmigaMixer mixer, int id, byte[] data)
    {
        Mixer = mixer;
        Id = id;
        Begin(data);
    }

    protected abstract void Begin(byte[] data);

    /// <summary>A 60th of a second; false when the sound is done.</summary>
    public abstract bool Update();

    /// <summary>Stops the sound's channels. Mirrors <c>V2A_Sound_Base::stop</c>.</summary>
    public virtual void Stop()
    {
        for (int i = 0; i < _channels; i++)
        {
            Mixer.StopChannel(Id | (i << 8));
        }

        Id = 0;
    }

    // a stretch of the sound's data, as the sounds copy it for a channel
    protected static byte[] Slice(byte[] data, int offset, int size)
    {
        var slice = new byte[size];
        if (offset < data.Length)
        {
            data.AsSpan(offset, Math.Min(size, data.Length - offset)).CopyTo(slice);
        }

        return slice;
    }

    // a 6-bit volume at the mixer's 8 bits, and at its 7 for two channels' sounds
    protected static int Volume8(int volume) => (volume << 2) | (volume >> 4);

    protected static int Volume7(int volume) => (volume << 1) | (volume >> 5);

    /// <summary>The class a table entry names, with its arguments. Mirrors <c>findSound</c>'s <c>new</c>.</summary>
    public static V2aSound Create(V2aSoundEntry entry)
    {
        var a = entry.Args;
        return entry.Kind switch
        {
            V2aKind.Single => new Single(a[0], a[1], a[2], a[3]),
            V2aKind.SingleLooped when a.Length == 6 => new SingleLooped(a[0], a[1], a[2], a[3], a[4], a[5]),
            V2aKind.SingleLooped => new SingleLooped(a[0], a[1], a[2], a[3], 0, a[1]),
            V2aKind.MultiLooped => new MultiLooped(a[0], a[1], a[2], a[3], a[4], a[5]),
            V2aKind.MultiLoopedDuration => new MultiLoopedDuration(a[0], a[1], a[2], a[3], a[4], a[5], a[6]),
            V2aKind.SingleLoopedPitchbend => new SingleLoopedPitchbend(a[0], a[1], a[2], a[3], a[4], a[5]),
            V2aKind.Maniac69 => new Maniac69(a[0], a[1], a[2], a[3]),
            V2aKind.ManiacDing => new ManiacDing(a[0], a[1], a[2], a[3], a[4]),
            V2aKind.ManiacTentacle => new ManiacTentacle(a[0], a[1], a[2], a[3], a[4]),
            V2aKind.Maniac59 => new Maniac59(a[0], a[1], a[2], a[3], a[4], a[5]),
            V2aKind.Maniac61 => new Maniac61(a[0], a[1], a[2], a[3]),
            V2aKind.ManiacPhone => new ManiacPhone(a[0], a[1], a[2], a[3], a[4], a[5], a[6], a[7], a[8]),
            V2aKind.Maniac46 => new Maniac46(a[0], a[1], a[2], a[3], a[4], a[5]),
            V2aKind.ManiacTypewriter => new ManiacTypewriter(a[0], a[1], a[2], a[3], entry.Durations!, entry.Flag),
            V2aKind.Maniac32 => new Maniac32(a[0], a[1], a[2], a[3], a[4]),
            V2aKind.Maniac44 => new Maniac44(a[0], a[1], a[2], a[3], a[4], a[5], a[6]),
            V2aKind.Music => new Music(a[0], a[1], a[2], a[3], a[4], a[5], a[6], entry.Flag),
            _ => throw new ArgumentOutOfRangeException(nameof(entry)),
        };
    }

    /// <summary>A waveform played once. Mirrors <c>V2A_Sound_Single</c>.</summary>
    private sealed class Single(int offset, int size, int period, int volume) : V2aSound(1)
    {
        private int _ticks;

        protected override void Begin(byte[] data)
        {
            Mixer.StartChannel(Id, Slice(data, offset, size), size, BaseFrequency / period, Volume8(volume));
            _ticks = 1 + 60 * size * period / BaseFrequency;
        }

        public override bool Update() => --_ticks != 0;
    }

    /// <summary>A waveform looped, from a point of it to its end. Mirrors <c>V2A_Sound_SingleLooped</c>.</summary>
    private sealed class SingleLooped(int offset, int size, int period, int volume, int loopOffset, int loopSize) : V2aSound(1)
    {
        protected override void Begin(byte[] data)
            => Mixer.StartChannel(Id, Slice(data, offset, size), size, BaseFrequency / period, Volume8(volume), loopOffset, loopOffset + loopSize);

        public override bool Update() => true;
    }

    /// <summary>A waveform looped on two channels, left and right, at two periods. Mirrors <c>V2A_Sound_MultiLooped</c>.</summary>
    private class MultiLooped(int offset, int size, int period1, int volume1, int period2, int volume2) : V2aSound(2)
    {
        protected override void Begin(byte[] data)
        {
            Mixer.StartChannel(Id | 0x000, Slice(data, offset, size), size, BaseFrequency / period1, Volume7(volume1), 0, size, -127);
            Mixer.StartChannel(Id | 0x100, Slice(data, offset, size), size, BaseFrequency / period2, Volume7(volume2), 0, size, 127);
        }

        public override bool Update() => true;
    }

    /// <summary>Two looped waveforms for a number of frames. Mirrors <c>V2A_Sound_MultiLoopedDuration</c>.</summary>
    private sealed class MultiLoopedDuration(int offset, int size, int period1, int volume1, int period2, int volume2, int frames)
        : MultiLooped(offset, size, period1, volume1, period2, volume2)
    {
        private int _ticks;

        protected override void Begin(byte[] data)
        {
            base.Begin(data);
            _ticks = 0;
        }

        public override bool Update() => ++_ticks < frames;
    }

    /// <summary>A looped waveform bending from one period to another, where it stays. Mirrors <c>V2A_Sound_SingleLoopedPitchbend</c>.</summary>
    private sealed class SingleLoopedPitchbend(int offset, int size, int period1, int period2, int volume, int step) : V2aSound(1)
    {
        private ushort _period;

        protected override void Begin(byte[] data)
        {
            _period = (ushort)period1;
            Mixer.StartChannel(Id, Slice(data, offset, size), size, BaseFrequency / _period, Volume8(volume), 0, size);
        }

        public override bool Update()
        {
            if (period1 < period2)
            {
                _period = (ushort)(_period + step);
                if (_period > period2)
                {
                    _period = (ushort)period2;
                }
                else
                {
                    Mixer.SetChannelFrequency(Id, BaseFrequency / _period);
                }
            }
            else
            {
                _period = (ushort)(_period - step);
                if (_period < period2)
                {
                    _period = (ushort)period2;
                }
                else
                {
                    Mixer.SetChannelFrequency(Id, BaseFrequency / _period);
                }
            }

            return true;
        }
    }

    /// <summary>A looped waveform dropping in pitch and fading to nothing, as the mansion explodes. Mirrors <c>V2A_Sound_Special_Maniac69</c>.</summary>
    private sealed class Maniac69(int offset, int size, int period, int volume) : V2aSound(1)
    {
        private ushort _period, _volume;

        protected override void Begin(byte[] data)
        {
            _volume = (ushort)((volume << 3) | (volume >> 3));
            _period = (ushort)period;
            Mixer.StartChannel(Id, Slice(data, offset, size), size, BaseFrequency / _period, _volume >> 1, 0, size);
        }

        public override bool Update()
        {
            _period += 2;
            Mixer.SetChannelFrequency(Id, BaseFrequency / _period);
            _volume--;
            if (_volume == 0)
            {
                return false;
            }

            Mixer.SetChannelVolume(Id, _volume >> 1);
            return true;
        }
    }

    /// <summary>A looped waveform fading in at one rate and out at another, the microwave's ding. Mirrors <c>V2A_Sound_Special_ManiacDing</c>.</summary>
    private sealed class ManiacDing(int offset, int size, int period, int fadeIn, int fadeOut) : V2aSound(1)
    {
        private int _volume, _direction;

        protected override void Begin(byte[] data)
        {
            _volume = 1;
            _direction = 0;
            Mixer.StartChannel(Id, Slice(data, offset, size), size, BaseFrequency / period, _volume, 0, size);
        }

        public override bool Update()
        {
            if (_direction == 0)
            {
                _volume += fadeIn;
                if (_volume > 0x3F)
                {
                    _volume = 0x3F;
                    _direction = 1;
                }
            }
            else
            {
                _volume -= fadeOut;
                if (_volume < 1)
                {
                    return false;
                }
            }

            Mixer.SetChannelVolume(Id, Volume8(_volume));
            return true;
        }
    }

    /// <summary>A looped waveform falling in pitch, fading as it passes a period. Mirrors <c>V2A_Sound_Special_ManiacTentacle</c>.</summary>
    private sealed class ManiacTentacle(int offset, int size, int period1, int period2, int step) : V2aSound(1)
    {
        private ushort _period;
        private int _volume;

        protected override void Begin(byte[] data)
        {
            _period = (ushort)period1;
            _volume = 0x3F;
            Mixer.StartChannel(Id, Slice(data, offset, size), size, BaseFrequency / _period, Volume8(_volume), 0, size);
        }

        public override bool Update()
        {
            if (_period > period2)
            {
                _volume = 0x3F + period2 - _period;
            }

            if (_volume < 1)
            {
                return false;
            }

            _period = (ushort)(_period + step);
            Mixer.SetChannelFrequency(Id, BaseFrequency / _period);
            Mixer.SetChannelVolume(Id, Volume8(_volume));
            return true;
        }
    }

    /// <summary>A looped waveform bending down in pitch and back up, an electronic noise. Mirrors <c>V2A_Sound_Special_Maniac59</c>.</summary>
    private sealed class Maniac59(int offset, int size, int period1, int period2, int step, int volume) : V2aSound(1)
    {
        private ushort _period;
        private int _direction;

        protected override void Begin(byte[] data)
        {
            _period = (ushort)period1;
            _direction = 2;
            Mixer.StartChannel(Id, Slice(data, offset, size), size, BaseFrequency / _period, Volume8(volume), 0, size);
        }

        public override bool Update()
        {
            if (_direction == 2)
            {
                _period = (ushort)(_period + step);
                if (_period > period2)
                {
                    _period = (ushort)period2;
                    _direction = 1;
                }

                Mixer.SetChannelFrequency(Id, BaseFrequency / _period);
            }
            else if (_direction == 1)
            {
                _period = (ushort)(_period - step);
                if (_period < period1)
                {
                    _period = (ushort)period1;
                    _direction = 0;
                }

                Mixer.SetChannelFrequency(Id, BaseFrequency / _period);
            }

            return true;
        }
    }

    /// <summary>A looped waveform falling in pitch and slowly fading. Mirrors <c>V2A_Sound_Special_Maniac61</c>.</summary>
    private sealed class Maniac61(int offset, int size, int period1, int period2) : V2aSound(1)
    {
        private ushort _period;
        private byte _volume;

        protected override void Begin(byte[] data)
        {
            _period = (ushort)period1;
            _volume = 0x3F;
            Mixer.StartChannel(Id, Slice(data, offset, size), size, BaseFrequency / _period, Volume8(_volume), 0, size);
        }

        public override bool Update()
        {
            _period++;
            if ((_period & 3) == 0)
            {
                _volume--;
            }

            if (_period == period2 || _volume == 0)
            {
                return false;
            }

            Mixer.SetChannelFrequency(Id, BaseFrequency / _period);
            Mixer.SetChannelVolume(Id, Volume8(_volume));
            return true;
        }
    }

    /// <summary>Two looped waveforms sounding and pausing for a number of frames, a telephone ringing. Mirrors <c>V2A_Sound_Special_ManiacPhone</c>.</summary>
    private sealed class ManiacPhone(int offset, int size, int period1, int volume1, int period2, int volume2, int frames, int playWidth, int loopWidth)
        : V2aSound(2)
    {
        private byte[] _data = [];
        private int _ticks, _loop;

        protected override void Begin(byte[] data)
        {
            _data = data;
            SoundOn();
            _ticks = 0;
            _loop = 0;
        }

        public override bool Update()
        {
            if (_loop == playWidth)
            {
                Mixer.StopChannel(Id | 0x000);
                Mixer.StopChannel(Id | 0x100);
            }

            if (_loop == loopWidth)
            {
                _loop = 0;
                SoundOn();
            }

            _loop++;
            return ++_ticks < frames;
        }

        private void SoundOn()
        {
            Mixer.StartChannel(Id | 0x000, Slice(_data, offset, size), size, BaseFrequency / period1, Volume7(volume1), 0, size, -127);
            Mixer.StartChannel(Id | 0x100, Slice(_data, offset, size), size, BaseFrequency / period2, Volume7(volume2), 0, size, 127);
        }
    }

    /// <summary>A waveform played again and again at an interval, a wrench on a pipe. Mirrors <c>V2A_Sound_Special_Maniac46</c>.</summary>
    private sealed class Maniac46(int offset, int size, int period, int volume, int loopWidth, int loops) : V2aSound(1)
    {
        private byte[] _data = [];
        private int _loop, _loopCount;

        protected override void Begin(byte[] data)
        {
            _data = data;
            SoundOn();
            _loop = 0;
            _loopCount = 0;
        }

        public override bool Update()
        {
            if (++_loop == loopWidth)
            {
                _loop = 0;
                if (++_loopCount == loops)
                {
                    return false;
                }

                Mixer.StopChannel(Id);
                SoundOn();
            }

            return true;
        }

        private void SoundOn() => Mixer.StartChannel(Id, Slice(_data, offset, size), size, BaseFrequency / period, Volume8(volume));
    }

    /// <summary>A waveform played at uneven intervals, looped or not, a typewriter. Mirrors <c>V2A_Sound_Special_ManiacTypewriter</c>.</summary>
    private sealed class ManiacTypewriter(int offset, int size, int period, int volume, byte[] durations, bool looped) : V2aSound(1)
    {
        private byte[] _data = [];
        private int _ticks, _duration;

        protected override void Begin(byte[] data)
        {
            _data = data;
            SoundOn();
            _duration = 0;
            _ticks = durations[_duration++];
        }

        public override bool Update()
        {
            if (--_ticks == 0)
            {
                if (_duration == durations.Length)
                {
                    if (!looped)
                    {
                        return false;
                    }

                    _duration = 0;
                }

                Mixer.StopChannel(Id);
                SoundOn();
                _ticks = durations[_duration++];
            }

            return true;
        }

        private void SoundOn() => Mixer.StartChannel(Id, Slice(_data, offset, size), size, BaseFrequency / period, Volume8(volume));
    }

    /// <summary>Two looped waveforms bending up at rates that change, a siren-like noise. Mirrors <c>V2A_Sound_Special_Maniac44</c>.</summary>
    private sealed class Maniac44(int offset1, int size1, int offset2, int size2, int period1, int period2, int volume) : V2aSound(2)
    {
        private static readonly int[] Steps = [0, 2, 2, 3, 4, 8, 15, 2];

        private byte[] _data = [];
        private int _period;
        private ushort _loop, _step;

        protected override void Begin(byte[] data)
        {
            _data = data;
            _loop = 1;
            _step = 2;
            _period = period1;
            SoundOn(offset1, size1);
        }

        public override bool Update()
        {
            Mixer.SetChannelFrequency(Id | 0x000, BaseFrequency / _period);
            Mixer.SetChannelFrequency(Id | 0x100, BaseFrequency / (_period + 3));
            _period -= _step;
            if (_loop == 7)
            {
                return BaseFrequency / _period < 65536;
            }

            if (_period >= period2)
            {
                return true;
            }

            _period = period1;
            _step = (ushort)Steps[++_loop];
            if (_loop == 7)
            {
                Mixer.StopChannel(Id | 0x000);
                Mixer.StopChannel(Id | 0x100);
                SoundOn(offset2, size2);
            }

            return true;
        }

        private void SoundOn(int offset, int size)
        {
            Mixer.StartChannel(Id | 0x000, Slice(_data, offset, size), size, BaseFrequency / _period, Volume7(volume), 0, size, -127);
            Mixer.StartChannel(Id | 0x100, Slice(_data, offset, size), size, BaseFrequency / (_period + 3), Volume7(volume), 0, size, 127);
        }
    }

    /// <summary>Four looped waveforms, each bending between two periods, the siren. Mirrors <c>V2A_Sound_Special_Maniac32</c>.</summary>
    private sealed class Maniac32(int offset1, int size1, int offset2, int size2, int volume) : V2aSound(4)
    {
        private readonly ushort[] _periods = [0x02D0, 0x0122, 0x02BC, 0x010E];
        private readonly short[] _steps = [-0x000A, 0x000A, -0x0005, 0x0007];

        protected override void Begin(byte[] data)
        {
            Mixer.StartChannel(Id | 0x000, Slice(data, offset1, size1), size1, BaseFrequency / _periods[0], volume, 0, size1, -127);
            Mixer.StartChannel(Id | 0x100, Slice(data, offset2, size2), size2, BaseFrequency / _periods[1], volume, 0, size2, 127);
            Mixer.StartChannel(Id | 0x200, Slice(data, offset1, size1), size1, BaseFrequency / _periods[2], volume, 0, size1, 127);
            Mixer.StartChannel(Id | 0x300, Slice(data, offset2, size2), size2, BaseFrequency / _periods[3], volume, 0, size2, -127);
        }

        public override bool Update()
        {
            Bend(0, 0x00AA, 0x00FA);
            Bend(1, 0x019A, 0x03B6);
            Bend(2, 0x00AA, 0x00FA);
            Bend(3, 0x019A, 0x03B6);
            for (int i = 0; i < 4; i++)
            {
                Mixer.SetChannelFrequency(Id | (i << 8), BaseFrequency / _periods[i]);
            }

            return true;
        }

        private void Bend(int i, int min, int max)
        {
            _periods[i] = (ushort)(_periods[i] + _steps[i]);
            if (_periods[i] <= min)
            {
                _periods[i] = (ushort)min;
                _steps[i] = (short)-_steps[i];
            }

            if (_periods[i] >= max)
            {
                _periods[i] = (ushort)max;
                _steps[i] = (short)-_steps[i];
            }
        }
    }

    /// <summary>
    /// A song on four channels: each a list of notes, 16 bytes each (when, period, duration,
    /// channel, instrument), the instruments' samples and their volume envelopes, looped or not.
    /// Mirrors <c>V2A_Sound_Music</c>.
    /// </summary>
    private sealed class Music(int instruments, int volumes, int track1, int track2, int track3, int track4, int samples, bool looped) : V2aSound(4)
    {
        private sealed class Track
        {
            public ushort Start, Data, VolumeBase, Channel, Duration, Ticks;
            public byte VolumePosition;
        }

        private readonly Track[] _tracks = [new() { Start = (ushort)track1 }, new() { Start = (ushort)track2 }, new() { Start = (ushort)track3 }, new() { Start = (ushort)track4 }];
        private byte[] _data = [];

        private int Word(int at) => at + 1 < _data.Length ? BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(at)) : 0;

        protected override void Begin(byte[] data)
        {
            _data = data;
            foreach (var track in _tracks)
            {
                track.Data = track.Start;
                track.VolumeBase = 0;
                track.VolumePosition = 0;
                track.Channel = 0;
                track.Duration = 0;
                track.Ticks = 0;
            }

            Update();
        }

        public override bool Update()
        {
            int ended = 0;
            foreach (var track in _tracks)
            {
                int id = Id | (track.Channel << 8);
                if (track.Duration != 0)
                {
                    if (--track.Duration == 0)
                    {
                        Mixer.StopChannel(id);
                    }
                    else
                    {
                        Mixer.SetChannelVolume(id, Word(track.VolumeBase + (track.VolumePosition++ << 1)));
                        if (track.VolumePosition == 0)
                        {
                            Mixer.StopChannel(id);
                            track.Duration = 0;
                        }
                    }
                }

                if (track.Data == 0)
                {
                    ended++;
                    continue;
                }

                if (Word(track.Data) <= track.Ticks)
                {
                    if (Word(track.Data + 2) == 0xFFFF)
                    {
                        if (looped)
                        {
                            track.Data = track.Start;
                            track.Ticks = 0;
                            if (Word(track.Data) > 0)
                            {
                                track.Ticks++;
                                continue;
                            }
                        }
                        else
                        {
                            track.Data = 0;
                            ended++;
                            continue;
                        }
                    }

                    int frequency = BaseFrequency / Word(track.Data + 2);
                    int instrument = Word(track.Data + 8);
                    int at = instruments + (instrument << 5);
                    track.VolumeBase = (ushort)(volumes + (Word(at) << 9));
                    track.VolumePosition = 0;
                    track.Channel = (ushort)(Word(track.Data + 6) & 0x3);
                    if (track.Duration != 0)
                    {
                        Mixer.StopChannel(Id | (track.Channel << 8));
                    }

                    track.Duration = (ushort)Word(track.Data + 4);
                    int volume = Word(track.VolumeBase + (track.VolumePosition++ << 1));
                    int pan = track.Channel is 0 or 3 ? -127 : 127;
                    int offset = Word(at + 0x14), length = Word(at + 0x18), loopOffset = Word(at + 0x16), loopLength = Word(at + 0x10);
                    var sample = new byte[length + loopLength];
                    Slice(_data, samples + offset, length).CopyTo(sample, 0);
                    Slice(_data, samples + loopOffset, loopLength).CopyTo(sample, length);
                    Mixer.StartChannel(Id | (track.Channel << 8), sample, sample.Length, frequency, volume, length, loopLength + length, pan);
                    track.Data += 16;
                }

                track.Ticks++;
            }

            return ended != 4;
        }

        public override void Stop()
        {
            foreach (var track in _tracks)
            {
                if (track.Duration != 0)
                {
                    Mixer.StopChannel(Id | (track.Channel << 8));
                }
            }
        }
    }
}
