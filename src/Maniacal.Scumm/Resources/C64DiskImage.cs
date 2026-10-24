using System.Buffers.Binary;
using Daventry.Core.Storage;

namespace Maniacal.Scumm.Resources;

/// <summary>
/// The Commodore 64 release's two disk images, seen as the loose files of the DOS releases: the
/// index as <c>00.LFL</c> and each room with its costumes, scripts and sounds as <c>NN.LFL</c>,
/// encoded as those are. Mirrors <c>ScummDiskImage</c> for Maniac Mansion, which builds the same
/// files from the disks for the classic index reader and the resource loader.
/// </summary>
public sealed class C64DiskImage : IGameContainer
{
    /// <summary>The names ScummVM looks for: the first disk, the second, and the demo's one disk.</summary>
    public const string Disk1Name = "maniac1.d64", Disk2Name = "maniac2.d64", DemoDiskName = "maniacdemo.d64";

    /// <summary>The word a disk starts with: the index on the first disk, a room on the second.</summary>
    public const ushort Disk1Signature = 0x0A31, Disk2Signature = 0x0132;

    // the index tables' sizes on the C64 (readClassicIndexFile, version 0)
    private const int GlobalObjects = 256, Rooms = 55, Costumes = 25, Scripts = 160, Sounds = 70, DemoScripts = 55, DemoSounds = 40;
    private const int SectorSize = 256, DiskSize = 174848;

    // a resource's length word may be 0xFFFF, padding before the real one (extractResource)
    private const ushort Padding = 0xFFFF;

    private readonly byte[][] _disks;
    private readonly bool _demo;
    private readonly byte[] _roomDisks = new byte[Rooms], _roomTracks = new byte[Rooms], _roomSectors = new byte[Rooms];
    private readonly Dictionary<int, byte[]> _files = [];

    private C64DiskImage(string name, byte[][] disks, bool demo)
    {
        Name = name;
        _disks = disks;
        _demo = demo;
        _files[0] = ExtractIndex();
    }

    public string Name { get; }

    /// <summary>Whether this is the demo's single disk.</summary>
    public bool IsDemo => _demo;

