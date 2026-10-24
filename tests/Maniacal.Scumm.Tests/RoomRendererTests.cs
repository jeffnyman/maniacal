using Daventry.Core.Storage;
using Maniacal.Scumm.Graphics;
using Maniacal.Scumm.Resources;

namespace Maniacal.Scumm.Tests;

public class RoomRendererTests
{
    private const string V1Folder = "maniac_mansion_original_DOS.com";
    private const string C64Folder = "maniac_mansion_c64";
    private const string V2Folder = "maniac_mansion_enhanced_DOS.com";

    [Fact]
    public void Runs_fill_columns_top_to_bottom_and_the_mask_follows()
    {
        // 8x2: one run of 16 pixels of colour 5 (length 0, so the length is the next byte),
        // then the mask: the byte 0xF0 repeated for both rows
        byte[] data = [0x05, 0x10, 0x82, 0xF0];

        var image = V2Bitmap.Decode(data, 8, 2, out int end);

        Assert.Equal(data.Length, end);
        Assert.All(image.Pixels.Pixels, pixel => Assert.Equal(5, pixel));
        for (int y = 0; y < 2; y++)
        {
            Assert.Equal([1, 1, 1, 1, 0, 0, 0, 0], image.Mask.Row(y).ToArray());
        }
    }

    [Fact]
    public void Dither_runs_repeat_the_previous_column()
    {
        // 8x2, column by column: colour 3 for column 0, a dither run copying it to columns 1 and 2,
        // then ten pixels of colour 7 (length 0, so the length is the next byte); then an empty mask
        byte[] data = [0x23, 0x84, 0x07, 0x0A, 0x82, 0x00];
        var image = V2Bitmap.Decode(data, 8, 2, out _);

        for (int y = 0; y < 2; y++)
        {
            Assert.Equal([3, 3, 3, 7, 7, 7, 7, 7], image.Pixels.Row(y).ToArray());
        }
    }

    [Fact]
    public void Truncated_data_is_rejected()
    {
        Assert.Throws<InvalidDataException>(() => V2Bitmap.Decode([0x05], 8, 2, out _));
    }

    /// <summary>
    /// The picture and its mask fill the room up to the first object image, so decoding a room
    /// should end exactly there.
    /// </summary>
    [Fact]
    public void Every_v2_room_decodes_up_to_its_object_images()
    {
        if (Open(V2Folder) is not { } loader)
        {
            return;
        }

        for (int number = 1; number <= 53; number++)
        {
            var room = loader.LoadRoom(number)!;
            var image = V2Bitmap.Decode(room.Data.Span[room.ImageOffset..], room.Width, room.Height, out int length);

            Assert.Equal(room.Width, image.Pixels.Width);
            Assert.Equal(room.Height, image.Pixels.Height);
            Assert.All(image.Pixels.Pixels, pixel => Assert.InRange(pixel, 0, 15));

            int end = room.ImageOffset + length;
            var images = room.Objects.Select(o => o.ImageOffset).Where(offset => offset > room.ImageOffset).ToList();
            if (images.Count > 0)
            {
                Assert.True(end == images.Min(), $"room {number}: picture ends at {end}, first object image at {images.Min()}");
            }
            else
            {
                Assert.InRange(end, room.ImageOffset, room.Data.Length);
            }
        }
    }

    [Fact]
    public void Front_of_the_mansion_picture_is_stable()
    {
        if (Open(V2Folder) is not { } loader)
        {
            return;
        }

        var image = RoomRenderer.DrawBackground(loader.LoadRoom(1)!);

        // pinned after checking the picture by eye against the room map
        Assert.Equal(PixelsMd5, Md5.ComputeHex(image.Pixels.Pixels));
        Assert.Equal(MaskMd5, Md5.ComputeHex(image.Mask.Pixels));
    }

    [Fact]
    public void V1_layers_use_common_values_repeats_and_literals()
    {
        // common values 7, 8, 9, 10; then: common value 1 three times (0x80 | 1 << 5 | 2),
        // the byte 0x55 twice (0x40 | 1), and two literals (0x01)
        byte[] data = [7, 8, 9, 10, 0xA2, 0x41, 0x55, 0x01, 0x11, 0x22];

        var layer = V1Bitmap.DecodeLayer(data, 0, 7, out int end);

        Assert.Equal([8, 8, 8, 0x55, 0x55, 0x11, 0x22], layer);
        Assert.Equal(data.Length, end);
    }

    /// <summary>As in version 2, the room's graphics end where its object images begin: here after the mask characters.</summary>
    [Fact]
    public void Every_v1_room_decodes_up_to_its_object_images()
    {
        if (Open(V1Folder) is not { } loader)
        {
            return;
        }

        for (int number = 1; number <= 52; number++)
        {
            var room = loader.LoadRoom(number)!;
            var image = RoomRenderer.DrawBackground(room);

            Assert.Equal(room.Width, image.Pixels.Width);
            Assert.Equal(room.Height, image.Pixels.Height);
            Assert.All(image.Pixels.Pixels, pixel => Assert.InRange(pixel, 0, 15));

            var data = room.Data.Span;
            int maskOffset = data[18] | (data[19] << 8);
            V1Bitmap.DecodeLayer(data, maskOffset + 2, (data[maskOffset] | (data[maskOffset + 1] << 8)) - 8, out int end);
            var images = room.Objects.Select(o => o.ImageOffset).Where(offset => offset > maskOffset).ToList();
            if (images.Count > 0)
            {
                Assert.True(end == images.Min(), $"room {number}: graphics end at {end}, first object image at {images.Min()}");
            }
            else
            {
                Assert.InRange(end, maskOffset, room.Data.Length);
            }
        }
    }

