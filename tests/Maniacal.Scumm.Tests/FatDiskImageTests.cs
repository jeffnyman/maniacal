using System.Buffers.Binary;
using Maniacal.Scumm.Detection;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Storage;

namespace Maniacal.Scumm.Tests;

/// <summary>
/// The Atari ST's disk image, a FAT12 floppy, read as the folder of files it holds. The tests with
/// the release's disk pass without checking when its folder is absent.
/// </summary>
public class FatDiskImageTests
{
    // an Atari ST double-sided floppy: 512-byte sectors, two a cluster, one reserved, two FATs of
    // three sectors, 112 root entries, 1440 sectors
    private const int SectorSize = 512, ClusterSize = 1024, Root = (1 + 2 * 3) * SectorSize, Data = Root + 112 * 32, DiskSize = 1440 * SectorSize;

    private static byte[] Floppy()
    {
        var disk = new byte[DiskSize];
        BinaryPrimitives.WriteUInt16LittleEndian(disk.AsSpan(11), SectorSize);
        disk[13] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(disk.AsSpan(14), 1);
        disk[16] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(disk.AsSpan(17), 112);
        BinaryPrimitives.WriteUInt16LittleEndian(disk.AsSpan(19), 1440);
        BinaryPrimitives.WriteUInt16LittleEndian(disk.AsSpan(22), 3);
        return disk;
    }

    // a twelve-bit FAT entry, in the first FAT
    private static void Link(byte[] disk, int cluster, int next)
    {
        int at = SectorSize + cluster * 3 / 2;
        int pair = disk[at] | (disk[at + 1] << 8);
        pair = (cluster & 1) != 0 ? (pair & 0x000F) | (next << 4) : (pair & 0xF000) | next;
        disk[at] = (byte)pair;
        disk[at + 1] = (byte)(pair >> 8);
    }

    private static void Entry(byte[] disk, int at, string name, string extension, byte attributes, int cluster, int size)
    {
        System.Text.Encoding.ASCII.GetBytes(name.PadRight(8) + extension.PadRight(3)).CopyTo(disk, at);
        disk[at + 11] = attributes;
        BinaryPrimitives.WriteUInt16LittleEndian(disk.AsSpan(at + 26), (ushort)cluster);
        BinaryPrimitives.WriteUInt32LittleEndian(disk.AsSpan(at + 28), (uint)size);
    }

    private static int Cluster(int number) => Data + (number - 2) * ClusterSize;

    /// <summary>
    /// A disk with the volume's label, a deleted file, 00.LFL across two clusters out of order, and a
    /// folder holding a file: the label and the deleted file are passed over, 00.LFL reads whole
    /// along its chain, and the folder's file is under the folder's name.
    /// </summary>
    [Fact]
    public void Files_are_read_along_their_cluster_chains()
    {
        var disk = Floppy();
        Entry(disk, Root, "MANIAC", "", 0x08, 0, 0);
        Entry(disk, Root + 32, "OLD", "TXT", 0, 9, 10);
        disk[Root + 32] = 0xE5;
        Entry(disk, Root + 64, "00", "LFL", 0, 4, 1500);
        Entry(disk, Root + 96, "SOLVE", "", 0x10, 3, 0);

        var index = Enumerable.Range(0, 1500).Select(i => (byte)i).ToArray();
        index.AsSpan(0, ClusterSize).CopyTo(disk.AsSpan(Cluster(4)));
        index.AsSpan(ClusterSize).CopyTo(disk.AsSpan(Cluster(2)));
        Link(disk, 4, 2);
        Link(disk, 2, 0xFFF);
        Link(disk, 3, 0xFFF);
        Link(disk, 5, 0xFFF);
        Entry(disk, Cluster(3), ".", "", 0x10, 3, 0);
        Entry(disk, Cluster(3) + 32, "..", "", 0x10, 0, 0);
        Entry(disk, Cluster(3) + 64, "MANIAC", "SOL", 0, 5, 5);
        "hints"u8.CopyTo(disk.AsSpan(Cluster(5)));

        var image = FatDiskImage.Open(new MemoryGameContainer("st").Add("Maniac Mansion.st", disk));

        Assert.NotNull(image);
        Assert.Equal(["00.LFL", "SOLVE/MANIAC.SOL"], image.EnumerateFiles().Order());
        Assert.Equal(index, image.ReadAllBytes("00.lfl"));
        Assert.Equal("hints"u8.ToArray(), image.ReadAllBytes("SOLVE/MANIAC.SOL"));
        Assert.Throws<FileNotFoundException>(() => image.Open("01.LFL"));
    }

