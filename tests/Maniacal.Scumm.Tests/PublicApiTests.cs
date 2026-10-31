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

        long read = 0;
        int peak = 0;
        ToTheDriveway(world, () =>
        {
            long due = (long)(world.Time.TotalSeconds * GameWorld.DefaultSampleRate);
            var buffer = new short[(due - read) * 2];
            world.Audio.Read(buffer);
            read = due;
            peak = Math.Max(peak, buffer.DefaultIfEmpty().Max(s => Math.Abs((int)s)));
        });

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
