using System.Buffers.Binary;
using Maniacal.Scumm.Detection;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Storage;

namespace Maniacal.Scumm.Tests;

/// <summary>
/// The Amiga's disk images, AmigaDOS floppies, read together as the folder of files they hold. The
/// tests with the release's disks pass without checking when their folder is absent.
/// </summary>
public class AdfDiskImageTests
{
    // a double-density floppy: 1760 blocks of 512 bytes, the root block in the middle
    private const int BlockSize = 512, Blocks = 1760, RootBlock = 880;

    private sealed class Disk(bool fast)
    {
        public byte[] Bytes { get; } = new byte[Blocks * BlockSize];

        public bool Fast => fast;

        public void Long(int block, int index, int value) => BinaryPrimitives.WriteInt32BigEndian(Bytes.AsSpan(block * BlockSize + index * 4), value);

        public void Name(int block, string name)
        {
            Bytes[block * BlockSize + BlockSize - 80] = (byte)name.Length;
            System.Text.Encoding.ASCII.GetBytes(name).CopyTo(Bytes, block * BlockSize + BlockSize - 79);
        }

        // a header block: a directory (2) or a file (-3), named
        public void Header(int block, int secondaryType, string name)
        {
            Long(block, 0, 2);
            Long(block, 127, secondaryType);
            Name(block, name);
        }

        // a file's data blocks: the bytes split as the file system splits them, listed in the
        // header (and, past the first block, in an extension list block when given one)
        public void File(int header, string name, byte[] data, int firstData, int? extension = null)
        {
            Header(header, -3, name);
            Long(header, 81, data.Length);
            int per = fast ? BlockSize : BlockSize - 24;
            int count = (data.Length + per - 1) / per;
            for (int i = 0; i < count; i++)
            {
                int block = firstData + i;
                var part = data.AsSpan(i * per, Math.Min(per, data.Length - i * per));
                if (fast)
                {
                    part.CopyTo(Bytes.AsSpan(block * BlockSize));
                }
                else
                {
                    Long(block, 0, 8);
                    Long(block, 3, part.Length);
                    part.CopyTo(Bytes.AsSpan(block * BlockSize + 24));
                }

                int list = extension is { } e && i > 0 ? e : header;
                int slot = extension is not null && i > 0 ? i - 1 : i;
                Long(list, 77 - slot, block);
            }

            if (extension is { } ext)
            {
                Long(header, 2, 1);
                Long(header, 126, ext);
                Long(ext, 0, 16);
                Long(ext, 2, count - 1);
            }
            else
            {
                Long(header, 2, count);
            }
        }
    }

    private static Disk NewDisk(bool fast, string name)
    {
        var disk = new Disk(fast);
        "DOS"u8.CopyTo(disk.Bytes);
        disk.Bytes[3] = (byte)(fast ? 1 : 0);
        disk.Header(RootBlock, 1, name);
        return disk;
    }

    private static byte[] Pattern(int length, int seed) => Enumerable.Range(0, length).Select(i => (byte)(i * 7 + seed)).ToArray();

    /// <summary>
    /// Two disks, the original file system and the fast one: the first holds the index and a room in
    /// its rooms folder (a hash chain of two, the room over two blocks through an extension list), the
    /// second another room and the index again; together they are one folder, each room found by
    /// its own name too, a file on both taken from the first.
    /// </summary>
    [Fact]
    public void Two_disks_read_as_one_folder()
    {
        var index = Pattern(700, 1);
        var room1 = Pattern(900, 2);
        var room2 = Pattern(600, 3);

        var first = NewDisk(fast: false, "Maniac Disk 1");
        first.Header(881, 2, "rooms");
        first.Long(RootBlock, 6 + 5, 881);
        first.File(882, "00.LFL", index, firstData: 900);
        first.File(883, "01.LFL", room1, firstData: 910, extension: 920);
        first.Long(881, 6 + 3, 882);
        first.Long(882, 124, 883);
        first.File(884, "Maniac", Pattern(10, 4), firstData: 930);
        first.Long(RootBlock, 6 + 40, 884);

        var second = NewDisk(fast: true, "Maniac Disk 2");
        second.Header(881, 2, "rooms");
        second.Long(RootBlock, 6, 881);
        second.File(882, "02.LFL", room2, firstData: 900);
        second.File(883, "00.LFL", Pattern(700, 9), firstData: 910);
        second.Long(881, 6 + 7, 882);
        second.Long(881, 6 + 8, 883);

        var image = AdfDiskImage.Open(new MemoryGameContainer("amiga").Add("Disk 1.adf", first.Bytes).Add("Disk 2.adf", second.Bytes));

        Assert.NotNull(image);
        Assert.Equal(["Maniac Disk 1", "Maniac Disk 2"], image.DiskNames);
        Assert.Equal(["00.LFL", "01.LFL", "02.LFL", "Maniac", "rooms/00.LFL", "rooms/01.LFL", "rooms/02.LFL"], image.EnumerateFiles().Order(StringComparer.Ordinal));
        Assert.Equal(index, image.ReadAllBytes("00.lfl"));
        Assert.Equal(room1, image.ReadAllBytes("01.LFL"));
        Assert.Equal(room2, image.ReadAllBytes("rooms/02.LFL"));
        Assert.Throws<FileNotFoundException>(() => image.Open("03.LFL"));
    }

