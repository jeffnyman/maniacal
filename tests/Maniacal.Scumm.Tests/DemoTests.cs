using Maniacal.Scumm.Game;
using Maniacal.Scumm.Graphics;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Scripts;
using Maniacal.Scumm.Storage;

namespace Maniacal.Scumm.Tests;

/// <summary>
/// The DOS demos, as ScummVM runs them: the 1987 demo's rooms read from its <c>NN.MAN</c> files,
/// both booted into their demo mode (script 9), every actor in the costume of its number, and the
/// 1988 demo's colours fixed for its script.
/// </summary>
public class DemoTests
{
    private const string V1Demo = WalkthroughTests.V1Demo, V2Demo = WalkthroughTests.V2Demo;

    // the C64 demo's menu: 1 while its tune plays the first time, so it plays again; 0 the second
    // time, after which the menu goes to the title
    private const int MenuTuneAgain = 92;

    private static GameWorld? Open(string folder)
        => GameFolders.Find(folder) is { } path ? new GameWorld(ResourceLoader.Open(new DirectoryGameContainer(path))) : null;

    private static void Steps(GameWorld world, int count)
    {
        for (int i = 0; i < count; i++)
        {
            world.Step();
        }
    }

    [Theory]
    [InlineData(V1Demo, 1)]
    [InlineData(V2Demo, 2)]
    public void A_demo_boots_into_its_demo_mode(string folder, int version)
    {
        if (Open(folder) is not { } world)
        {
            return;
        }

        world.Boot();
        world.Step();

        Assert.Equal(version, world.Version);
        Assert.True(world.IsDemo);
        Assert.Contains(world.RunningScripts(), s => s.Kind == ScriptKind.Global && s.Number == GameWorld.DemoBootScript);
        Assert.All(world.Actors.Skip(1), a => Assert.Equal(a.Number, a.CostumeNumber));
    }

    /// <summary>Both demos show the title, the front porch and the lab, every instruction handled.</summary>
    [Theory]
    [InlineData(V1Demo, 1100, 1600)]
    [InlineData(V2Demo, 1400, 2100)]
    public void A_demo_plays_through_the_mansion_to_the_lab(string folder, int inTheLab, int outOfTheLab)
    {
        if (Open(folder) is not { } world)
        {
            return;
        }

        world.Boot();
        var rooms = new List<int>();
        for (int step = 0; step < outOfTheLab; step++)
        {
            world.Step();
            if (world.CurrentRoom?.Number is { } room && (rooms.Count == 0 || rooms[^1] != room))
            {
                rooms.Add(room);
            }

            if (step == inTheLab)
            {
                Assert.Equal(31, world.CurrentRoom?.Number);
            }
        }

        Assert.Equal([33, 1, 10, 31, 10], rooms.Where(r => r != 49));
        Assert.DoesNotContain(world.Unhandled, call => !call.Contains("resourceRoutines", StringComparison.Ordinal));
    }

    /// <summary>
    /// Script 9 starts script 175, whose cutscene freezes script 9, and then, frozen as it is, goes
    /// on to start script 173 and its quotes: in versions 0 to 2 a caller resumes after a nested
    /// script whatever its freeze bit (runScriptNested asks freezeCount, which freezeScripts never
    /// raises there). Checked against ScummVM 2026.4.0, which shows the first quote by the meteor.
    /// </summary>
    [Fact]
    public void The_1988_demo_shows_its_quotes_by_the_meteor()
    {
        if (Open(V2Demo) is not { } world)
        {
            return;
        }

        world.Boot();
        Steps(world, 120);

        Assert.Contains(world.RunningScripts(), s => s.Kind == ScriptKind.Global && s.Number == 173);
        Assert.Contains(world.Verbs.Slots, v => System.Text.Encoding.Latin1.GetString(v.Name).Contains("Game of the Year", StringComparison.Ordinal));
    }

    /// <summary>resetScumm and o2_actorOps give the 1988 demo's script its colours.</summary>
    [Fact]
    public void The_1988_demo_has_its_colours()
    {
        if (Open(V2Demo) is not { } world)
        {
            return;
        }

        Assert.Equal(1, world.Actors[3].Palette[3]);
        Assert.Equal([15, 7, 2, 5, 14], new[] { 9, 10, 11, 13, 23 }.Select(n => world.Actors[n].TalkColor));
    }

    /// <summary>
    /// The C64 demo boots with script 1, as the full game does (ScummVM's runBootscript starts the
    /// DOS demos' script 9 only), to its menu: in the verb area, in place of the verbs, its own
    /// lines (verbDemoMode), "Press F7 to return to menu." in colour 16. Checked against ScummVM
    /// 2026.4.0, whose menu is the same but for the cursor.
    /// </summary>
    [Fact]
    public void The_c64_demo_shows_its_menu_and_lines()
    {
        if (Open(WalkthroughTests.C64Demo) is not { } world)
        {
            return;
        }

        world.Boot();
        Steps(world, 300);

        Assert.True(world.IsDemo);
        Assert.Equal(0, world.Version);
        Assert.Equal(45, world.CurrentRoom?.Number);
        Assert.Contains(world.RunningScripts(), s => s.Kind == ScriptKind.Global && s.Number == 1);
        Assert.Contains(world.Verbs.Pixels.Pixels, p => p == 16);
        Assert.Equal(2, world.ObjectsHere().Count(o => o.Touchable));
    }

