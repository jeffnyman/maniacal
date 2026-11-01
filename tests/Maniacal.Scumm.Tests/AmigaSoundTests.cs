using Maniacal.Scumm.Sound;

namespace Maniacal.Scumm.Tests;

/// <summary>The Amiga's sound machinery without the game: its sample channels and the CRC its sounds are known by.</summary>
public class AmigaSoundTests
{
    private const int Rate = 22050;

    private static short[] Mix(AmigaMixer mixer, int frames)
    {
        var stereo = new short[frames * 2];
        mixer.Mix(stereo);
        return stereo;
    }

    /// <summary>
    /// A channel at the output's own rate gives its samples back one for one, signed bytes as
    /// 16-bit samples, each taken at 65535/65536 of itself (the resampler's first fraction), at the
    /// volume's share (here 64 of 256, on both sides when not panned), and stops when it runs out.
    /// </summary>
    [Fact]
    public void A_channel_at_the_output_rate_plays_its_samples_one_for_one()
    {
        var mixer = new AmigaMixer(Rate);
        byte[] samples = [0x10, 0x20, 0xF0, 0x7F, 0x80];
        mixer.StartChannel(1, samples, samples.Length, Rate, 64);

        var stereo = Mix(mixer, 8);

        short[] expected = [.. samples[..4].Select(b => (short)(((sbyte)b << 8) * 0xFFFF / 0x10000 * 64 / 256))];
        Assert.Equal(expected, Enumerable.Range(0, 4).Select(i => stereo[2 * i]));
        Assert.Equal(expected, Enumerable.Range(0, 4).Select(i => stereo[2 * i + 1]));
        Assert.All(stereo[8..], s => Assert.Equal(0, s));
    }

    /// <summary>A looped section plays on and on after the samples before it, and a channel panned hard to one side is silent on the other.</summary>
    [Fact]
    public void A_looped_section_repeats_on_one_side()
    {
        var mixer = new AmigaMixer(Rate);
        byte[] samples = [0x00, 0x40, 0xC0];
        mixer.StartChannel(1, samples, samples.Length, Rate, 255, loopStart: 1, loopEnd: 3, pan: 127);

        var stereo = Mix(mixer, 40);

        Assert.All(Enumerable.Range(0, 40), i => Assert.Equal(0, stereo[2 * i]));
        var right = Enumerable.Range(0, 40).Select(i => stereo[2 * i + 1]).ToArray();
        Assert.Equal(0, right[0]);
        Assert.All(Enumerable.Range(1, 37), i => Assert.Equal(right[i], right[i + 2]));
        Assert.NotEqual(right[1], right[2]);
    }

    /// <summary>The update routine runs every so many samples, the output's rate over its own rounded down (367 at 22050), the first at once (<c>do_mix</c>).</summary>
    [Fact]
    public void The_update_runs_at_its_rate()
    {
        var mixer = new AmigaMixer(Rate);
        int updates = 0;
        mixer.SetUpdate(() => updates++, 60);

        Mix(mixer, Rate);

        Assert.Equal(61, updates);
    }

    /// <summary>Sounds are known by the CRC ScummVM takes, the usual CRC-32, of the data from 0x0A as long as the word at 0x08 says.</summary>
    [Fact]
    public void Sounds_are_known_by_their_crc()
    {
        byte[] data = [0, 0, 0, 0, 0, 0, 0, 0, 0x00, 0x09, .. "123456789"u8, 0xAA];

        Assert.Equal(0xCBF43926u, PlayerV2a.Crc(data));
        Assert.Equal(41, V2aSoundTables.Sounds.Length);
        Assert.Equal(41, V2aSoundTables.Sounds.Select(s => s.Crc).Distinct().Count());
    }
}
