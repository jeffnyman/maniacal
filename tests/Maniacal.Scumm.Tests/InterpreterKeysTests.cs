using Maniacal.Scumm.Game;
using Maniacal.Scumm.Graphics;
using Maniacal.Scumm.Hosting;
using Maniacal.Scumm.Input;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Storage;

namespace Maniacal.Scumm.Tests;

/// <summary>
/// The keys the original interpreters kept for themselves, as ScummVM has them with the original
/// interface: Space pauses, F8 asks before restarting, F6 turns the sounds off and on, Ctrl+C and
/// Alt+X quit. The tests pass without checking when their folder is absent.
/// </summary>
public class InterpreterKeysTests
{
    private const int F6 = GameWorld.KeyF1 + 5, F8 = GameWorld.KeyF1 + 7;

    // the doorbell, the same sound in every release
    private const int Doorbell = 39;

    private static void Steps(GameWorld world, int count)
    {
        for (int i = 0; i < count; i++)
        {
            world.Step();
        }
    }

    private static void Press(GameWorld world, int key, int steps = 5)
    {
        world.PressKey(key);
        Steps(world, steps);
    }

    // how many pixels of a colour in a band of rows of the screen
    private static int Count(Surface8 screen, int top, int bottom, byte color)
    {
        int count = 0;
        for (int y = top; y < bottom; y++)
        {
            foreach (byte pixel in screen.Row(y))
            {
                count += pixel == color ? 1 : 0;
            }
        }

        return count;
    }

    // a message's colour as it shows: version 1 shows the text at the top through its colour
    // table (postProcessDOSGraphics), its sentence line not
    private static byte Shown(GameWorld world, byte color) => world.Version == 1 ? Palettes.ManiacV1ToEga[color] : color;

    // the pixels of a band of rows of the screen
    private static byte[] Rows(Surface8 screen, int top, int bottom)
        => [.. Enumerable.Range(top, bottom - top).SelectMany(y => screen.Row(y).ToArray())];

    private static GameWorld? Booted(string folder)
    {
        if (GameFolders.Find(folder) is not { } path)
        {
            return null;
        }

        var world = new GameWorld(ResourceLoader.Open(new DirectoryGameContainer(path)));
        world.Boot();
        return world;
    }

    /// <summary>
    /// Space pauses the game: the sentence line says so, in colour 13 (the C64's 16), the snail is
    /// the cursor, and nothing moves, its time and sound with it, until a key, which goes no
    /// further. Then version 2 shows its sentence again; version 1 and the C64 leave the line clear.
    /// </summary>
    [Theory]
    [InlineData(WalkthroughTests.V2, 13)]
    [InlineData(WalkthroughTests.V1, 13)]
    [InlineData(WalkthroughTests.C64, 16)]
    [InlineData(WalkthroughTests.AtariSt, 13)]
    [InlineData(WalkthroughTests.Amiga, 13)]
    public void Space_pauses_the_game_until_a_key(string folder, byte color)
    {
        if (WalkthroughTests.Start(folder) is not { } player)
        {
            return;
        }

        var world = player.World;
        world.MoveMouse(160, 100);
        Steps(world, 10);
        int top = world.Verbs.Top, verb = world.Engine.Variables[GameWorld.VarSentenceVerb];
        var line = Rows(world.Screen(), top, top + 8);

        world.PressKey(' ');
        world.Step();
        world.ReadAudio();
        var paused = world.Time;
        Assert.True(world.WaitsOnMessage);
        Steps(world, 60);

        Assert.True(world.WaitsOnMessage);
        Assert.Equal(paused, world.Time);
        Assert.Empty(world.ReadAudio());
        Assert.True(Count(world.Screen(), top, top + 8, color) > 100);

        // the snail, its hotspot at the mouse
        var cursor = new Surface8(world.Layout.Width, world.Layout.Height);
        cursor.Fill(0xEE);
        world.DrawCursor(cursor);
        var snail = world.IsAmiga ? CursorImages.AmigaSnail : world.IsAtariSt ? CursorImages.AtariStSnail : CursorImages.Snail;
        Assert.Equal(snail.Count(b => b != 0xFF), cursor.Pixels.Count(b => b != 0xEE));

        // a key ends it, and is not the game's: Q does not pick Push; the step it interrupted goes
        // on, and the clock with the steps after
        Press(world, 'q', 1);
        Assert.False(world.WaitsOnMessage);
        Assert.Equal(verb, world.Engine.Variables[GameWorld.VarSentenceVerb]);
        Assert.Equal(paused, world.Time);
        world.Step();
        Assert.True(world.Time > paused);
        if (world.Version == 2)
        {
            Assert.Equal(line, Rows(world.Screen(), top, top + 8));
        }
        else
        {
            Assert.Equal(0, Count(world.Screen(), top, top + 8, color));
        }
    }

    /// <summary>
    /// The demos pause with a banner across the room instead, ScummVM's old-style one: twelve rows
    /// from row 80, black with a line at the top and the bottom and the text, in colour 12.
    /// </summary>
    [Theory]
    [InlineData(WalkthroughTests.V2Demo)]
    [InlineData(WalkthroughTests.V1Demo)]
    [InlineData(WalkthroughTests.C64Demo)]
    public void A_demo_pauses_with_a_banner(string folder)
    {
        if (Booted(folder) is not { } world)
        {
            return;
        }

        Steps(world, 300);
        Press(world, ' ');

        Assert.True(world.WaitsOnMessage);
        var screen = world.Screen();
        Assert.Equal(screen.Width, Count(screen, 80, 81, 12));
        Assert.Equal(screen.Width, Count(screen, 91, 92, 12));
        Assert.True(Count(screen, 82, 90, 12) > 100);
        Assert.Equal(screen.Width * 10, Count(screen, 81, 91, 12) + Count(screen, 81, 91, 0));

        world.Click(100, 100);
        world.Step();
        Assert.False(world.WaitsOnMessage);
    }

