using System.Buffers.Binary;

namespace Maniacal.Scumm.Rooms;

/// <summary>
/// A room of versions 1 and 2: its size, objects, walkboxes, entry and exit scripts, and the
/// sounds and scripts loaded with it. The graphics are read when the room is drawn. Mirrors
/// ScummVM's <c>ScummEngine_v3old::setupRoomSubBlocks</c>, <c>resetRoomSubBlocks</c> and
/// <c>resetRoomObjects</c> for <c>version &lt;= 2</c>.
/// </summary>
/// <remarks>
/// Header, from the room's size word: width and height at 4 (version 1: two bytes in 8-pixel
/// units; version 2: two words in pixels, and the picture offset at 10), the object count at 20, the walkbox offset at 21 (a
/// byte), the sound and script counts at 22 and 23, the exit and entry script offsets at 24 and
/// 26, and from 28 the object image offsets, the object code offsets, the sound numbers and the
/// script numbers.
/// </remarks>
public sealed class Room
{
    private const int HeaderSize = 28;

    private Room(int number, int version, ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        if (span.Length < HeaderSize)
        {
            throw new InvalidDataException($"Room {number} is too short for its header ({span.Length} bytes).");
        }

        Number = number;
        Version = version;
        Data = data;

        if (version <= 1)
        {
            Width = span[4] * 8;
            Height = span[5] * 8;
        }
        else
        {
            Width = Word(span, 4);
            Height = Word(span, 6);
            ImageOffset = Word(span, 0x0A);
        }

        int objectCount = span[20];
        int soundCount = span[22];
        int scriptCount = span[23];
        int lists = HeaderSize + objectCount * 4;
        if (lists + soundCount + scriptCount > span.Length)
        {
            throw new InvalidDataException($"Room {number}: the object, sound and script lists run past the end of the room.");
        }

        var objects = new RoomObject[objectCount];
        for (int i = 0; i < objectCount; i++)
        {
            int image = Word(span, HeaderSize + 2 * i);
            int code = Word(span, HeaderSize + 2 * (objectCount + i));
            objects[i] = RoomObject.Parse(data, image, code);
        }

        Objects = objects;
        Sounds = Numbers(span.Slice(lists, soundCount));
        Scripts = Numbers(span.Slice(lists + soundCount, scriptCount));

        int boxes = span[21];
        WalkBoxes = boxes == 0 ? WalkBoxes.Empty : WalkBoxes.Parse(span[boxes..]);

        // ScummVM sizes these scripts the same way: the exit script runs up to the entry script,
        // and the entry script to the end of the room
        int exit = Word(span, 24);
        int entry = Word(span, 26);
        ExitScript = exit == 0 ? ReadOnlyMemory<byte>.Empty : data[exit..(entry > exit ? entry : span.Length)];
        EntryScript = entry == 0 ? ReadOnlyMemory<byte>.Empty : data[entry..];
    }

    public int Number { get; }

    public int Version { get; }

    /// <summary>The room resource, from its size word.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>Width in pixels.</summary>
    public int Width { get; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; }

    /// <summary>Version 2: offset of the background picture and its mask (<c>_IM00_offs</c>). Version 1 keeps its graphics elsewhere and leaves this 0.</summary>
    public int ImageOffset { get; }

    public IReadOnlyList<RoomObject> Objects { get; }

    public WalkBoxes WalkBoxes { get; }

    /// <summary>Global sounds loaded with the room.</summary>
    public IReadOnlyList<int> Sounds { get; }

    /// <summary>Global scripts loaded with the room. Versions 1 and 2 have no room-local scripts.</summary>
    public IReadOnlyList<int> Scripts { get; }

    public ReadOnlyMemory<byte> EntryScript { get; }

    public ReadOnlyMemory<byte> ExitScript { get; }

    /// <summary>The object with a global number, or null when it is not in this room.</summary>
    public RoomObject? FindObject(int number) => Objects.FirstOrDefault(o => o.Number == number);

    public static Room Parse(int number, int version, ReadOnlyMemory<byte> data)
    {
        if (version is < 1 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "Only the version 1 and 2 room format is supported.");
        }

        return new Room(number, version, data);
    }

    private static int[] Numbers(ReadOnlySpan<byte> bytes)
    {
        var numbers = new int[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
        {
            numbers[i] = bytes[i];
        }

        return numbers;
    }

    private static int Word(ReadOnlySpan<byte> span, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(span[offset..]);
}