    [Fact]
    public void V1_front_of_the_mansion_picture_is_stable()
    {
        if (Open(V1Folder) is not { } loader)
        {
            return;
        }

        var image = RoomRenderer.DrawBackground(loader.LoadRoom(1)!);

        // pinned after checking the picture by eye against the room map
        Assert.Equal(V1PixelsMd5, Md5.ComputeHex(image.Pixels.Pixels));
        Assert.Equal(V1MaskMd5, Md5.ComputeHex(image.Mask.Pixels));
    }

    /// <summary>
    /// The C64's rooms are drawn as version 1's are, in the C64's own colour numbers. Their layers
    /// are in another order, the mask map last, and end where the object images begin, or where
    /// the object code does in a room without images.
    /// </summary>
    [Fact]
    public void Every_c64_room_decodes_up_to_what_follows_its_picture()
    {
        if (Open(C64Folder) is not { } loader)
        {
            return;
        }

        for (int number = 1; number <= 52; number++)
        {
            var room = loader.LoadRoom(number)!;
            var image = RoomRenderer.DrawBackground(room);

            Assert.Equal(room.Width, image.Pixels.Width);
            Assert.Equal(room.Height, image.Pixels.Height);
            Assert.All(image.Pixels.Pixels, pixel => Assert.InRange(pixel, 0, 15));

            var images = room.Objects.Where(o => o.ImageOffset > 0).Select(o => o.ImageOffset).ToList();
            int next = images.Count > 0 ? images.Min()
                : room.Objects.Count > 0 ? room.Objects.Min(o => o.CodeOffset)
                : room.EntryScriptOffset;
            Assert.True(V1Bitmap.ForRoom(room).GraphicsEnd == next, $"room {number}: graphics end at {V1Bitmap.ForRoom(room).GraphicsEnd}, next part at {next}");
        }
    }

    [Fact]
    public void C64_front_of_the_mansion_picture_is_stable()
    {
        if (Open(C64Folder) is not { } loader)
        {
            return;
        }

        var image = RoomRenderer.DrawBackground(loader.LoadRoom(1)!);

        // pinned after checking the picture by eye
        Assert.Equal(C64PixelsMd5, Md5.ComputeHex(image.Pixels.Pixels));
        Assert.Equal(C64MaskMd5, Md5.ComputeHex(image.Mask.Pixels));
    }

    /// <summary>
    /// At the start of the game the package (not delivered yet) and the three tombstones (no kid
    /// has died) are in state 8: their images, empty ground, are drawn over the background, which
    /// shows them. The script run when a kid dies clears state 8 to reveal a tombstone.
    /// </summary>
    [Theory]
    [InlineData(V1Folder)]
    [InlineData(V2Folder)]
    public void Front_of_the_mansion_hides_the_package_and_tombstones_at_the_start(string folder)
    {
        if (Open(folder) is not { } loader)
        {
            return;
        }

        var room = loader.LoadRoom(1)!;
        var states = loader.Index.ObjectStates;

        var visible = RoomRenderer.VisibleObjects(room, n => states[n]).Select(o => o.Number).ToList();

        Assert.Contains(57, visible);  // package
        Assert.Contains(62, visible);  // tombstones
        Assert.Contains(63, visible);
        Assert.Contains(64, visible);
        Assert.DoesNotContain(61, visible);  // the front door is closed: its open image is not drawn
    }

    /// <summary>The key under the door mat shows only when the mat, its parent, has been moved (state 8).</summary>
    [Theory]
    [InlineData(V1Folder)]
    [InlineData(V2Folder)]
    public void The_key_shows_only_when_the_mat_is_moved(string folder)
    {
        if (Open(folder) is not { } loader)
        {
            return;
        }

        var room = loader.LoadRoom(1)!;
        bool KeyVisible(int matState) => RoomRenderer
            .VisibleObjects(room, n => n switch { 54 => matState, 55 => 8, _ => 0 })
            .Any(o => o.Number == 55);

        Assert.False(KeyVisible(0));
        Assert.True(KeyVisible(8));
    }

    /// <summary>Every image that clearly belongs to its object decodes, in every release.</summary>
    [Theory]
    [InlineData(V1Folder, 52, 246)]
    [InlineData(V2Folder, 53, 258)]
    [InlineData(C64Folder, 52, 250)]
    public void Every_owned_object_image_decodes(string folder, int lastRoom, int expected)
    {
        if (Open(folder) is not { } loader)
        {
            return;
        }

        int count = 0;
        for (int number = 1; number <= lastRoom; number++)
        {
            var room = loader.LoadRoom(number)!;
            foreach (var obj in room.Objects.Where(o => RoomRenderer.OwnsImage(room, o)))
            {
                var image = RoomRenderer.DrawObject(room, obj)!;
                Assert.Equal(obj.Width, image.Pixels.Width);
                Assert.Equal(obj.Height, image.Pixels.Height);
                count++;
            }
        }

        Assert.Equal(expected, count);
    }

    private const string PixelsMd5 = "ed148d7b10998fd612d592cbc41ce7f1";
    private const string MaskMd5 = "8ce08ed6bde1b4bde0e7d4286c33239e";
    private const string V1PixelsMd5 = "abf8a1ba6c3162137c3286a7bec29c72";
    private const string V1MaskMd5 = "5bfbbed378058da55624a4e8deb5c540";
    private const string C64PixelsMd5 = "30da2356714337ce172f50c8709f69d4";
    private const string C64MaskMd5 = "388ff7a9cf4e55e8e8430737f46ac501";

    private static ResourceLoader? Open(string folder)
        => GameFolders.Find(folder) is { } path ? ResourceLoader.Open(new DirectoryGameContainer(path)) : null;
}