    /// <summary>
    /// The self-running demo, chosen on the menu, plays through to the library and back to the
    /// menu. There Purple Tentacle, following Sandy past the stairs, would stop for good, as he
    /// does in ScummVM without the step it gives him alone in the demo (the original engine's).
    /// </summary>
    [Fact]
    public void The_c64_self_running_demo_plays_back_to_its_menu()
    {
        if (Open(WalkthroughTests.C64Demo) is not { } world)
        {
            return;
        }

        world.Boot();
        Steps(world, 300);
        var selfRunning = world.ObjectsHere().Where(o => o.Touchable).OrderBy(o => o.Y).First();
        Assert.True(world.ClickObject(selfRunning.Number));

        var rooms = new List<int>();
        for (int step = 0; step < 4000; step++)
        {
            world.Step();
            if (world.CurrentRoom?.Number is { } room && (rooms.Count == 0 || rooms[^1] != room))
            {
                rooms.Add(room);
            }
        }

        Assert.Equal([45, 1, 10, 31, 10, 3, 5, 45], rooms.Take(8));
        Assert.DoesNotContain(world.Unhandled, call => !call.Contains("resourceRoutines", StringComparison.Ordinal));
    }

    /// <summary>F7 in the C64 demo goes back to its menu (ScummVM's processKeyboard restarts it), from anywhere but the menu itself.</summary>
    [Fact]
    public void F7_takes_the_c64_demo_back_to_its_menu()
    {
        if (Open(WalkthroughTests.C64Demo) is not { } world)
        {
            return;
        }

        world.Boot();
        Steps(world, 300);
        world.ClickObject(world.ObjectsHere().Where(o => o.Touchable).OrderBy(o => o.Y).First().Number);
        Steps(world, 700);
        Assert.NotEqual(45, world.CurrentRoom?.Number);

        world.PressKey(GameWorld.KeyF1 + 6);
        Steps(world, 30);

        Assert.Equal(45, world.CurrentRoom?.Number);
    }

    /// <summary>
    /// The C64 demo's index names rooms on a second disk the demo does not come with (room 4 is on
    /// disk 2, track 6), and rooms whose files hold only costumes (30, 40, 42, 46 and 51): the
    /// inspector lists the rooms and scripts there are, and those are not among them.
    /// </summary>
    [Fact]
    public void The_c64_demo_lists_only_the_rooms_on_its_disk()
    {
        if (GameFolders.Find(WalkthroughTests.C64Demo) is not { } path)
        {
            return;
        }

        var game = ManiacMansion.Open(path);
        var inspector = game.Inspect();

        Assert.Empty(inspector.Rooms.Intersect([4, 30, 40, 42, 46, 51]));
        Assert.Contains(45, inspector.Rooms);
        Assert.Null(inspector.Room(30));
        Assert.All(inspector.Rooms, room => Assert.NotNull(inspector.Room(room)));
        Assert.All(inspector.Scripts, script => Assert.NotNull(inspector.Script(script)));
    }

    /// <summary>
    /// F5 opens the C64's save screen, script 2, which the demo's index puts in room 50 on the
    /// second disk: in the demo it does nothing, and the demo plays on.
    /// </summary>
    [Fact]
    public void F5_does_nothing_in_the_c64_demo()
    {
        if (Open(WalkthroughTests.C64Demo) is not { } world)
        {
            return;
        }

        world.Boot();
        Steps(world, 300);
        world.ClickObject(world.ObjectsHere().Where(o => o.Touchable).OrderBy(o => o.Y).First().Number);
        Steps(world, 300);
        int room = world.CurrentRoom!.Number;

        world.PressKey(GameWorld.KeyF1 + 4);
        Steps(world, 30);

        Assert.Equal(room, world.CurrentRoom?.Number);
        Assert.DoesNotContain(world.RunningScripts(), s => s.Kind == ScriptKind.Global && s.Number == 2);
    }

    /// <summary>
    /// The C64 demo's menu waits while its tune plays (script 1 loops on variable 8, whether a
    /// sound is running), plays it a second time, and then goes to the title (room 33). A load
    /// stops every sound, as ScummVM's does, and the C64 starts none again: a save made at the
    /// menu during the second time loads into a game whose tune has ended, and it goes to the
    /// title at once, where the
    /// game it was saved from waits on. Checked against ScummVM 2026.4.0, which loads the same
    /// save to the same title, to the pixel.
    /// </summary>
    [Fact]
    public void A_save_made_at_the_c64_demos_menu_loads_with_its_tune_ended()
    {
        if (Open(WalkthroughTests.C64Demo) is not { } world)
        {
            return;
        }

        // the tune's second time through (from step 303, to the title at 590); a save made during
        // the first loads into a game that plays it again
        world.Boot();
        Steps(world, 400);
        Assert.Equal(45, world.CurrentRoom?.Number);
        Assert.Equal(0, world.Engine.Variables[MenuTuneAgain]);
        Assert.Equal(1, world.Engine.Variables[world.Engine.Vars.IsSoundRunning]);
        var save = world.Save("menu", new DateTime(2026, 11, 2));

        var loaded = Open(WalkthroughTests.C64Demo)!;
        loaded.Load(save);
        loaded.Step();
        Assert.Equal(0, loaded.Engine.Variables[loaded.Engine.Vars.IsSoundRunning]);

        Steps(world, 20);
        Steps(loaded, 19);
        Assert.Equal(45, world.CurrentRoom?.Number);
        Assert.Equal(33, loaded.CurrentRoom?.Number);
    }

    /// <summary>The 1987 demo's font has the 1988 release's apostrophe, as ScummVM's charset gives it.</summary>
    [Fact]
    public void The_1987_demo_has_the_1988_apostrophe()
    {
        Assert.Equal(Font.English(2).Glyph('\'').ToArray(), Font.English(1, demo: true).Glyph('\'').ToArray());
        Assert.NotEqual(Font.English(2).Glyph('\'').ToArray(), Font.English(1).Glyph('\'').ToArray());
    }
}
