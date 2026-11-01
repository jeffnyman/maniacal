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
