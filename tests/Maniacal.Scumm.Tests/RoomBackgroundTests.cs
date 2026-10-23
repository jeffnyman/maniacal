using Daventry.Core.Storage;
using Maniacal.Scumm.Graphics;
using Maniacal.Scumm.Resources;

namespace Maniacal.Scumm.Tests;

public class RoomBackgroundTests
{
    private const string V1Folder = "maniac_mansion_original_DOS.com";
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

        var image = RoomBackground.Decode(loader.LoadRoom(1)!);

        // pinned after checking the picture by eye against the room map
        Assert.Equal(PixelsMd5, Md5.ComputeHex(image.Pixels.Pixels));
        Assert.Equal(MaskMd5, Md5.ComputeHex(image.Mask.Pixels));
    }

    [Fact]
    public void Version_1_rooms_are_not_supported_yet()
    {
        if (Open(V1Folder) is not { } loader)
        {
            return;
        }

        Assert.Throws<NotSupportedException>(() => RoomBackground.Decode(loader.LoadRoom(1)!));
    }

    private const string PixelsMd5 = "ed148d7b10998fd612d592cbc41ce7f1";
    private const string MaskMd5 = "8ce08ed6bde1b4bde0e7d4286c33239e";

    private static ResourceLoader? Open(string folder)
        => GameFolders.Find(folder) is { } path ? ResourceLoader.Open(new DirectoryGameContainer(path)) : null;
}
