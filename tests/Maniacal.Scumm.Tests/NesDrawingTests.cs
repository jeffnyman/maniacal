using Maniacal.Scumm.Game;
using Maniacal.Scumm.Graphics;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Storage;

namespace Maniacal.Scumm.Tests;

/// <summary>
/// The NES's rooms drawn from its tiles, as ScummVM's GdiNES draws them, on its 256 by 240 screen.
/// The tests with the cartridge pass without checking when its folder is absent.
/// </summary>
public class NesDrawingTests
{
    private const int Driveway = 44, SelectionRoom = 45, FrontOfTheHouse = 1, ScreenWidth = 256, ScreenHeight = 240, RoomTop = 32, RoomBottom = 160;

    private static ResourceLoader? Loader()
        => GameFolders.Find(WalkthroughTests.Nes) is { } path && NesRom.Open(new DirectoryGameContainer(path)) is { } rom
            ? new ResourceLoader(rom, ResourceIndex.Parse(LflFile.Read(rom, 0)))
            : null;

    /// <summary>
    /// Every room draws, with every image its objects have: 128 lines high, as wide as the room
    /// says or the screen's 256, the narrow ones (28 tiles) moved two strips right.
    /// </summary>
    [Fact]
    public void Every_room_draws()
    {
        if (Loader() is not { } loader)
        {
            return;
        }

        int narrow = 0;
        for (int number = 1; number < 55; number++)
        {
            var room = loader.LoadRoom(number)!;
            var image = RoomRenderer.RenderAll(room);

            Assert.Equal(128, image.Pixels.Height);
            Assert.Equal(room.Width, image.Pixels.Width);
            Assert.True(room.Width >= ScreenWidth);
            Assert.All(image.Pixels.Pixels, color => Assert.InRange(color, 0, 63));
            if (room.NesStartStrip > 0)
            {
                Assert.Equal(2, room.NesStartStrip);
                narrow++;
            }
        }

        Assert.Equal(37, narrow);
    }