    /// <summary>
    /// F8 asks first, in colour 4 at the top of the screen, the game standing still; any key but Y
    /// goes back to the game where it was, and Y restarts it, at the title. The game's own script,
    /// which restarts at once on F8, never sees the key.
    /// </summary>
    [Theory]
    [InlineData(WalkthroughTests.V2)]
    [InlineData(WalkthroughTests.V1)]
    [InlineData(WalkthroughTests.C64)]
    [InlineData(WalkthroughTests.AtariSt)]
    [InlineData(WalkthroughTests.Amiga)]
    public void F8_asks_before_restarting(string folder)
    {
        if (WalkthroughTests.Start(folder) is not { } player)
        {
            return;
        }

        var world = player.World;
        int ego = world.Engine.Variables[Scripts.ScriptEngine.VarEgo];

        Press(world, F8);
        Assert.True(world.WaitsOnMessage);
        Assert.True(Count(world.Screen(), 0, 8, Shown(world, 4)) > 100);

        Press(world, 'n', 100);
        Assert.False(world.WaitsOnMessage);
        Assert.Equal(WalkthroughTests.Driveway, world.CurrentRoom?.Number);
        Assert.Equal(ego, world.Engine.Variables[Scripts.ScriptEngine.VarEgo]);

        Press(world, F8);
        Press(world, 'y', 1);
        Assert.False(world.WaitsOnMessage);
        for (int i = 0; i < 500 && world.CurrentRoom?.Number is not 45; i++)
        {
            world.Step();
        }

        Assert.Equal(45, world.CurrentRoom?.Number);
    }

    /// <summary>
    /// F6 turns the sounds off, saying "Sounds Off" in colour 4 (version 1's and the C64's 2), and
    /// on again, "Sounds On" in 2 (5); the game goes on, its sound unheard meanwhile. Version 1
    /// shows those colours through its colour table, as all its text at the top.
    /// </summary>
    [Theory]
    [InlineData(WalkthroughTests.V2, 4, 2)]
    [InlineData(WalkthroughTests.V1, 2, 5)]
    [InlineData(WalkthroughTests.C64, 2, 5)]
    [InlineData(WalkthroughTests.Amiga, 4, 2)]
    public void F6_turns_the_sounds_off_and_on(string folder, byte offColor, byte onColor)
    {
        if (WalkthroughTests.Start(folder) is not { } player)
        {
            return;
        }

        var world = player.World;
        Press(world, F6, 1);
        Assert.True(world.SoundsOff);
        Assert.False(world.WaitsOnMessage);
        Assert.True(Count(world.Screen(), 0, 8, Shown(world, offColor)) > 20);

        world.ReadAudio();
        world.Sound.Start(Doorbell);
        var heard = new List<short>();
        for (int i = 0; i < 30; i++)
        {
            world.Step();
            heard.AddRange(world.ReadAudio());
        }

        Assert.All(heard, s => Assert.Equal(0, s));

        Steps(world, 200);
        Press(world, F6, 1);
        Assert.False(world.SoundsOff);
        Assert.True(Count(world.Screen(), 0, 8, Shown(world, onColor)) > 20);
        world.ReadAudio();
        world.Sound.Start(Doorbell);
        heard.Clear();
        for (int i = 0; i < 30; i++)
        {
            world.Step();
            heard.AddRange(world.ReadAudio());
        }

        Assert.Contains(heard, s => s != 0);
    }

    /// <summary>The NES keeps none of these: Space opens its own menu, and F8 and F6 do nothing of the interpreter's.</summary>
    [Fact]
    public void The_nes_keeps_its_own_keys()
    {
        if (WalkthroughTests.Start(WalkthroughTests.Nes) is not { } player)
        {
            return;
        }

        var world = player.World;
        foreach (int key in new[] { F8, F6, ' ' })
        {
            Press(world, key, 1);
            Assert.False(world.WaitsOnMessage);
            Assert.False(world.SoundsOff);
        }
    }

    /// <summary>Ctrl+C and Alt+X quit the engine, as in ScummVM; C alone is the game's.</summary>
    [Fact]
    public void Ctrl_c_and_alt_x_quit()
    {
        if (GameFolders.Find(WalkthroughTests.V2) is not { } path)
        {
            return;
        }

        var container = new DirectoryGameContainer(path);
        var provider = new ScummEngineProvider();
        foreach (var (key, modifiers, quits) in new[] { ('c', KeyModifiers.None, false), ('c', KeyModifiers.Ctrl, true), ('x', KeyModifiers.Alt, true), ('x', KeyModifiers.Ctrl, false) })
        {
            var host = new HeadlessHost();
            using var engine = provider.Create(host, container, provider.Detect(container)[0]);
            engine.Initialize();
            host.Run(engine, 5);
            host.Input.PushKey((Key)key, modifiers, key);
            Assert.Equal(quits, !engine.Tick(TimeSpan.Zero).Continue);
        }
    }
}
