using System.Buffers.Binary;
using Maniacal.Scumm.Storage;

namespace Maniacal.Scumm.Resources;

/// <summary>
/// The Amiga release's floppy disks, images of AmigaDOS disks (<c>.adf</c> files, the disk's
/// 512-byte blocks one after another, in the original or the fast file system), seen together as
/// the folder of files they hold: the game's two disks give <c>00.LFL</c> to <c>53.LFL</c> between
/// them, under a <c>rooms</c> folder, and each room file is found by its own name too, as
/// ScummVM's search path finds it there (<c>SearchMan.addSubDirectoryMatching(gameDataDir,
/// "rooms")</c>). ScummVM reads the files once copied off the disks; this reads them where they are.
/// </summary>
internal sealed class AdfDiskImage : IGameContainer
{
    /// <summary>The image files' extension.</summary>
    public const string Extension = ".adf";

    /// <summary>The folder the game's room files are in on its disks.</summary>
    public const string RoomsFolder = "rooms";

    private const int BlockSize = 512;

    // a block's longs: its type, the hash table's slots, a file's size, the next in a hash chain,
    // the parent, the extension block, the secondary type; the name's length and characters
    private const int TypeAt = 0, HighSeqAt = 2, FirstSlot = 6, SlotCount = 72, FileSizeAt = 81, HashChainAt = 124,
        ExtensionAt = 126, SecondaryTypeAt = 127, NameAt = BlockSize - 80;

    // block types: a header (the root, a directory, a file) and a file's list of data blocks
    private const int Header = 2, List = 16, Root = 1, Directory = 2, File = -3;

    // a data block in the original file system: its own header, then 488 bytes of the file
    private const int OfsDataHeader = 24, OfsDataSizeAt = 3;

