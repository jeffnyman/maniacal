using Maniacal.Scumm.Game;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Scripts;
using Maniacal.Scumm.Storage;

namespace Maniacal.Scumm.Tests;

/// <summary>
/// The game as a consumer of the package drives it: only <see cref="GameWorld"/>'s public members,
/// as the player's headless run uses them. The tests see the engine's internals, but these keep
/// to what is promised.
/// </summary>
public class PublicApiTests
{
    private static GameWorld? Open()
        => GameFolders.Find(WalkthroughTests.V2) is { } path ? new GameWorld(ResourceLoader.Open(new DirectoryGameContainer(path))) : null;

    /// <summary>Clicks two kids and START, skips the intro, and plays on to the driveway (room 44).</summary>
    private static void ToTheDriveway(GameWorld world, Action? afterStep = null)
    {
        world.Boot();
        for (int step = 0; step < 1500; step++)
        {
            switch (step)
            {
                case 300: world.Click(82, 118); break;
                case 310: world.Click(200, 118); break;
                case 320: world.Click(290, 75); break;
                case 700: world.SkipCutscene(); break;
            }

            world.Step();
            afterStep?.Invoke();
        }
    }

    /// <summary>
    /// The kids are picked by the objects on screen alone, with no place known beforehand, in
    /// every release: the portraits are the 40 by 48 objects left to right (Dave, Syd, Michael,
    /// Wendy, Bernard, Razor, Jeff), START the 40 by 8 one. Their numbers differ by release and
    /// they have no names, so their places are what tells them apart.
    /// </summary>
    [Theory]
    [InlineData(WalkthroughTests.V2)]
    [InlineData(WalkthroughTests.V1)]
    [InlineData(WalkthroughTests.C64)]
    public void The_kids_are_picked_by_the_objects_on_screen(string folder)
    {
        if (GameFolders.Find(folder) is not { } path)
        {
            return;
        }

        var world = new GameWorld(ResourceLoader.Open(new DirectoryGameContainer(path)));
        world.Boot();
        int step = 0;
        while (step < 2000 && !(world.CurrentRoom?.Number == 45 && step >= 300))
        {
            world.Step();
            step++;
        }

        var here = world.ObjectsHere().Where(o => o.Touchable).ToList();
        var portraits = here.Where(o => (o.Width, o.Height) == (40, 48)).GroupBy(o => o.X).Select(g => g.First()).OrderBy(o => o.X).ToList();
        var start = Assert.Single(here, o => (o.Width, o.Height) == (40, 8));
        Assert.Equal(7, portraits.Count);

        foreach (var obj in new[] { portraits[1], portraits[4], start })
        {
            Assert.True(world.ClickObject(obj.Number));
            for (int i = 0; i < 10; i++)
            {
                world.Step();
            }
        }

        for (int i = 0; i < 3000 && world.ActorsHere().Count < 3; i++)
        {
            if (i % 50 == 0)
            {
                world.SkipCutscene();
            }

            world.Step();
        }

        Assert.NotEqual(45, world.CurrentRoom?.Number);
        Assert.Contains(world.ActorsHere(), a => a.Name == "Syd");
        Assert.Contains(world.ActorsHere(), a => a.Name == "Bernard");
    }

    [Fact]
    public void A_headless_run_sees_the_kids_on_the_driveway()
    {
        if (Open() is not { } world)
        {
            return;
        }

        ToTheDriveway(world);

        Assert.Equal(44, world.CurrentRoom?.Number);
        Assert.Equal(["Syd", "Dave", "Bernard"], world.ActorsHere().Select(a => a.Name));
        Assert.All(world.ActorsHere(), a => Assert.True(a.Visible));
        Assert.Contains(world.RunningScripts(), s => s.Kind == ScriptKind.Global && s.Number == 177 && s.Status == SlotStatus.Paused);
    }

    [Fact]
    public void Variables_and_options_are_set_through_the_world()
    {
        if (Open() is not { } world)
        {
            return;
        }

        world.Boot();
        world.SetVariable(70, 3);
        Assert.Equal(3, world.Variables[70]);

        Assert.False(world.CopyProtection);
        world.CopyProtection = true;
        Assert.True(world.CopyProtection);
    }

    /// <summary>Read after each step, as a host or a recording reads it, the intro is heard (the title is silent).</summary>
    [Fact]
    public void The_sound_is_read_as_game_time_passes()
    {
        if (Open() is not { } world)
        {
            return;
        }

        long frames = 0;
        int peak = 0;
        ToTheDriveway(world, () =>
        {
            var samples = world.ReadAudio();
            frames += samples.Length / 2;
            peak = Math.Max(peak, samples.DefaultIfEmpty().Max(s => Math.Abs((int)s)));
        });

        Assert.Equal(GameWorld.DefaultSampleRate, world.SampleRate);
        Assert.Equal((long)(world.Time.TotalSeconds * world.SampleRate), frames);
        Assert.Empty(world.ReadAudio());
        Assert.True(peak > 0);
    }

    [Fact]
    public void A_save_loads_back_with_what_it_says_about_itself()
    {
        if (Open() is not { } world)
        {
            return;
        }

        ToTheDriveway(world);
        var file = world.Save("driveway", new DateTime(2026, 10, 31, 12, 0, 0));

        var loaded = Open()!;
        var info = loaded.Load(file);
        for (int step = 0; step < 60; step++)
        {
            loaded.Step();
        }

        Assert.Equal("driveway", info.Name);
        Assert.Equal(new DateTime(2026, 10, 31, 12, 0, 0), info.Saved);
        Assert.Equal(44, loaded.CurrentRoom?.Number);
        Assert.Equal(world.ActorsHere().Select(a => (a.Name, a.X, a.Y)), loaded.ActorsHere().Select(a => (a.Name, a.X, a.Y)));
    }
}
