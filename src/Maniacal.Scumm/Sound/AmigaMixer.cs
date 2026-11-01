namespace Maniacal.Scumm.Sound;

/// <summary>
/// The Amiga's sample channels as ScummVM mixes them for the Amiga's version 2 sounds: up to 24
/// channels of signed 8-bit samples, each at its own rate, volume and pan, a section of each looped
/// or not, resampled to the output's rate linearly into a stereo buffer, and an update routine
/// called at a fixed rate among the samples. Mirrors <c>Player_MOD</c>, with the raw and looping
/// streams it plays from (<c>makeRawStream</c>, <c>SubLoopingAudioStream</c>).
/// </summary>
internal sealed class AmigaMixer(int sampleRate)
{
    private const int MaxChannels = 24, MaxMixerVolume = 256, MaxFrequency = 31400;

    private readonly Channel[] _channels = [.. Enumerable.Range(0, MaxChannels).Select(_ => new Channel())];
    private Action? _update;
    private uint _mixAmount, _mixPosition;

    private sealed class Channel
    {
        public int Id;
        public byte Volume;
        public sbyte Pan;
        public ushort Frequency;
        public uint Counter;
        public short Position;
        public SampleStream? Input;

        public void Clear()
        {
            Input = null;
            Id = 0;
            Volume = 0;
            Frequency = 0;
            Counter = 0;
            Position = 0;
        }
    }

    // a channel's samples, signed bytes, each read as a 16-bit sample; with a loop, the section
    // from its start to its end again and again once the end is reached
    private sealed class SampleStream(byte[] data, int size, int loopStart, int loopEnd)
    {
        private readonly bool _looping = loopStart != loopEnd;
        private int _position;

        public bool Read(out short sample)
        {
            if (_looping && _position == loopEnd)
            {
                _position = loopStart;
            }

            if (_position >= size || _position >= data.Length)
            {
                sample = 0;
                return false;
            }

            sample = (short)((sbyte)data[_position++] << 8);
            return true;
        }
    }

    /// <summary>The routine to call, and how many times a second among the samples. Mirrors <c>setUpdateProc</c>.</summary>
    public void SetUpdate(Action update, int frequency)
    {
        _update = update;
        _mixAmount = (uint)(sampleRate / frequency);
    }

    /// <summary>
    /// Starts a channel of an id on samples, at a rate and volume and pan, its section from
    /// <paramref name="loopStart"/> to <paramref name="loopEnd"/> looped when they differ. Mirrors
    /// <c>startChannel</c>.
    /// </summary>
    public void StartChannel(int id, byte[] data, int size, int rate, int volume, int loopStart = 0, int loopEnd = 0, int pan = 0)
    {
        var channel = Array.Find(_channels, c => c.Id == 0);
        if (channel is null)
        {
            return;
        }

        channel.Id = id;
        channel.Volume = (byte)volume;
        channel.Pan = (sbyte)pan;
        channel.Frequency = (ushort)rate;
        channel.Counter = 0;
        channel.Input = new SampleStream(data, size, loopStart, loopEnd);

        // the first sample read
        channel.Input.Read(out channel.Position);
    }

    /// <summary>Mirrors <c>stopChannel</c>.</summary>
    public void StopChannel(int id)
    {
        foreach (var channel in _channels)
        {
            if (channel.Id == id)
            {
                channel.Clear();
            }
        }
    }

    /// <summary>Mirrors <c>setChannelVol</c>.</summary>
    public void SetChannelVolume(int id, int volume)
    {
        if (Array.Find(_channels, c => c.Id == id) is { } channel)
        {
            channel.Volume = (byte)volume;
        }
    }

    /// <summary>Mirrors <c>setChannelFreq</c>: no faster than 31400, as fast as WinUAE goes.</summary>
    public void SetChannelFrequency(int id, int frequency)
    {
        if (Array.Find(_channels, c => c.Id == id) is { } channel)
        {
            channel.Frequency = (ushort)Math.Min(frequency, MaxFrequency);
        }
    }

    /// <summary>
    /// Mixes the channels into an interleaved stereo buffer, calling the update routine at its
    /// rate among the samples, a channel that runs out of samples stopped. Mirrors <c>do_mix</c>.
    /// </summary>
    public void Mix(Span<short> stereo)
    {
        stereo.Clear();
        int frames = stereo.Length / 2;
        int done = 0;
        while (frames > 0)
        {
            int length;
            if (_update is not null)
            {
                length = (int)(_mixAmount - _mixPosition);
                if (_mixPosition == 0)
                {
                    _update();
                }

                if (length <= frames)
                {
                    _mixPosition = 0;
                    frames -= length;
                }
                else
                {
                    _mixPosition += (uint)frames;
                    length = frames;
                    frames = 0;
                }
            }
            else
            {
                length = frames;
                frames = 0;
            }

            foreach (var channel in _channels)
            {
                if (channel.Id != 0)
                {
                    MixChannel(channel, stereo.Slice(done * 2, length * 2));
                }
            }

            done += length;
        }
    }

    // a channel's samples added to a stretch of the buffer, resampled linearly (do_mix's inner loop)
    private void MixChannel(Channel channel, Span<short> stereo)
    {
        int left = (ushort)((127 - channel.Pan) * channel.Volume / 127);
        int right = (ushort)((127 + channel.Pan) * channel.Volume / 127);
        for (int j = 0; j < stereo.Length / 2; j++)
        {
            int delta = (int)(((uint)channel.Frequency << 16) / (uint)sampleRate);
            ushort fraction = (ushort)(~channel.Counter & 0xFFFF);
            if (channel.Counter + (uint)delta < 0x10000)
            {
                fraction = (ushort)delta;
            }

            channel.Counter += (uint)delta;
            int position = channel.Position * fraction / 0x10000;
            while (channel.Counter >= 0x10000)
            {
                if (!channel.Input!.Read(out channel.Position))
                {
                    channel.Clear();
                    return;
                }

                channel.Counter -= 0x10000;
                if (channel.Counter > 0x10000)
                {
                    position += channel.Position;
                }
                else
                {
                    position += channel.Position * (int)(channel.Counter & 0xFFFF) / 0x10000;
                }
            }

            // a sum too large for the calculation below taken in pieces
            short sample = 0;
            while (position < -0x8000)
            {
                sample = (short)(sample - (int)(0x80000000u / (uint)delta));
                position += 0x8000;
            }

            while (position > 0x7FFF)
            {
                sample = (short)(sample + 0x7FFF0000 / delta);
                position -= 0x7FFF;
            }

            sample = (short)(sample + position * 0x10000 / delta);
            stereo[2 * j] = ClampedAdd(stereo[2 * j], sample * left / MaxMixerVolume);
            stereo[2 * j + 1] = ClampedAdd(stereo[2 * j + 1], sample * right / MaxMixerVolume);
        }
    }

    private static short ClampedAdd(short a, int b) => (short)Math.Clamp(a + b, short.MinValue, short.MaxValue);
}
