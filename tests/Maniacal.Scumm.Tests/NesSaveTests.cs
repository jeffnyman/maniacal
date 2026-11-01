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
    /// A load made outside the save room starts the kid's song again (script 5, and the sound
    /// variable 224 names), as ScummVM does on the NES, its save screen's script having done so.
    /// </summary>
    [Fact]
    public void A_load_starts_the_song_again()
    {
        if (WalkthroughTests.Start(WalkthroughTests.Nes) is not { } p || Fresh() is not { } loaded)
        {
            return;
        }

        int song = p.World.Engine.Variables[VarNesMusic];
        Assert.NotEqual(0, song);
        Assert.True(p.World.Sound.IsRunning(song));

        loaded.Load(p.World.Save("driveway", DateTime.Now));
        loaded.Step();

        Assert.True(loaded.Sound.IsRunning(song));
    }

    private const int VarNesMusic = 224;

    /// <summary>
    /// The NES's own save screen (script 163, on Space or F5): its Save, and the save is in the
    /// store, the game going on where it was; in a fresh game, its Load, and the kid stands where he
    /// was when it was saved. Its buttons are objects of the save room, chosen as the mouse passes.
    /// </summary>
    [Fact]
    public void The_save_screen_saves_and_loads()
    {
        if (WalkthroughTests.Start(WalkthroughTests.Nes) is not { } p)
        {
            return;
        }

        var store = new MemorySaveStore();
        var world = p.World;
        world.SaveStore = store;
        world.Click(150, 120);
        p.Steps(60);
        int x = world.Actors[Dave].X;

        UseSaveScreen(world, ' ', SaveButton);
        Assert.Equal(["maniac.s01"], store.List("maniac."));
        Assert.Equal(WalkthroughTests.Driveway, world.CurrentRoom?.Number);
        Assert.Equal(x, world.Actors[Dave].X);

        var fresh = WalkthroughTests.Start(WalkthroughTests.Nes)!.World;
        fresh.SaveStore = store;
        Assert.NotEqual(x, fresh.Actors[Dave].X);

        UseSaveScreen(fresh, GameWorld.KeyF1 + 4, LoadButton);
        Assert.Equal(WalkthroughTests.Driveway, fresh.CurrentRoom?.Number);
        Assert.Equal(x, fresh.Actors[Dave].X);
    }

    /// <summary>
    /// The save screen left before the game has begun (variable 164 still 0, in the intro) starts
    /// the game over from its boot script (<c>terminateSaveMenuScript</c>'s restart).
    /// </summary>
    [Fact]
    public void The_save_screen_left_in_the_intro_starts_the_game_over()
    {
        if (Fresh() is not { } world)
        {
            return;
        }

        world.BootWithoutTitles();
        Steps(world, 120);
        Assert.Equal(TitleRoom, world.CurrentRoom?.Number);
        Assert.DoesNotContain(world.ActorsHere(), a => a.Number == Meteor && a.Visible);
        var played = world.Time;

        world.PressKey(' ');
        Steps(world, 30);
        Assert.Equal(GameWorld.SaveRoom, world.CurrentRoom?.Number);
        Assert.False(world.ObjectsHere().First(o => o.Number == SaveButton).Touchable);
        Assert.True(world.ClickObject(ResumeButton));
        Steps(world, 60);

        // the meteor, landed before, flies in again from the top of the sky
        Assert.Equal(TitleRoom, world.CurrentRoom?.Number);
        Assert.False(world.ShowingTitles);
        Assert.True(world.Time > played);
        Assert.Contains(world.ActorsHere(), a => a.Number == Meteor && a.Visible && a.X >= 40 && a.Y < 20);
    }

    // Dave, and the save screen's buttons (script 163)
    private const int Dave = 3, Meteor = 22, SaveButton = 541, LoadButton = 542, ResumeButton = 543;

    // a key that brings the save screen up, then one of its buttons
    private static void UseSaveScreen(GameWorld world, int key, int button)
    {
        world.PressKey(key);
        Steps(world, 30);
        Assert.Equal(GameWorld.SaveRoom, world.CurrentRoom?.Number);
        Assert.True(world.ClickObject(button));
        Steps(world, 90);
    }

    private static void Steps(GameWorld world, int count)
    {
        for (int i = 0; i < count; i++)
        {
            world.Step();
        }
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

        intro.BootWithoutTitles();
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
