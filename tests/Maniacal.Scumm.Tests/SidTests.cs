using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Sound;
using Maniacal.Scumm.Storage;

namespace Maniacal.Scumm.Tests;

public class SidTests
{
    private const string C64Folder = "maniac_mansion_c64";
    private const int Rate = 22050;

    /// <summary>
    /// A sawtooth on voice 1, its envelope full and the volume up, sounds at the frequency its
    /// register gives: freq * clock / 2^24, 440 Hz for 7218 at the NTSC clock.
    /// </summary>
    [Fact]
    public void A_sawtooth_sounds_at_its_frequency()
    {
        var sid = new Sid(Rate);
        sid.Write(0x00, 7218 & 0xFF);
        sid.Write(0x01, 7218 >> 8);
        sid.Write(0x05, 0x00);
        sid.Write(0x06, 0xF0);
        sid.Write(0x18, 0x0F);
        sid.Write(0x04, 0x21);

        var samples = new short[Rate * 2];
        sid.Generate(samples);

        // the second second, once the external filter has settled: count the rises through the middle
        var second = samples.AsSpan(Rate, Rate);
        int middle = (second.ToArray().Max() + second.ToArray().Min()) / 2;
        int rises = 0;
        for (int i = 1; i < second.Length; i++)
        {
            if (second[i - 1] < middle && second[i] >= middle)
            {
                rises++;
            }
        }

        Assert.InRange(rises, 438, 442);
    }

    /// <summary>A silent chip gives nothing but the output stage's settling DC.</summary>
    [Fact]
    public void A_silent_chip_is_still()
    {
        var sid = new Sid(Rate);
        var samples = new short[Rate];
        sid.Generate(samples);
        Assert.All(samples[(Rate / 2)..], s => Assert.InRange(s, -200, 200));
    }

    /// <summary>
    /// Every sound and song of the C64 release plays for 3 seconds without running off its data,
    /// and sounds; the songs (byte 4 is 7) are the music. The first second of sound 57, the
    /// intro's rising whine, is pinned.
    /// </summary>
    [Fact]
    public void Every_c64_sound_plays()
    {
        if (GameFolders.Find(C64Folder) is not { } path)
        {
            return;
        }

        var loader = ResourceLoader.Open(new DirectoryGameContainer(path));
        int played = 0, songs = 0;
        for (int number = 1; number < 70; number++)
        {
            // 3 to 5 are the player's channel buffers, whose index entries point at nothing
            if (number is >= 3 and <= 5 || !loader.Index.Locate(ResourceType.Sound, number).IsPresent || loader.Load(ResourceType.Sound, number) is not { } resource)
            {
                continue;
            }

            var data = resource.ToArray();
            if (data.Length < 6)
            {
                continue;
            }

            var player = new PlayerSid(Rate);
            player.StartSound(number, data);
            var samples = new short[Rate * 3];
            Render(player, samples);
            Assert.True(samples.Max() - samples.Min() > 1000, $"sound {number} is silent");
            played++;
            if (data[4] == 7)
            {
                songs++;
            }

            if (number == 57)
            {
                Assert.Equal("afe8c556fde635690cfd57152d4324a7", Md5.ComputeHex(System.Runtime.InteropServices.MemoryMarshal.AsBytes(samples.AsSpan(0, Rate)).ToArray()));
            }
        }

        Assert.Equal((66, 6), (played, songs));
    }

    /// <summary>
    /// A sound plays while it lasts and stops when asked. A song plays through the player's
    /// channel buffers, not its own resource, so stopping it by number leaves it playing; only
    /// stopping everything stops it, as in ScummVM (<c>stopSound_intern</c>, <c>stopAllSounds</c>).
    /// </summary>
    [Fact]
    public void Sounds_and_songs_stop()
    {
        if (GameFolders.Find(C64Folder) is not { } path)
        {
            return;
        }

        var loader = ResourceLoader.Open(new DirectoryGameContainer(path));
        var player = new PlayerSid(Rate);
        player.StartSound(57, loader.Load(ResourceType.Sound, 57)!.Value.ToArray());
        Render(player, new short[Rate / 10]);
        Assert.True(player.IsPlaying(57));
        player.StopSound(57);
        Assert.False(player.IsPlaying(57));

        var song = loader.Load(ResourceType.Sound, 58)!.Value.ToArray();
        Assert.Equal(7, song[4]);
        player.StartSound(58, song);
        Render(player, new short[Rate / 10]);
        player.StopSound(58);
        Assert.True(player.IsPlaying(58));
        player.StopAllSounds();
        Assert.False(player.IsPlaying(58));
    }

    // a mono player renders into a host's stereo buffer; the left channel is the sound
    private static void Render(PlayerSid player, short[] mono)
    {
        var stereo = new short[mono.Length * 2];
        player.Read(stereo);
        for (int i = 0; i < mono.Length; i++)
        {
            mono[i] = stereo[i * 2];
        }
    }
}
