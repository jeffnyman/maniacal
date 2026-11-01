using Maniacal.Scumm.Game;
using Maniacal.Scumm.Graphics;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Saves;
using Maniacal.Scumm.Storage;

namespace Maniacal.Scumm.Tests;

/// <summary>
/// The Atari ST release played: the 1988 game in the Amiga's colours, with a cursor of its own.
/// Maniacal refuses the release for now, so the world is made from its disk directly. The tests
/// pass without checking when its folder is absent.
/// </summary>
public class AtariStTests
{
    private const int SelectionRoom = 45, Hall = 10;

    // the cursor's picture: 16 by 16, its hotspot in the middle (atari_main_cursor)
    private const int CursorSize = 16, Hotspot = 8;

    private static GameWorld? Booted()
    {
        if (GameFolders.Find(WalkthroughTests.AtariSt) is not { } path || FatDiskImage.Open(new DirectoryGameContainer(path)) is not { } disk)
        {
            return null;
        }

        var world = new GameWorld(new ResourceLoader(disk, ResourceIndex.Parse(LflFile.Read(disk, 0))));
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

    /// <summary>The release is the Atari ST's, and draws in the Amiga's 16 colours, as ScummVM gives it (<c>resetPalette</c>).</summary>
    [Fact]
    public void It_draws_in_the_amiga_colours()
    {
        if (Booted() is not { } world)
        {
            return;
        }

        Assert.True(world.IsAtariSt);
        Assert.Equal(Palettes.Amiga.ToArray(), world.Palette.ToArray());
        Assert.Equal(new Rgb(0x00, 0x00, 0xBB), world.Palette.Span[1]);
    }

    /// <summary>
    /// Screens as ScummVM 2026.4.0 showed them in its window, to the pixel: the copyright over the
    /// kid selection, and the kid selection asking for two other kids.
    /// </summary>
    [Theory]
    [InlineData(16, "bf44e341ece6132621f5ec9cefc07fd68c738abffd989754f1b4111ebaf95ef1")]
    [InlineData(179, "eadef0794d6f4418baf55ba31010825183fb4f5563b0510344f5656fcf040e14")]
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
    /// The cursor is the Atari ST's own picture, its hotspot at the mouse, its marked pixels in the
    /// colour the animation is at; a save keeps it as ScummVM's does, 16 by 16 with its hotspot at
    /// (8, 8). Loaded in ScummVM, saves made in the driveway, on the porch and in the hall showed
    /// the same screens there, cursor and all.
    /// </summary>
    [Fact]
    public void The_cursor_is_the_atari_st_picture()
    {
        if (Booted() is not { } world)
        {
            return;
        }

        Steps(world, 179);
        Assert.True(world.CursorShown);
        world.MoveMouse(100, 100);
        var screen = new Surface8(320, 200);
        screen.Fill(0xEE);
        world.DrawCursor(screen);

        var marked = Enumerable.Range(0, CursorSize * CursorSize).Where(i => CursorImages.AtariSt[i] == 0x0F)
            .Select(i => (100 - Hotspot + i % CursorSize, 100 - Hotspot + i / CursorSize)).ToHashSet();
        var drawn = Enumerable.Range(0, 320 * 200).Where(i => screen.Pixels[i] != 0xEE).Select(i => (i % 320, i / 320)).ToHashSet();
        Assert.Equal(33, marked.Count);
        Assert.Equal(marked, drawn);
        Assert.Single(drawn.Select(p => screen[p.Item1, p.Item2]).Distinct());

        var (_, state) = SaveFile.Read(world.Save("selection", DateTime.Now), world.Engine.ObjectOwners.Length);
        Assert.Equal((CursorSize, CursorSize, Hotspot, Hotspot), (state.CursorWidth, state.CursorHeight, state.CursorHotspotX, state.CursorHotspotY));
        Assert.Equal(33, state.GrabbedCursor.Count(b => b != 0xFF));
        Assert.Equal(Palettes.Amiga.ToArray().SelectMany(c => new[] { c.R, c.G, c.B }), state.CurrentPalette.Take(16 * 3));
    }

    /// <summary>Syd and Bernard picked by clicking, the game starts in the driveway, and Dave goes into the house as on DOS, every instruction handled.</summary>
    [Fact]
    public void Dave_goes_into_the_house()
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
        p.Go("front door");

        Assert.Equal(Hall, p.Room);
        Assert.Empty(p.Unhandled);
    }
}
