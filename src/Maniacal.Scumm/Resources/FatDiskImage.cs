using System.Buffers.Binary;
using Maniacal.Scumm.Storage;

namespace Maniacal.Scumm.Resources;

/// <summary>
/// A floppy disk image in the FAT12 file system the Atari ST shares with DOS (a <c>.st</c> file,
/// the disk's sectors one after another), seen as the folder of files it holds: the Atari ST
/// release's <c>00.LFL</c> to <c>54.LFL</c> as they are on the disk, with any folder's files under
/// its name (<c>MAN_SOLV/MANIAC.SOL</c>). ScummVM reads the files once copied off the disk; this
/// reads them where they are.
/// </summary>
internal sealed class FatDiskImage : IGameContainer
{
    /// <summary>The image file's extension.</summary>
    public const string Extension = ".st";

    // the boot sector's parameter block (BPB): its fields' places
    private const int BytesPerSectorAt = 11, SectorsPerClusterAt = 13, ReservedSectorsAt = 14, FatsAt = 16,
        RootEntriesAt = 17, TotalSectorsAt = 19, SectorsPerFatAt = 22;

    // a directory entry: 32 bytes, its name, extension, attributes, first cluster and size
    private const int EntrySize = 32, AttributesAt = 11, FirstClusterAt = 26, SizeAt = 28;
    private const byte Deleted = 0xE5, VolumeLabel = 0x08, Directory = 0x10, LongName = 0x0F;

    // a FAT12 cluster number at or past this ends a chain; below 2 there is none
    private const int EndOfChain = 0xFF0;

    private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);

    private FatDiskImage(string name) => Name = name;

    public string Name { get; }

    /// <summary>
    /// The game disk in a folder, or null when it has none: the one <c>.st</c> image whose file
    /// system can be read and holds an <c>00.LFL</c>.
    /// </summary>
    public static FatDiskImage? Open(IGameContainer folder)
    {
        if (folder is FatDiskImage image)
        {
            return image;
        }

        var found = folder.EnumerateFiles()
            .Where(f => f.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
            .Select(f => FromBytes(folder.Name, folder.ReadAllBytes(f)))
            .Where(d => d is not null && d.Exists(LflFile.NameOf(0)))
            .ToList();
        return found.Count == 1 ? found[0] : null;
    }

    /// <summary>The files of a disk image's bytes, or null when they hold no FAT12 file system that can be read.</summary>
    internal static FatDiskImage? FromBytes(string name, byte[] disk)
    {
        if (disk.Length < 512)
        {
            return null;
        }

        int bytesPerSector = BinaryPrimitives.ReadUInt16LittleEndian(disk.AsSpan(BytesPerSectorAt));
        int sectorsPerCluster = disk[SectorsPerClusterAt];
        int reserved = BinaryPrimitives.ReadUInt16LittleEndian(disk.AsSpan(ReservedSectorsAt));
        int fats = disk[FatsAt];
        int rootEntries = BinaryPrimitives.ReadUInt16LittleEndian(disk.AsSpan(RootEntriesAt));
        int totalSectors = BinaryPrimitives.ReadUInt16LittleEndian(disk.AsSpan(TotalSectorsAt));
        int sectorsPerFat = BinaryPrimitives.ReadUInt16LittleEndian(disk.AsSpan(SectorsPerFatAt));
        if (bytesPerSector is not (512 or 1024 or 2048 or 4096) || sectorsPerCluster is 0 or > 64 || reserved == 0
            || fats is 0 or > 2 || rootEntries == 0 || sectorsPerFat == 0
            || (long)totalSectors * bytesPerSector > disk.Length)
        {
            return null;
        }

        var layout = new Layout(disk, bytesPerSector, sectorsPerCluster, reserved * bytesPerSector, sectorsPerFat * bytesPerSector,
            (reserved + fats * sectorsPerFat) * bytesPerSector, rootEntries);
        if (layout.Data > disk.Length)
        {
            return null;
        }

        var image = new FatDiskImage(name);
        try
        {
            image.ReadDirectory(layout, disk.AsSpan(layout.Root, rootEntries * EntrySize).ToArray(), string.Empty, depth: 0);
        }
        catch (InvalidDataException)
        {
            return null;
        }

        return image;
    }

    private readonly record struct Layout(byte[] Disk, int BytesPerSector, int SectorsPerCluster, int Fat, int FatLength, int Root, int RootEntries)
    {
        public int Data => Root + RootEntries * EntrySize;

        public int ClusterSize => BytesPerSector * SectorsPerCluster;

        // the next cluster of a chain: twelve bits, three bytes holding two
        public int Next(int cluster)
        {
            int at = Fat + cluster * 3 / 2;
            if (at + 1 >= Fat + FatLength)
            {
                throw new InvalidDataException("A cluster chain runs past the file allocation table.");
            }

            int pair = Disk[at] | (Disk[at + 1] << 8);
            return (cluster & 1) != 0 ? pair >> 4 : pair & 0xFFF;
        }

        // a chain's bytes, up to a length when one is given
        public byte[] Read(int cluster, int? length)
        {
            var output = new List<byte>(length ?? ClusterSize);
            var seen = new HashSet<int>();
            while (cluster >= 2 && cluster < EndOfChain && (length is null || output.Count < length))
            {
                long at = Data + (long)(cluster - 2) * ClusterSize;
                if (!seen.Add(cluster) || at + ClusterSize > Disk.Length)
                {
                    throw new InvalidDataException("A cluster chain loops or runs past the disk.");
                }

                output.AddRange(Disk.AsSpan((int)at, ClusterSize));
                cluster = Next(cluster);
            }

            if (length is { } wanted)
            {
                if (output.Count < wanted)
                {
                    throw new InvalidDataException("A file is longer than its clusters.");
                }

                return [.. output.Take(wanted)];
            }

            return [.. output];
        }
    }

    // a directory's entries: files under their names, folders' files under theirs
    private void ReadDirectory(Layout layout, byte[] entries, string path, int depth)
    {
        for (int at = 0; at + EntrySize <= entries.Length; at += EntrySize)
        {
            var entry = entries.AsSpan(at, EntrySize);
            if (entry[0] == 0)
            {
                return;
            }

            byte attributes = entry[AttributesAt];
            if (entry[0] == Deleted || attributes == LongName || (attributes & VolumeLabel) != 0)
            {
                continue;
            }

            string baseName = Latin1(entry[..8]), extension = Latin1(entry[8..11]);
            if (baseName is "." or "..")
            {
                continue;
            }

            string name = path + baseName + (extension.Length > 0 ? "." + extension : string.Empty);
            int cluster = BinaryPrimitives.ReadUInt16LittleEndian(entry[FirstClusterAt..]);
            if ((attributes & Directory) != 0)
            {
                if (depth < 8)
                {
                    ReadDirectory(layout, layout.Read(cluster, null), name + "/", depth + 1);
                }
            }
            else
            {
                _files[name] = layout.Read(cluster, (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[SizeAt..]));
            }
        }
    }

    private static string Latin1(ReadOnlySpan<byte> bytes) => System.Text.Encoding.Latin1.GetString(bytes).TrimEnd(' ');

    public IEnumerable<string> EnumerateFiles() => _files.Keys;

    public bool Exists(string name) => _files.ContainsKey(name);

    public long GetLength(string name) => File(name).Length;

    public Stream Open(string name) => new MemoryStream(File(name), writable: false);

    private byte[] File(string name)
        => _files.TryGetValue(name, out var data) ? data : throw new FileNotFoundException($"{name} is not on the disk {Name}.", name);
}
