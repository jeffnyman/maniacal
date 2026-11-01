using Maniacal.Scumm.Game;
using Maniacal.Scumm.Graphics;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Saves;
using Maniacal.Scumm.Storage;

namespace Maniacal.Scumm.Tests;

/// <summary>
/// The Amiga release played: the 1988 game in its colours, with a cursor of its own and no room
/// transitions, on a clock of 240 quarter-frames a second. Maniacal refuses the release for now,
/// so the world is made from its disks directly. The tests pass without checking when their
/// folder is absent.
/// </summary>
public class AmigaTests
{
    private const int SelectionRoom = 45, Hall = 10;

    // the cursor: 15 by 15, its hotspot in the middle
    private const int CursorSize = 15, Hotspot = 7;

    private static GameWorld? World()
        => GameFolders.Find(WalkthroughTests.Amiga) is { } path && AdfDiskImage.Open(new DirectoryGameContainer(path)) is { } disks
            ? new GameWorld(new ResourceLoader(disks, ResourceIndex.Parse(LflFile.Read(disks, 0))))
            : null;

    private static GameWorld? Booted()
    {
        if (World() is not { } world)
        {
            return null;
        }

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

    /// <summary>The release is the Amiga's: its 16 colours, and its clock at 240 quarter-frames a second (<c>setTimerAndShakeFrequency</c>).</summary>
    [Fact]
    public void It_draws_in_its_colours_on_its_clock()
    {
        if (Booted() is not { } world)
        {
            return;
        }

        Assert.True(world.IsAmiga);
        Assert.Equal(Palettes.Amiga.ToArray(), world.Palette.ToArray());
        Assert.Equal(240, world.TimerFrequency);
    }

    /// <summary>
    /// Screens as ScummVM 2026.4.0 showed them in its window, to the pixel but for its cursor at the
    /// window's corner: the copyright over the kid selection, and the kid selection.
    /// </summary>
    [Theory]
    [InlineData(16, "9851144c2b3d9ecc8b4714c6518097cd1d5896c9e87d0fac96d0fff78278a969")]
    [InlineData(359, "755c6e8c2be367009bc83df7c88f2e4844b7d50f0c38093dc27a7c66b9a252c1")]
    public void Screens_are_drawn_as_scummvm_draws_them(int steps, string hash)
    {
        if (Booted() is not { } world)
        {
            return;
        }

        Steps(world, steps);

        Assert.Equal(SelectionRoom, world.CurrentRoom?.Number);
        Assert.Equal(hash, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(world.Screen().Pixels)));
    }

    /// <summary>
    /// The cursor is the Amiga's cross, its arms 3 to 7 pixels from the hotspot, each with an
    /// arrowhead pointing in, in the colour the animation is at; a save keeps it as ScummVM's does,
    /// 15 by 15 with its hotspot at (7, 7). Loaded in ScummVM, saves made in the driveway, on the
    /// porch and in the hall showed the same screens there, cursor and all.
    /// </summary>
    [Fact]
    public void The_cursor_is_the_amiga_cross()
    {
        if (Booted() is not { } world)
        {
            return;
        }

        Steps(world, 359);
        Assert.True(world.CursorShown);
        world.MoveMouse(100, 100);
        var screen = new Surface8(320, 200);
        screen.Fill(0xEE);
        world.DrawCursor(screen);

        var expected = new HashSet<(int, int)>();
        for (int i = 3; i <= 7; i++)
        {
            expected.UnionWith([(-i, 0), (i, 0), (0, -i), (0, i)]);
        }

        for (int i = 1; i <= 2; i++)
        {
            expected.UnionWith([(-3 - i, -i), (-3 - i, i), (3 + i, -i), (3 + i, i), (-i, -3 - i), (i, -3 - i), (-i, 3 + i), (i, 3 + i)]);
        }

        var drawn = Enumerable.Range(0, 320 * 200).Where(i => screen.Pixels[i] != 0xEE).Select(i => (i % 320 - 100, i / 320 - 100)).ToHashSet();
        Assert.Equal(36, expected.Count);
        Assert.Equal(expected, drawn);
        Assert.Single(drawn.Select(p => screen[p.Item1 + 100, p.Item2 + 100]).Distinct());

        var (_, state) = SaveFile.Read(world.Save("selection", DateTime.Now), world.Engine.ObjectOwners.Length);
        Assert.Equal((CursorSize, CursorSize, Hotspot, Hotspot), (state.CursorWidth, state.CursorHeight, state.CursorHotspotX, state.CursorHotspotY));
        Assert.Equal(36, state.GrabbedCursor.Count(b => b != 0xFF));
        Assert.Equal(Palettes.Amiga.ToArray().SelectMany(c => new[] { c.R, c.G, c.B }), state.CurrentPalette.Take(16 * 3));
    }

    /// <summary>
    /// Syd and Bernard picked, the game starts in the driveway, and Dave goes into the house as on
    /// DOS, every instruction handled; the rooms change at once, with no transition, as ScummVM has
    /// the Amiga's (<c>fadeIn</c> and <c>fadeOut</c>, "verified on WinUAE").
    /// </summary>
    [Fact]
    public void Dave_goes_into_the_house_without_transitions()
    {
        if (Booted() is not { } world)
        {
            return;
        }

        Steps(world, 300);
        foreach (var (x, y) in new[] { (82, 118), (202, 118), (290, 75) })
        {
            world.Click(x, y);
            Steps(world, 100);
        }

        for (int i = 0; i < 3000 && !(world.CurrentRoom?.Number == WalkthroughTests.Driveway && world.CursorShown); i++)
        {
            world.Step();
        }

        var p = new Player(world) { Kids = ["Dave", "Syd", "Bernard"] };
        p.Leave(left: true);
        p.Do("Pull", "door mat");
        p.Do("Pick up", "key", until: () => p.Carries("key"));
        p.Do("Unlock", "front door", "key");
        p.Do("Open", "front door");
        p.Verb("Walk to");
        int frontDoor = world.ObjectsHere().First(o => o.Name == "front door").Number;
        world.ClickObject(frontDoor);
        for (int i = 0; i < 400 && world.CurrentRoom?.Number != Hall; i++)
        {
            world.Step();
            Assert.Empty(world.Transition);
        }

        Assert.Equal(Hall, p.Room);
        Assert.Empty(p.Unhandled);
    }
}