    /// <summary>
    /// The intro's hillside (room 33) 107 steps in, as ScummVM 2026.4.0 showed it in its window: the
    /// room's lines matched to the pixel, the text over it aside.
    /// </summary>
    [Fact]
    public void The_intro_room_is_drawn_as_scummvm_draws_it()
    {
        if (Loader() is not { } loader)
        {
            return;
        }

        var world = new GameWorld(loader);
        world.Boot();
        for (int step = 0; step < 107; step++)
        {
            world.Step();
        }

        var room = world.Screen().Pixels.AsSpan(RoomTop * ScreenWidth, (RoomBottom - RoomTop) * ScreenWidth);

        Assert.Equal(33, world.CurrentRoom?.Number);
        Assert.Equal("23a7ac651734fe88871c8e8985ac099c78eacea94b8ca554b40d06e9f78be4aa", Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(room)));
    }

    /// <summary>
    /// Whole screens as ScummVM 2026.4.0 showed them in its window, to the pixel: the meteor (an
    /// actor in the NES's costume set 1) flying over the hillside, the credits in the NES's font
    /// over it, the copyright and the credits the NES spells out in its verb area, and the kid
    /// selection under its line of text.
    /// </summary>
    [Theory]
    [InlineData(82, 33, "41d98a4421e196add4d9a517fbff339f846095b41b57532b2b24fdf475099862")]
    [InlineData(228, 33, "4535aff331557e21c532386cfa716746d49058547d29f9be8fc5df4f2a15276c")]
    [InlineData(320, 33, "3553bf944b5164c400e61514a1fdcd4e742727493b30b012e40ce20cce4f3da9")]
    [InlineData(362, 33, "9abd51936c7d4407c04e8eb919151ae256e21dbe796939702e6025569f1507b1")]
    [InlineData(682, SelectionRoom, "e789327d8495caf0aa605cf22ea9f796ca53433edf5712a87113f0bc123dc73a")]
    public void Screens_are_drawn_as_scummvm_draws_them(int steps, int room, string hash)
    {
        if (Loader() is not { } loader)
        {
            return;
        }

        var world = new GameWorld(loader);
        world.Boot();
        for (int step = 0; step < steps; step++)
        {
            world.Step();
        }

        Assert.Equal(room, world.CurrentRoom?.Number);
        Assert.Equal(hash, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(world.Screen().Pixels)));
    }

    /// <summary>The kid selection with the NES's cursor where ScummVM's mouse rests, at the top left, as its window showed it.</summary>
    [Fact]
    public void The_selection_shows_the_cursor_as_scummvm_does()
    {
        if (Loader() is not { } loader)
        {
            return;
        }

        var world = new GameWorld(loader);
        world.Boot();
        world.MoveMouse(0, 0);
        for (int step = 0; step < 682; step++)
        {
            world.Step();
        }

        var screen = new Surface8(ScreenWidth, ScreenHeight);
        screen.CopyFrom(world.Screen());
        world.DrawCursor(screen);

        Assert.Equal("a0443d4d80d95e9a321c56d881434e724d79c86a9fbd42c0eb41c43b7c668e8e", Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(screen.Pixels)));
    }

    /// <summary>
    /// An NES costume animates one limb: an animation starts at its first frame and steps through
    /// its list, back to the first after the last (NESCostumeLoader).
    /// </summary>
    [Fact]
    public void An_nes_animation_loops_through_its_frames()
    {
        var state = new Costumes.CostumeState();
        state.DecodeNes(animation: 9, frames: 3);

        Assert.Equal(9, state.Animation(0));
        Assert.Equal([0, 1, 2, 0], new[] { state.Position(0), Step(), Step(), Step() });
        Assert.Equal(Costumes.CostumeState.None, state.Position(1));

        int Step()
        {
            state.IncreaseNes();
            return state.Position(0);
        }
    }

    /// <summary>The NES's 64 colours, which the world shows the release with.</summary>
    [Fact]
    public void The_palette_is_the_nes_one()
    {
        Assert.Equal(64, Palettes.Nes.Length);
        Assert.Equal(new Rgb(0x66, 0x66, 0x66), Palettes.Nes[0]);
        Assert.Equal(new Rgb(0x00, 0x00, 0x00), Palettes.Nes[NesBitmap.Black]);

        if (Loader() is not { } loader)
        {
            return;
        }

        Assert.Equal(64, new GameWorld(loader).Palette.Length);
    }

    /// <summary>Without its lights, a room is drawn in the four colours of the dark palette (drawStripNES).</summary>
    [Fact]
    public void A_dark_room_is_drawn_in_the_dark_palette()
    {
        if (Loader() is not { } loader)
        {
            return;
        }

        var dark = NesBitmap.ForRoom(loader.LoadRoom(FrontOfTheHouse)!).DrawBackground(lit: false);

        Assert.All(dark.Pixels.Pixels, color => Assert.Contains(color, new byte[] { 0x2D, 0x1D, 0x3D, 0x20 }));
    }

    /// <summary>
    /// The screen is 256 by 240, black (the NES's 0x1D) but for the room from line 32 to 160; a
    /// narrow room shows 16 pixels in from each side, a wide one across the screen.
    /// </summary>
    [Fact]
    public void Rooms_show_on_the_nes_screen()
    {
        if (Loader() is not { } loader)
        {
            return;
        }

        var world = new GameWorld(loader);
        world.Boot();
        for (int step = 0; step < 2000 && world.CurrentRoom?.Number != SelectionRoom; step++)
        {
            world.Step();
        }

        world.Step();
        var narrow = world.Screen();

        Assert.Equal(ScreenWidth, narrow.Width);
        Assert.Equal(ScreenHeight, narrow.Height);
        for (int y = 0; y < ScreenHeight; y++)
        {
            bool inRoom = y is >= RoomTop and < RoomBottom;
            Assert.Equal(NesBitmap.Black, narrow[0, y]);
            Assert.Equal(NesBitmap.Black, narrow[15, y]);
            Assert.Equal(NesBitmap.Black, narrow[ScreenWidth - 1, y]);
            if (!inRoom)
            {
                Assert.All(narrow.Row(y).ToArray(), color => Assert.Equal(NesBitmap.Black, color));
            }
        }

        // the room's own picture, two strips in: its first column is the screen's 16th
        var room = RoomRenderer.DrawBackground(world.CurrentRoom!);
        Assert.Equal(room.Pixels[0, 40], narrow[16, RoomTop + 40]);
    }
}