    private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);

    private AdfDiskImage(string name) => Name = name;

    public string Name { get; }

    /// <summary>The names the disks gave, in the order read: "Maniac Disk 1", "Maniac Disk 2".</summary>
    public List<string> DiskNames { get; } = [];

    /// <summary>
    /// The game's disks in a folder, or null when it has none: every <c>.adf</c> image whose file
    /// system can be read, together, when one of them holds an <c>00.LFL</c>.
    /// </summary>
    public static AdfDiskImage? Open(IGameContainer folder)
    {
        if (folder is AdfDiskImage image)
        {
            return image;
        }

        var disks = folder.EnumerateFiles()
            .Where(f => f.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(f => folder.ReadAllBytes(f))
            .ToList();
        var combined = FromDisks(folder.Name, disks);
        return combined is not null && combined.Exists(LflFile.NameOf(0)) ? combined : null;
    }

    /// <summary>
    /// The files of disk images' bytes, together, or null when none holds an AmigaDOS file system
    /// that can be read; a disk that cannot be read is left out. A file on two disks is taken
    /// from the first.
    /// </summary>
    internal static AdfDiskImage? FromDisks(string name, IEnumerable<byte[]> disks)
    {
        var image = new AdfDiskImage(name);
        foreach (var disk in disks)
        {
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            string? diskName;
            try
            {
                diskName = Read(disk, files);
            }
            catch (InvalidDataException)
            {
                continue;
            }

            if (diskName is null)
            {
                continue;
            }

            image.DiskNames.Add(diskName);
            foreach (var (path, data) in files)
            {
                image._files.TryAdd(path, data);

                // the room files, found by their own names too, as ScummVM's search path finds them
                if (path.StartsWith(RoomsFolder + "/", StringComparison.OrdinalIgnoreCase) && path.IndexOf('/', RoomsFolder.Length + 1) < 0)
                {
                    image._files.TryAdd(path[(RoomsFolder.Length + 1)..], data);
                }
            }
        }

        return image.DiskNames.Count > 0 ? image : null;
    }

    // a disk's files into the dictionary, by their paths; the disk's name, or null when it is no
    // AmigaDOS disk
    private static string? Read(byte[] disk, Dictionary<string, byte[]> files)
    {
        if (disk.Length < 4 * BlockSize || disk.Length % BlockSize != 0 || !disk.AsSpan(0, 3).SequenceEqual("DOS"u8))
        {
            return null;
        }

        bool fast = (disk[3] & 1) != 0;
        int blocks = disk.Length / BlockSize;
        var root = Block(disk, blocks / 2);
        if (Long(root, TypeAt) != Header || (int)Long(root, SecondaryTypeAt) != Root)
        {
            return null;
        }

        var visited = new HashSet<int>();
        ReadDirectory(disk, root, string.Empty, fast, files, visited, depth: 0);
        return BlockName(root);
    }

    private static void ReadDirectory(byte[] disk, ReadOnlySpan<byte> directory, string path, bool fast, Dictionary<string, byte[]> files, HashSet<int> visited, int depth)
    {
        for (int slot = FirstSlot; slot < FirstSlot + SlotCount; slot++)
        {
            for (int number = (int)Long(directory, slot); number != 0;)
            {
                if (!visited.Add(number))
                {
                    throw new InvalidDataException("A directory's hash chain loops.");
                }

                var header = Block(disk, number);
                if (Long(header, TypeAt) != Header)
                {
                    throw new InvalidDataException($"Block {number} is no header.");
                }

                string name = path + BlockName(header);
                switch ((int)Long(header, SecondaryTypeAt))
                {
                    case Directory when depth < 8:
                        ReadDirectory(disk, header, name + "/", fast, files, visited, depth + 1);
                        break;
                    case File:
                        files[name] = ReadFile(disk, header, fast, visited);
                        break;
                }

                number = (int)Long(header, HashChainAt);
            }
        }
    }

    // a file's bytes: its data blocks, listed in its header and its extension blocks, the list
    // filled from its end
    private static byte[] ReadFile(byte[] disk, ReadOnlySpan<byte> header, bool fast, HashSet<int> visited)
    {
        long size = Long(header, FileSizeAt);
        if (size > disk.Length)
        {
            throw new InvalidDataException("A file is longer than its disk.");
        }

        var output = new byte[size];
        int written = 0;
        var list = header;
        while (true)
        {
            int count = (int)Long(list, HighSeqAt);
            if (count > SlotCount)
            {
                throw new InvalidDataException("A file's block list is longer than a block holds.");
            }

            for (int i = 0; i < count && written < size; i++)
            {
                var data = Block(disk, (int)Long(list, FirstSlot + SlotCount - 1 - i));
                var part = fast ? data : data.Slice(OfsDataHeader, Math.Min((int)Long(data, OfsDataSizeAt), BlockSize - OfsDataHeader));
                part = part[..(int)Math.Min(part.Length, size - written)];
                part.CopyTo(output.AsSpan(written));
                written += part.Length;
            }

            int extension = (int)Long(list, ExtensionAt);
            if (extension == 0 || written >= size)
            {
                break;
            }

            if (!visited.Add(extension))
            {
                throw new InvalidDataException("A file's block lists loop.");
            }

            list = Block(disk, extension);
            if (Long(list, TypeAt) != List)
            {
                throw new InvalidDataException($"Block {extension} is no block list.");
            }
        }

        if (written < size)
        {
            throw new InvalidDataException("A file is longer than its blocks.");
        }

        return output;
    }

    private static ReadOnlySpan<byte> Block(byte[] disk, int number)
        => number > 0 && (long)(number + 1) * BlockSize <= disk.Length
            ? disk.AsSpan(number * BlockSize, BlockSize)
            : throw new InvalidDataException($"Block {number} is past the disk's end.");

    private static uint Long(ReadOnlySpan<byte> block, int index) => BinaryPrimitives.ReadUInt32BigEndian(block[(index * 4)..]);

    private static string BlockName(ReadOnlySpan<byte> block)
        => System.Text.Encoding.Latin1.GetString(block.Slice(NameAt + 1, Math.Min(block[NameAt], (byte)30)));

    public IEnumerable<string> EnumerateFiles() => _files.Keys;

    public bool Exists(string name) => _files.ContainsKey(name);

    public long GetLength(string name) => FileBytes(name).Length;

    public Stream Open(string name) => new MemoryStream(FileBytes(name), writable: false);

    private byte[] FileBytes(string name)
        => _files.TryGetValue(name, out var data) ? data : throw new FileNotFoundException($"{name} is not on the disks {Name}.", name);
}
