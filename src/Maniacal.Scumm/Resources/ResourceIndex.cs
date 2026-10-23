using System.Buffers.Binary;

namespace Maniacal.Scumm.Resources;

/// <summary>The resource types the index locates. Mirrors ScummVM's <c>rtRoom</c>, <c>rtCostume</c>, <c>rtScript</c> and <c>rtSound</c>.</summary>
public enum ResourceType
{
    Room,
    Costume,
    Script,
    Sound,
}

/// <summary>
/// Where a resource lives: the room file that holds it and its offset in that file. Rooms
/// themselves always start at offset 0 of their own file.
/// </summary>
public readonly record struct ResourceLocation(int Room, int Offset)
{
    /// <summary>Offset value meaning "no resource". Mirrors ScummVM's <c>0xFFFF</c> check before <c>RES_INVALID_OFFSET</c>.</summary>
    public const int Invalid = 0xFFFF;

    public static readonly ResourceLocation None = new(0, Invalid);

    public bool IsPresent => Offset != Invalid;
}

/// <summary>
/// The index in room 0 (<c>00.LFL</c>) of the DOS releases: the initial owner and state of every
/// global object, and where every room, costume, script and sound is. Mirrors ScummVM's
/// <c>ScummEngine_v2::readIndexFile</c>, <c>readClassicIndexFile</c> (version 1) and
/// <c>readEnhancedIndexFile</c> (version 2).
/// </summary>
/// <remarks>
/// The index is taken at face value, as ScummVM takes it. Entry 0 of each type is never used, and
/// some entries point at no real resource: version 1's tables are a fixed size, larger than the game
/// needs, and a few entries in both versions are left over from resources that were removed.
/// </remarks>
public sealed class ResourceIndex
{
    /// <summary>First word of a version 1 index. Mirrors the <c>0x0A31</c> case of <c>readIndexFile</c>.</summary>
    public const ushort ClassicMagic = 0x0A31;

    /// <summary>First word of a version 2 index. Mirrors the <c>0x0100</c> case of <c>readIndexFile</c>.</summary>
    public const ushort EnhancedMagic = 0x0100;

    /// <summary>Mirrors ScummVM's <c>OF_OWNER_MASK</c>.</summary>
    private const byte OwnerMask = 0x0F;

    /// <summary>Mirrors ScummVM's <c>OF_STATE_SHL</c>.</summary>
    private const int StateShift = 4;

    // the fixed table sizes of the DOS version 1 index (readClassicIndexFile, the non-C64 Maniac Mansion branch)
    private const int ClassicGlobalObjects = 800;
    private const int ClassicRooms = 55;
    private const int ClassicCostumes = 35;
    private const int ClassicScripts = 200;
    private const int ClassicSounds = 100;

    private readonly ResourceLocation[][] _locations;

    private ResourceIndex(int version, byte[] owners, byte[] states, ResourceLocation[][] locations)
    {
        Version = version;
        ObjectOwners = owners;
        ObjectStates = states;
        _locations = locations;
    }

    /// <summary>The SCUMM version the index format belongs to: 1 or 2.</summary>
    public int Version { get; }

    /// <summary>The initial owner of each global object: a kid, or 0x0F for "in its room".</summary>
    public IReadOnlyList<byte> ObjectOwners { get; }

    /// <summary>The initial state bits of each global object.</summary>
    public IReadOnlyList<byte> ObjectStates { get; }

    public int GlobalObjectCount => ObjectOwners.Count;

    public IReadOnlyList<ResourceLocation> this[ResourceType type] => _locations[(int)type];

    public int Count(ResourceType type) => _locations[(int)type].Length;

    public ResourceLocation Locate(ResourceType type, int index)
    {
        var locations = _locations[(int)type];
        return index >= 0 && index < locations.Length ? locations[index] : ResourceLocation.None;
    }

    public static ResourceIndex Parse(ReadOnlySpan<byte> data)
    {
        var reader = new IndexReader(data);
        ushort magic = reader.Word();
        return magic switch
        {
            ClassicMagic => ParseClassic(ref reader),
            EnhancedMagic => ParseEnhanced(ref reader),
            _ => throw new InvalidDataException($"Unknown index magic 0x{magic:X4}; only the DOS releases of Maniac Mansion are supported."),
        };
    }

    /// <summary>Version 1: fixed table sizes, no counts in the file.</summary>
    private static ResourceIndex ParseClassic(ref IndexReader reader)
    {
        var (owners, states) = ReadGlobalObjects(ref reader, ClassicGlobalObjects);
        var locations = new[]
        {
            ReadResourceList(ref reader, ResourceType.Room, ClassicRooms),
            ReadResourceList(ref reader, ResourceType.Costume, ClassicCostumes),
            ReadResourceList(ref reader, ResourceType.Script, ClassicScripts),
            ReadResourceList(ref reader, ResourceType.Sound, ClassicSounds),
        };
        return new ResourceIndex(1, owners, states, locations);
    }

    /// <summary>Version 2: each table is preceded by its count. Mirrors <c>readGlobalObjects</c> and <c>ScummEngine_v3old::readResTypeList</c>.</summary>
    private static ResourceIndex ParseEnhanced(ref IndexReader reader)
    {
        var (owners, states) = ReadGlobalObjects(ref reader, reader.Word());
        var locations = new[]
        {
            ReadResourceList(ref reader, ResourceType.Room, reader.Byte()),
            ReadResourceList(ref reader, ResourceType.Costume, reader.Byte()),
            ReadResourceList(ref reader, ResourceType.Script, reader.Byte()),
            ReadResourceList(ref reader, ResourceType.Sound, reader.Byte()),
        };
        return new ResourceIndex(2, owners, states, locations);
    }

    private static (byte[] Owners, byte[] States) ReadGlobalObjects(ref IndexReader reader, int count)
    {
        var owners = new byte[count];
        var states = new byte[count];
        for (int i = 0; i < count; i++)
        {
            byte value = reader.Byte();
            owners[i] = (byte)(value & OwnerMask);
            states[i] = (byte)(value >> StateShift);
        }

        return (owners, states);
    }

    /// <summary>
    /// A list of room numbers, then a list of 16-bit offsets. For rooms the first list holds disk
    /// numbers, which the DOS releases ignore, and the offsets only mark which rooms exist: ScummVM
    /// resets them to 0 (<c>deleteRoomOffsets</c>, called from <c>closeRoom</c>) because each room
    /// starts its own file.
    /// </summary>
    private static ResourceLocation[] ReadResourceList(ref IndexReader reader, ResourceType type, int count)
    {
        var rooms = reader.Bytes(count);
        var locations = new ResourceLocation[count];
        for (int i = 0; i < count; i++)
        {
            int offset = reader.Word();
            locations[i] = offset == ResourceLocation.Invalid
                ? ResourceLocation.None
                : type == ResourceType.Room ? new ResourceLocation(i, 0) : new ResourceLocation(rooms[i], offset);
        }

        return locations;
    }

    private ref struct IndexReader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _position;

        public byte Byte() => Take(1)[0];

        public ushort Word() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));

        public ReadOnlySpan<byte> Bytes(int count) => Take(count);

        private ReadOnlySpan<byte> Take(int count)
        {
            if (_position + count > _data.Length)
            {
                throw new InvalidDataException($"The index ends early: {count} bytes wanted at offset {_position} of {_data.Length}.");
            }

            var slice = _data.Slice(_position, count);
            _position += count;
            return slice;
        }
    }
}
