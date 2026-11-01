using Maniacal.Scumm.Game;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Sound;
using Maniacal.Scumm.Storage;

namespace Maniacal.Scumm.Tests;

/// <summary>
/// The NES's sound: ScummVM's Player_NES and its emulation of the NES's sound chip. The game starts
/// at its boot script, the title screens left out, as ScummVM's recording was aligned from it. The
/// tests with the cartridge pass without checking when its folder is absent.
/// </summary>
public class NesSoundTests
{
    private const int Rate = 44100, FrameSamples = Rate / 60;

    // an effect for slot 0 of a priority: square 0 at full constant volume, a wait of some
    // frames (any byte from 16 to 0xFD, then the count), then its end
    private static byte[] Effect(int priority, int frames) => [0, 0, 0, (byte)priority, 0x00, 0xBF, 0x00, 0x80, 0x08, 0x10, (byte)frames, 0xFF];

    private static short[] Render(PlayerNes player, int frames)
    {
        var stereo = new short[frames * FrameSamples * 2];
        player.Read(stereo);
        return stereo;
    }

    /// <summary>An effect writes its channel's registers, sounds through its wait, then ends, leaving the chip quiet.</summary>
    [Fact]
    public void An_effect_plays_and_ends()
    {
        var player = new PlayerNes(Rate);
        player.StartSound(9, Effect(10, 6));

        Assert.True(player.IsPlaying(9));
        Assert.Contains(Render(player, 4), s => s != 0);

        Render(player, 6);

        Assert.False(player.IsPlaying(9));
        var after = Render(player, 2);
        Assert.Single(after.Distinct());
    }

    /// <summary>A sound of lower priority than the one in its slot is not played; one of the same or higher replaces it.</summary>
    [Fact]
    public void A_lower_priority_does_not_take_the_slot()
    {
        var player = new PlayerNes(Rate);
        player.StartSound(9, Effect(10, 30));
        player.StartSound(8, Effect(5, 30));

        Assert.True(player.IsPlaying(9));
        Assert.False(player.IsPlaying(8));

        player.StartSound(7, Effect(10, 30));

        Assert.False(player.IsPlaying(9));
        Assert.True(player.IsPlaying(7));

        player.StopAllSounds();
        Assert.False(player.IsPlaying(7));
    }

    /// <summary>
    /// The intro's sound, second by second, as loud as ScummVM 2026.4.0 made it (its mixer's output
    /// recorded through SDL's disk driver, aligned on ours): the meteor's sounds (56 and 57), then
    /// the title song (58). Sample by sample the two differ where the chip's history differs (the
    /// noise channel's shift register, the channels' phases), which ScummVM ran through its title
    /// screens first.
    /// </summary>
    [Fact]
    public void The_intro_sounds_as_loud_as_in_scummvm()
    {
        if (GameFolders.Find(WalkthroughTests.Nes) is not { } path || NesRom.Open(new DirectoryGameContainer(path)) is not { } rom)
        {
            return;
        }

        var world = new GameWorld(new ResourceLoader(rom, ResourceIndex.Parse(LflFile.Read(rom, 0))), sampleRate: Rate);
        world.BootWithoutTitles();
        var left = new List<short>();
        var started = new HashSet<int>();
        while (world.Time.TotalSeconds < 36)
        {
            world.Step();
            started.UnionWith(new[] { 56, 57, 58 }.Where(world.Sound.IsRunning));
            var stereo = world.ReadAudio();
            for (int i = 0; i < stereo.Length; i += 2)
            {
                left.Add(stereo[i]);
            }
        }

        Assert.Equal([56, 57, 58], started.Order());
        Assert.True(world.Sound.IsRunning(58));

        // ScummVM's mean loudness in those seconds
        foreach (var (second, theirs) in new[] { (9, 414), (10, 554), (11, 461), (12, 1263), (15, 2688), (20, 2508), (25, 4275), (30, 3659), (35, 3384) })
        {
            double ours = left.Skip(second * Rate).Take(Rate).Average(s => Math.Abs((int)s));
            Assert.InRange(ours, theirs * 0.97, theirs * 1.03);
        }
    }
}
