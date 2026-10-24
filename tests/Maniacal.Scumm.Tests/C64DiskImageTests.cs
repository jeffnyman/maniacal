using System.Buffers.Binary;
using Daventry.Core.Storage;
using Maniacal.Scumm.Resources;

namespace Maniacal.Scumm.Tests;

/// <summary>The C64 disk images, read into the loose files of the DOS releases.</summary>
public class C64DiskImageTests
{
    private const int DiskSize = 174848, IndexLength = 1188;

    // where the index's room tables start on the first disk: after the signature and 256 object flags
    private const int RoomDisks = 2 + 256, RoomPlaces = RoomDisks + 55;

    /// <summary>
    /// Two disks: the index on the first, with room 1 on the second disk at track 2, sector 3; there
    /// the room file's 11 resources, the first after a padding word.
    /// </summary>
    private static (byte[] Disk1, byte[] Disk2) Disks()
    {
        var disk1 = new byte[DiskSize];
        var disk2 = new byte[DiskSize];
        BinaryPrimitives.WriteUInt16LittleEndian(disk1, C64DiskImage.Disk1Signature);
        BinaryPrimitives.WriteUInt16LittleEndian(disk2, C64DiskImage.Disk2Signature);
        disk1[RoomDisks + 1] = (byte)'2';
        disk1[RoomPlaces + 2] = 3;
        disk1[RoomPlaces + 3] = 2;

        int at = (21 + 3) * 256;
        byte[] room = [0xFF, 0xFF, 0x04, 0x00, 0xAA, 0xBB, .. Enumerable.Repeat<byte[]>([0x02, 0x00], 10).SelectMany(b => b)];
        room.CopyTo(disk2, at);
        return (disk1, disk2);
    }

    private static byte[] Decoded(IGameContainer game, string name)
    {
        var data = game.ReadAllBytes(name);
        LflFile.Decode(data);
        return data;
    }

    [Fact]
    public void A_room_file_is_the_resources_at_the_rooms_place_on_its_disk()
    {
        var (disk1, disk2) = Disks();
        var image = C64DiskImage.Open(new MemoryGameContainer("c64").Add("maniac1.d64", disk1).Add("maniac2.d64", disk2));

        Assert.NotNull(image);
        byte[] expected = [0xFF, 0xFF, 0x04, 0x00, 0xAA, 0xBB, .. Enumerable.Repeat<byte[]>([0x02, 0x00], 10).SelectMany(b => b)];
        Assert.Equal(expected, Decoded(image, "01.LFL"));
    }

    [Fact]
    public void The_index_is_rebuilt_with_the_second_disks_signature()
    {
        var (disk1, disk2) = Disks();
        var image = C64DiskImage.Open(new MemoryGameContainer("c64").Add("maniac1.d64", disk1).Add("maniac2.d64", disk2))!;

        var index = Decoded(image, "00.LFL");
        Assert.Equal(IndexLength, index.Length);
        Assert.Equal(ResourceIndex.C64Magic, BinaryPrimitives.ReadUInt16LittleEndian(index));
        Assert.Equal(disk1.AsSpan(2, IndexLength - 2).ToArray(), index[2..]);

        var parsed = ResourceIndex.Parse(index);
        Assert.Equal(0, parsed.Version);
        Assert.Equal(new ResourceLocation(1, 0), parsed.Locate(ResourceType.Room, 1));
    }

    [Fact]
    public void Only_rooms_with_resources_have_files()
    {
        var (disk1, disk2) = Disks();
        var image = C64DiskImage.Open(new MemoryGameContainer("c64").Add("maniac1.d64", disk1).Add("maniac2.d64", disk2))!;

        Assert.True(image.Exists("00.LFL"));
        Assert.True(image.Exists("52.lfl"));
        Assert.False(image.Exists("53.LFL"));
        Assert.False(image.Exists("55.LFL"));
        Assert.Equal(53, image.EnumerateFiles().Count());
        Assert.Throws<FileNotFoundException>(() => image.Open("53.LFL"));
    }

    /// <summary>Disks named otherwise are told apart by the words they start with.</summary>
    [Fact]
    public void Disks_with_other_names_are_found_by_their_signatures()
    {
        var (disk1, disk2) = Disks();
        var folder = new MemoryGameContainer("c64").Add("Side B.d64", disk2).Add("Side A.d64", disk1);

        var image = C64DiskImage.Open(folder);

        Assert.NotNull(image);
        Assert.Equal(0xAA, Decoded(image, "01.LFL")[4]);
    }

    [Fact]
    public void A_folder_without_both_disks_has_no_image()
    {
        var (disk1, _) = Disks();

        Assert.Null(C64DiskImage.Open(new MemoryGameContainer("dos").Add("00.LFL", [0xCE, 0xF5])));
        Assert.Null(C64DiskImage.Open(new MemoryGameContainer("one").Add("maniac1.d64", disk1)));
        Assert.Null(C64DiskImage.Open(new MemoryGameContainer("same").Add("a.d64", disk1).Add("b.d64", disk1)));
    }
}
