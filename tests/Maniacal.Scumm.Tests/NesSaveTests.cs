using Maniacal.Scumm.Game;
using Maniacal.Scumm.Graphics;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Saves;
using Maniacal.Scumm.Storage;

namespace Maniacal.Scumm.Tests;

/// <summary>
/// The NES's saves: ScummVM's format with the NES's own values in it, as ScummVM 2026.4.0 writes
/// and reads them. The walkthrough's checkpoints (<see cref="WalkthroughTests"/>) loaded there show
/// the same screens, and ScummVM's own NES saves load here. The tests pass without checking when
/// the cartridge's folder is absent.
/// </summary>
public class NesSaveTests
{
    private const int TitleRoom = 33;

    private static GameWorld? Fresh()
    {
        if (GameFolders.Find(WalkthroughTests.Nes) is not { } path || NesRom.Open(new DirectoryGameContainer(path)) is not { } rom)
        {
            return null;
        }

        return new GameWorld(new ResourceLoader(rom, ResourceIndex.Parse(LflFile.Read(rom, 0))));
    }

    // a fresh world with a save's state, as the save screen restores it
    private static GameWorld Restored(byte[] file)
    {
        var world = Fresh()!;
        var (_, state) = SaveFile.Read(file, world.Engine.ObjectOwners.Length);
        world.Restore(state);
        return world;
    }

    /// <summary>
    /// A game saved on the way out of the driveway and restored plays on as the original: the
    /// room, variables, actors and costume set are the same, and the screens match step for step
    /// but for the message line and the sentence's two lines, which a load clears.
    /// </summary>
    [Fact]
    public void A_game_loaded_from_its_save_plays_on_as_the_original()
    {
        if (WalkthroughTests.Start(WalkthroughTests.Nes) is not { } p)
        {
            return;
        }

        var world = p.World;
        world.Click(4, 120);
        p.Steps(20);
        var loaded = Restored(world.Save("on the way", DateTime.Now));

        Assert.Equal(world.CurrentRoom?.Number, loaded.CurrentRoom?.Number);
        Assert.Equal(world.Engine.Variables, loaded.Engine.Variables);
        Assert.Equal(world.NesCostumeSetNumber, loaded.NesCostumeSetNumber);
        Assert.Equal(world.Actors.Select(a => (a.Room, a.X, a.Y, a.Facing, a.Moving)), loaded.Actors.Select(a => (a.Room, a.X, a.Y, a.Facing, a.Moving)));

        int width = world.Layout.Width, roomStart = world.Layout.RoomTop * width, sentenceStart = world.Verbs.Top * width, sentenceEnd = (world.Verbs.Top + 16) * width;
        for (int step = 0; step < 300; step++)
        {
            world.Step();
            loaded.Step();
            var a = world.Screen().Pixels;
            var b = loaded.Screen().Pixels;
            Assert.True(a.AsSpan(roomStart, sentenceStart - roomStart).SequenceEqual(b.AsSpan(roomStart, sentenceStart - roomStart))
                && a.AsSpan(sentenceEnd).SequenceEqual(b.AsSpan(sentenceEnd)), $"the screens differ at step {step}");
        }

        Assert.Equal(WalkthroughTests.FrontOfTheHouse, loaded.CurrentRoom?.Number);
    }

    /// <summary>
    /// A save holds what ScummVM keeps for the NES, as its own NES saves have it: the room's view
    /// at lines 16 to 144 (<c>initScreens</c>), the camera at the middle of 240 lines, the NES's 64
    /// colours with no darkened copy and the whole shadow palette, the 8 by 8 cursor tile, no
    /// charset loaded, and the costume set. ScummVM loads it on these.
    /// </summary>
    [Fact]
    public void A_save_holds_what_ScummVM_keeps_for_the_NES()
    {
        if (WalkthroughTests.Start(WalkthroughTests.Nes) is not { } p)
        {
            return;
        }

        var world = p.World;
        var (_, s) = SaveFile.Read(world.Save("driveway", DateTime.Now), world.Engine.ObjectOwners.Length);

        Assert.Equal((16, 144), (s.ScreenB, s.ScreenH));
        Assert.Equal((120, 120, 120), (s.CameraY, s.CameraDestY, s.CameraLastY));
        Assert.Equal(Palettes.Nes.ToArray().SelectMany(c => new[] { c.R, c.G, c.B }), s.CurrentPalette.Take(64 * 3));
        Assert.All(s.CurrentPalette.Skip(64 * 3), b => Assert.Equal(0, b));
        Assert.All(s.DarkenPalette, b => Assert.Equal(0, b));
        Assert.Equal(Enumerable.Range(0, 256).Select(i => (byte)i), s.ShadowPalette);
        Assert.Equal((8, 8, 0, 0), (s.CursorWidth, s.CursorHeight, s.CursorHotspotX, s.CursorHotspotY));
        Assert.All(s.GrabbedCursor.Skip(64), b => Assert.Equal(0xFF, b));
        Assert.Equal(-1, s.CharsetId);
        Assert.Equal(world.NesCostumeSetNumber, s.NesCostumeSet);
    }

    /// <summary>
    /// A load takes the costume set from the save, whichever the world had: a save made in the
    /// intro, in the other set, loaded into a game in the driveway draws the intro's figures.
    /// </summary>
    [Fact]
    public void A_load_takes_the_costume_set_from_the_save()
    {
        if (Fresh() is not { } intro || WalkthroughTests.Start(WalkthroughTests.Nes) is not { } p)
        {
            return;
        }

        intro.Boot();
        for (int step = 0; step < 200 && intro.CurrentRoom?.Number != TitleRoom; step++)
        {
            intro.Step();
        }

        Assert.Equal(1, intro.NesCostumeSetNumber);
        Assert.Equal(0, p.World.NesCostumeSetNumber);

        p.World.Load(intro.Save("intro", DateTime.Now));

        Assert.Equal(TitleRoom, p.World.CurrentRoom?.Number);
        Assert.Equal(1, p.World.NesCostumeSetNumber);
        for (int step = 0; step < 100; step++)
        {
            p.World.Step();
            p.World.Screen();
        }
    }
}
