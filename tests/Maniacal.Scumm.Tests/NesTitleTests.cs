using Maniacal.Scumm.Game;
using Maniacal.Scumm.Graphics;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Storage;

namespace Maniacal.Scumm.Tests;

/// <summary>
/// The NES's two title screens, before its boot script: the licence screen for five seconds, then
/// Lucasfilm Games for five and a half with its eight sparkles and their twinkles. The tests pass
/// without checking when the cartridge's folder is absent.
/// </summary>
public class NesTitleTests
{
    // the steps each screen stays, a 60th of a second each
    private const int FirstSteps = 300, SecondSteps = 330, TitleRoom = 33;

    private static GameWorld? Booted(int sampleRate = 22050)
    {
        if (GameFolders.Find(WalkthroughTests.Nes) is not { } path || NesRom.Open(new DirectoryGameContainer(path)) is not { } rom)
        {
            return null;
        }

        var world = new GameWorld(new ResourceLoader(rom, ResourceIndex.Parse(LflFile.Read(rom, 0))), sampleRate: sampleRate);
        world.Boot();
        return world;
    }

    private static void Steps(GameWorld world, int count)
    {
        for (int i = 0; i < count; i++)
        {
            world.Step();
        }
    }

    /// <summary>
    /// The screens as ScummVM 2026.4.0 showed them in its window, to the pixel: the licence screen,
    /// Lucasfilm Games before its first sparkle, and with its sparkles as they came (ScummVM's
    /// ticks are 16 milliseconds, so its sparkles came a little sooner).
    /// </summary>
    [Theory]
    [InlineData(1, "041e5f23055def179b696ba7de2897f5568ce493ede1dfcbc3179a73f7bb813f")]
    [InlineData(300, "dcb86dc66e7578b7534626c6416a3712ef5dee9c50f2ebfaf69a2f3c320ba046")]
    [InlineData(333, "6f9c56824172dde4c54dbf2ec19081651291862ab3678b642d9b8de3dd68c8fb")]
    [InlineData(366, "30aee137d56dbf0707181573c1e32db3b5ad461269fb01f7beb1050d13997f1a")]
    [InlineData(425, "cc9e082d33986d47834f6b627ae20520e228b60e26d692749c8dd5a65518b2f8")]
    [InlineData(487, "e6d12bee7807dd6d08f79e04b4820fd1bfc84236636e14fc5f6c805b63645884")]
    public void The_title_screens_are_drawn_as_scummvm_draws_them(int steps, string hash)
    {
        if (Booted() is not { } world)
        {
            return;
        }

        Steps(world, steps);

        Assert.True(world.ShowingTitles);
        Assert.Equal((NesTitles.Width, NesTitles.Height), (world.Screen().Width, world.Screen().Height));
        Assert.Equal(hash, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(world.Screen().Pixels)));
    }

    /// <summary>
    /// The screens stay their time, a tick a step with no cursor, and the step after them boots the
    /// game: its first room is the intro's.
    /// </summary>
    [Fact]
    public void The_titles_stay_their_time_then_the_game_boots()
    {
        if (Booted() is not { } world)
        {
            return;
        }

        Assert.Equal(FirstSteps, NesTitles.Ticks(0));
        Assert.Equal(SecondSteps, NesTitles.Ticks(1));
        Steps(world, FirstSteps + SecondSteps - 1);

        Assert.True(world.ShowingTitles);
        Assert.False(world.CursorShown);
        Assert.Equal(1, world.StepTicks);
        Assert.Null(world.CurrentRoom);

        world.Step();
        Assert.False(world.ShowingTitles);
        Assert.Equal(10.5, world.Time.TotalSeconds, 3);

        for (int step = 0; step < 200 && world.CurrentRoom?.Number != TitleRoom; step++)
        {
            world.Step();
        }

        Assert.Equal(TitleRoom, world.CurrentRoom?.Number);
    }

    /// <summary>A click moves on from the first screen to the second, and Escape from the second to the game.</summary>
    [Fact]
    public void A_click_or_escape_moves_on()
    {
        if (Booted() is not { } world)
        {
            return;
        }

        Steps(world, 10);
        var first = world.Screen().Pixels.ToArray();
        world.Click(100, 100);
        world.Step();

        Assert.True(world.ShowingTitles);
        Assert.NotEqual(first, world.Screen().Pixels);

        world.PressKey(GameWorld.KeyEscape);
        world.Step();
        Assert.False(world.ShowingTitles);
    }

    /// <summary>
    /// The licence screen is silent; each sparkle starts a twinkle, which ring on together for
    /// three and a half seconds, about as loud as ScummVM 2026.4.0's own (its mixer's output,
    /// recorded through SDL's disk driver: a mean of 1763 over those seconds), then fall silent.
    /// </summary>
    [Fact]
    public void The_sparkles_twinkle_as_in_scummvm()
    {
        const int Rate = 44100;
        if (Booted(Rate) is not { } world)
        {
            return;
        }

        var left = new List<short>();
        while (world.ShowingTitles)
        {
            world.Step();
            var stereo = world.ReadAudio();
            for (int i = 0; i < stereo.Length; i += 2)
            {
                left.Add(stereo[i]);
            }
        }

        double Loudness(double from, double to)
        {
            var part = left.Skip((int)(from * Rate)).Take((int)((to - from) * Rate)).ToArray();
            double mean = part.Average(s => (double)s);
            return part.Average(s => Math.Abs(s - mean));
        }

        Assert.Equal(0, Loudness(0, 5));
        Assert.InRange(Loudness(5.5, 8.5), 1763 * 0.9, 1763 * 1.1);
        Assert.Equal(0, Loudness(9.25, 10.4));
    }
}