    /// <summary>
    /// The disk images in a folder, or null when it has none. ScummVM's names come first; failing
    /// those, any two disk images whose first words are the two disks' signatures.
    /// </summary>
    public static C64DiskImage? Open(IGameContainer folder)
    {
        if (folder is C64DiskImage image)
        {
            return image;
        }

        string? Find(string name) => folder.EnumerateFiles().FirstOrDefault(f => f.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (Find(Disk1Name) is { } first && Find(Disk2Name) is { } second)
        {
            return Load(folder, first, second);
        }

        if (Find(DemoDiskName) is { } demo && Signature(folder, demo) == Disk1Signature)
        {
            return new C64DiskImage(folder.Name, [folder.ReadAllBytes(demo)], demo: true);
        }

        var images = folder.EnumerateFiles().Where(f => f.EndsWith(".d64", StringComparison.OrdinalIgnoreCase) && folder.GetLength(f) == DiskSize).ToList();
        var disk1 = images.Where(f => Signature(folder, f) == Disk1Signature).ToList();
        var disk2 = images.Where(f => Signature(folder, f) == Disk2Signature).ToList();
        return disk1.Count == 1 && disk2.Count == 1 ? Load(folder, disk1[0], disk2[0]) : null;
    }

    private static C64DiskImage? Load(IGameContainer folder, string first, string second)
    {
        var disk1 = folder.ReadAllBytes(first);
        var disk2 = folder.ReadAllBytes(second);
        if (disk1.Length < 2 || disk2.Length < 2
            || BinaryPrimitives.ReadUInt16LittleEndian(disk1) != Disk1Signature
            || BinaryPrimitives.ReadUInt16LittleEndian(disk2) != Disk2Signature)
        {
            return null;
        }

        return new C64DiskImage(folder.Name, [disk1, disk2], demo: false);
    }

    private static int Signature(IGameContainer folder, string name)
    {
        using var stream = folder.Open(name);
        Span<byte> word = stackalloc byte[2];
        return stream.Read(word) == 2 ? BinaryPrimitives.ReadUInt16LittleEndian(word) : -1;
    }

    public IEnumerable<string> EnumerateFiles()
    {
        yield return LflFile.NameOf(0);
        for (int room = 1; room < Rooms; room++)
        {
            if (RoomResources(room) > 0)
            {
                yield return LflFile.NameOf(room);
            }
        }
    }

    public bool Exists(string name) => Number(name) is { } room && (room == 0 || RoomResources(room) > 0);

    public long GetLength(string name) => File(name).Length;

    public Stream Open(string name) => new MemoryStream(File(name), writable: false);

    private byte[] File(string name)
    {
        if (Number(name) is not { } room || !Exists(name))
        {
            throw new FileNotFoundException($"{name} is not on the disks.", name);
        }

        if (!_files.TryGetValue(room, out var data))
        {
            data = ExtractRoom(room);
            _files[room] = data;
        }

        return data;
    }

    // "NN.LFL", as openSubFile reads the room number from the two characters before the dot
    private static int? Number(string name)
        => name.Length == 6 && name.EndsWith(".LFL", StringComparison.OrdinalIgnoreCase) && int.TryParse(name.AsSpan(0, 2), out int room) && room < Rooms
            ? room
            : null;

    private int RoomResources(int room) => (_demo ? C64DiskTables.DemoResourcesPerFile : C64DiskTables.ResourcesPerFile)[room];

    /// <summary>
    /// The index, as the classic reader expects it: the second disk's signature, the object flags,
    /// the rooms' disks and places (sector, then track), and the costume, script and sound lists.
    /// Mirrors <c>extractIndex</c>, which also keeps where each room is.
    /// </summary>
    private byte[] ExtractIndex()
    {
        var disk = new DiskReader(_disks[0], 2);
        var output = new List<byte>();
        void Byte(byte value) => output.Add((byte)(value ^ LflFile.EncodingKey));
        void Word(ushort value)
        {
            Byte((byte)value);
            Byte((byte)(value >> 8));
        }

        Word(Disk2Signature);
        for (int i = 0; i < GlobalObjects; i++)
        {
            Byte(disk.Byte());
        }

        for (int i = 0; i < Rooms; i++)
        {
            _roomDisks[i] = disk.Byte();
            Byte(_roomDisks[i]);
        }

        for (int i = 0; i < Rooms; i++)
        {
            _roomSectors[i] = disk.Byte();
            Byte(_roomSectors[i]);
            _roomTracks[i] = disk.Byte();
            Byte(_roomTracks[i]);
        }

        foreach (int count in new[] { Costumes, _demo ? DemoScripts : Scripts, _demo ? DemoSounds : Sounds })
        {
            for (int i = 0; i < count; i++)
            {
                Byte(disk.Byte());
            }

            for (int i = 0; i < count; i++)
            {
                Word(disk.Word());
            }
        }

        return [.. output];
    }

    /// <summary>
    /// A room file: the resources that start at the room's track and sector, one after another,
    /// each as long as its length word says. Mirrors <c>extractResource</c>.
    /// </summary>
    private byte[] ExtractRoom(int room)
    {
        // the index names the disk as a character, '1' or '2' (openDisk)
        int diskNumber = _roomDisks[room] is (byte)'1' or (byte)'2' ? _roomDisks[room] - '0' : _roomDisks[room];
        if (diskNumber < 1 || diskNumber > _disks.Length || _roomTracks[room] >= C64DiskTables.TrackStarts.Length)
        {
            throw new InvalidDataException($"Room {room} is on disk {diskNumber}, track {_roomTracks[room]}, which the disks do not have.");
        }

        var disk = new DiskReader(_disks[diskNumber - 1], (C64DiskTables.TrackStarts[_roomTracks[room]] + _roomSectors[room]) * SectorSize);
        var output = new List<byte>();
        for (int i = 0; i < RoomResources(room); i++)
        {
            ushort length;
            do
            {
                length = disk.Word();
                output.Add((byte)(length ^ LflFile.EncodingKey));
                output.Add((byte)((length >> 8) ^ LflFile.EncodingKey));
            }
            while (length == Padding);

            for (int left = length - 2; left > 0; left--)
            {
                output.Add((byte)(disk.Byte() ^ LflFile.EncodingKey));
            }
        }

        return [.. output];
    }

    private struct DiskReader(byte[] disk, int position)
    {
        private int _position = position;

        public byte Byte() => _position < disk.Length
            ? disk[_position++]
            : throw new InvalidDataException($"The disk ends at {disk.Length} bytes, before the resource does.");

        public ushort Word() => (ushort)(Byte() | Byte() << 8);
    }
}
