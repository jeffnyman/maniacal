namespace Maniacal.Scumm.Hosting;

/// <summary>
/// Pull-model audio output. The host asks the engine's <see cref="IAudioSource"/> for samples when
/// its output buffer has room; the engine never blocks on audio.
/// </summary>
public interface IAudioDriver
{
    /// <summary>The output's sample rate in frames a second, which the engine makes its sound at.</summary>
    int SampleRate { get; }

    /// <summary>
    /// Master volume from 0 (silent) to <see cref="AudioVolume.Unity"/> (the engine's mix as is),
    /// applied to everything the source produces. Starts at <see cref="AudioVolume.Default"/>, the
    /// level ScummVM mixes at.
    /// </summary>
    int Volume { get; set; }

    /// <summary>Starts pulling samples from the source, replacing any source already playing.</summary>
    void Start(IAudioSource source);

    /// <summary>Stops pulling samples.</summary>
    void Stop();
}

/// <summary>Produces interleaved stereo signed 16-bit samples. Must always fill the whole buffer.</summary>
public interface IAudioSource
{
    /// <summary>Fills the buffer with the next samples, left and right in turn.</summary>
    void Read(Span<short> interleavedStereo);
}

/// <summary>The master volume scale shared by the hosts: 256 steps like ScummVM's mixer.</summary>
public static class AudioVolume
{
    /// <summary>Full volume: samples pass unchanged.</summary>
    public const int Unity = 256;

    /// <summary>ScummVM's default for music, effects and speech (192 of 256, 75%).</summary>
    public const int Default = 192;

    /// <summary>A volume kept between 0 and <see cref="Unity"/>.</summary>
    public static int Clamp(int volume) => Math.Clamp(volume, 0, Unity);

    /// <summary>The volume for a percentage of <see cref="Unity"/>.</summary>
    public static int FromPercent(int percent) => Clamp(percent * Unity / 100);

    /// <summary>A volume as a percentage of <see cref="Unity"/>, rounded.</summary>
    public static int ToPercent(int volume) => (Clamp(volume) * 100 + Unity / 2) / Unity;

    /// <summary>Scales samples in place.</summary>
    public static void Apply(Span<short> samples, int volume)
    {
        volume = Clamp(volume);
        if (volume == Unity)
        {
            return;
        }

        if (volume == 0)
        {
            samples.Clear();
            return;
        }

        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (short)(samples[i] * volume / Unity);
        }
    }
}