    /// <summary>A disk that is no FAT floppy, a chain that loops, or a floppy without the game's index is not taken for the game.</summary>
    [Fact]
    public void Other_disks_are_not_taken_for_the_game()
    {
        var noise = new byte[DiskSize];
        new Random(1).NextBytes(noise);
        Assert.Null(FatDiskImage.Open(new MemoryGameContainer("st").Add("noise.st", noise)));

        var looping = Floppy();
        Entry(looping, Root, "00", "LFL", 0, 2, 5000);
        Link(looping, 2, 3);
        Link(looping, 3, 2);
        Assert.Null(FatDiskImage.Open(new MemoryGameContainer("st").Add("loop.st", looping)));

        var other = Floppy();
        Entry(other, Root, "README", "TXT", 0, 2, 5);
        Link(other, 2, 0xFFF);
        Assert.Null(FatDiskImage.Open(new MemoryGameContainer("st").Add("other.st", other)));
        Assert.NotNull(FatDiskImage.FromBytes("other", other));
    }

    /// <summary>
    /// The Atari ST release's disk: its 55 files named as rooms, the index ScummVM's for the English
    /// Atari ST release, its rooms 1 to 53 and the scripts, sounds and costumes the index names
    /// reading whole but where DOS's 1988 release's do not either (script 134, sounds 1, 2, 47 to 49
    /// and 53, whose entries point at nothing whole; and the index names room slots to 60 with no
    /// files behind them), and sound 55, whose length word, 10000, runs past the end of room 30;
    /// and opened as the release. Its <c>54.LFL</c> is no room but
    /// the interpreter, an Atari ST program (it starts with 0x601A), which <c>MANIAC.PRG</c> runs.
    /// </summary>
    [Fact]
    public void The_atari_st_disk_holds_the_release()
    {
        if (GameFolders.Find(WalkthroughTests.AtariSt) is not { } path)
        {
            return;
        }

        var folder = new DirectoryGameContainer(path);
        var disk = FatDiskImage.Open(folder)!;
        Assert.Equal(55, disk.EnumerateFiles().Count(f => f.EndsWith(".LFL", StringComparison.OrdinalIgnoreCase)));

        var game = ScummDetector.Identify(folder);
        Assert.Equal("Maniac Mansion (V2, English, Atari ST)", game?.Description);
        Assert.Equal("0c331637580950aea2346e012ef2a868", game?.Entry.Md5);
        Assert.IsType<FatDiskImage>(ResourceLoader.Open(folder).Game);

        var index = ResourceIndex.Parse(LflFile.Read(disk, 0));
        var loader = new ResourceLoader(disk, index);
        Assert.Equal(2, index.Version);
        List<int> Unreadable(ResourceType type)
            => Enumerable.Range(0, index.Count(type)).Where(i => index.Locate(type, i) is { IsPresent: true, Room: > 0 } && !loader.IsReadable(type, i)).ToList();

        Assert.Equal([134], Unreadable(ResourceType.Script));
        Assert.Equal([1, 2, 47, 48, 49, 53, 55], Unreadable(ResourceType.Sound));
        Assert.Empty(Unreadable(ResourceType.Costume));

        Assert.All(Enumerable.Range(1, 53), room => Assert.True(loader.IsReadable(ResourceType.Room, room) && loader.LoadRoom(room) is not null, $"room {room}"));
        Assert.Equal([0x60, 0x1A], disk.ReadAllBytes("54.LFL")[..2]);
    }
}
