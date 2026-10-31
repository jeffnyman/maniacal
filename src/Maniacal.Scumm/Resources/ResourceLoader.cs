using System.Buffers.Binary;
using Maniacal.Scumm.Costumes;
using Maniacal.Scumm.Rooms;
using Maniacal.Scumm.Storage;

namespace Maniacal.Scumm.Resources;

/// <summary>
/// Loads resources from the room files the index points to. In versions 1 and 2 every resource
/// starts with its own size as a 16-bit word, the size included; a room file is the room itself
/// followed by the costumes, scripts and sounds stored with it. Mirrors the <c>GF_OLD_BUNDLE</c>
/// path of ScummVM's <c>ScummEngine::loadResource</c>. Decoded room files are cached.
/// </summary>
public sealed class ResourceLoader
{
    private readonly IGameContainer _game;
    private readonly Dictionary<int, byte[]> _files = [];

    internal ResourceLoader(IGameContainer game, ResourceIndex index)
    {
        _game = game;
        Index = index;
    }

    internal ResourceIndex Index { get; }

    /// <summary>The files of the release.</summary>
    public IGameContainer Game => _game;

    /// <summary>
    /// Reads the index from <c>00.LFL</c> (on the C64, from the disk images) and returns a loader
    /// over it. Throws <see cref="FileNotFoundException"/> when the container has no such file and
    /// <see cref="InvalidDataException"/> when its index is not Maniac Mansion's or ends early.
    /// </summary>
    public static ResourceLoader Open(IGameContainer game)
    {
        game = C64DiskImage.Open(game) ?? game;
        return new(game, ResourceIndex.Parse(LflFile.Read(game, 0)));
    }

    /// <summary>The decoded contents of a room file.</summary>
    internal ReadOnlyMemory<byte> RoomFile(int room)
    {
        if (!_files.TryGetValue(room, out var data))
        {
            data = LflFile.Read(_game, room);
            _files[room] = data;
        }

        return data;
    }

    /// <summary>
    /// The bytes of a resource, starting with its size word, or null when the index has no entry
    /// for it or its room file is missing. Throws <see cref="InvalidDataException"/> when the entry
    /// points outside its file.
    /// </summary>
    internal ReadOnlyMemory<byte>? Load(ResourceType type, int index)
    {
        var location = Index.Locate(type, index);
        if (!location.IsPresent || !LflFile.Exists(_game, location.Room))
        {
            return null;
        }

        var file = RoomFile(location.Room);
        if (location.Offset + 2 > file.Length)
        {
            throw new InvalidDataException($"{type} {index}: offset {location.Offset} is past the end of {LflFile.NameOf(location.Room)} ({file.Length} bytes).");
        }

        int size = BinaryPrimitives.ReadUInt16LittleEndian(file.Span[location.Offset..]);
        if (size < 2 || location.Offset + size > file.Length)
        {
            throw new InvalidDataException($"{type} {index}: size {size} at offset {location.Offset} does not fit in {LflFile.NameOf(location.Room)} ({file.Length} bytes).");
        }

        return file.Slice(location.Offset, size);
    }

    /// <summary>Loads and parses a room, or returns null when the index has no such room.</summary>
    internal Room? LoadRoom(int room)
        => Load(ResourceType.Room, room) is { } data ? Room.Parse(room, Index.Version, data) : null;

    /// <summary>Loads and parses a costume, or returns null when the index has no such costume.</summary>
    internal Costume? LoadCostume(int costume)
        => Load(ResourceType.Costume, costume) is { } data ? Costume.Parse(costume, data) : null;

    /// <summary>Loads and parses a C64 costume, or returns null when the index has no such costume.</summary>
    internal V0Costume? LoadCostumeV0(int costume)
        => Load(ResourceType.Costume, costume) is { } data ? V0Costume.Parse(costume, data) : null;
}