    /// <summary>A disk that is no AmigaDOS disk, one whose hash chain loops, or disks without the game's index are not taken for the game.</summary>
    [Fact]
    public void Other_disks_are_not_taken_for_the_game()
    {
        var noise = new byte[Blocks * BlockSize];
        new Random(1).NextBytes(noise);
        Assert.Null(AdfDiskImage.Open(new MemoryGameContainer("amiga").Add("noise.adf", noise)));

        var looping = NewDisk(fast: true, "Loop");
        looping.File(882, "00.LFL", Pattern(10, 1), firstData: 900);
        looping.Long(RootBlock, 6, 882);
        looping.Long(882, 124, 882);
        Assert.Null(AdfDiskImage.Open(new MemoryGameContainer("amiga").Add("loop.adf", looping.Bytes)));

        var other = NewDisk(fast: false, "Workbench");
        other.File(882, "README", Pattern(10, 1), firstData: 900);
        other.Long(RootBlock, 6, 882);
        Assert.Null(AdfDiskImage.Open(new MemoryGameContainer("amiga").Add("other.adf", other.Bytes)));
        Assert.NotNull(AdfDiskImage.FromDisks("other", [other.Bytes]));
    }

    /// <summary>
    /// The Amiga release's two disks: "Maniac Disk 1" with the index and nine rooms, "Maniac Disk 2"
    /// with 45, room 44 on both and the same; together the rooms 1 to 53, the index
    /// ScummVM's for the English Amiga release; the scripts and costumes reading whole but where
    /// DOS's 1988 release's do not either (script 134), and the sounds but where DOS's do not
    /// (1, 2, 47 to 49, 53) and sound 55, whose entry is two bytes before the next resource (the
    /// Amiga's ending plays the intro's song, 58, instead). Opened as the release.
    /// </summary>
    [Fact]
    public void The_amiga_disks_hold_the_release()
    {
        if (GameFolders.Find(WalkthroughTests.Amiga) is not { } path)
        {
            return;
        }

        var folder = new DirectoryGameContainer(path);
        var disks = AdfDiskImage.Open(folder)!;
        Assert.Equal(["Maniac Disk 1", "Maniac Disk 2"], disks.DiskNames);
        Assert.Equal(54, disks.EnumerateFiles().Count(f => f.StartsWith("rooms/", StringComparison.Ordinal) && f.EndsWith(".LFL", StringComparison.Ordinal)));
        Assert.All(Enumerable.Range(0, 54), room => Assert.True(disks.Exists(LflFile.NameOf(room))));
        var each = folder.EnumerateFiles().Where(f => f.EndsWith(".adf", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase)
            .Select(f => AdfDiskImage.FromDisks(f, [folder.ReadAllBytes(f)])!).ToList();
        Assert.Equal([10, 45], each.Select(d => d.EnumerateFiles().Count(f => f.StartsWith("rooms/", StringComparison.Ordinal) && f.EndsWith(".LFL", StringComparison.Ordinal))));
        Assert.Equal(each[0].ReadAllBytes("44.LFL"), each[1].ReadAllBytes("44.LFL"));

        var game = ScummDetector.Identify(folder);
        Assert.Equal("Maniac Mansion (V2, English, Amiga)", game?.Description);
        Assert.Equal("e781230da44a44e2f0770edb2b3b3633", game?.Entry.Md5);
        Assert.IsType<AdfDiskImage>(ResourceLoader.Open(folder).Game);

        var index = ResourceIndex.Parse(LflFile.Read(disks, 0));
        var loader = new ResourceLoader(disks, index);
        List<int> Unreadable(ResourceType type)
            => Enumerable.Range(0, index.Count(type)).Where(i => index.Locate(type, i) is { IsPresent: true, Room: > 0 } && !loader.IsReadable(type, i)).ToList();

        Assert.Equal([134], Unreadable(ResourceType.Script));
        Assert.Equal([1, 2, 47, 48, 49, 53, 55], Unreadable(ResourceType.Sound));
        Assert.Empty(Unreadable(ResourceType.Costume));
        Assert.All(Enumerable.Range(1, 53), room => Assert.True(loader.IsReadable(ResourceType.Room, room) && loader.LoadRoom(room) is not null, $"room {room}"));
    }
}
