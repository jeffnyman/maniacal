using System.Buffers.Binary;
using Maniacal.Scumm.Costumes;
using Maniacal.Scumm.Detection;
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
    private readonly string _extension;

    internal ResourceLoader(IGameContainer game, ResourceIndex index, bool demo = false, string extension = LflFile.Extension)
    {
        _game = game;
        Index = index;
        IsDemo = demo;
        _extension = extension;
    }

    internal ResourceIndex Index { get; }

    /// <summary>Whether the release is a demo (<c>GF_DEMO</c>), which the engine treats a little differently.</summary>
    internal bool IsDemo { get; }

    /// <summary>The files of the release.</summary>
    public IGameContainer Game => _game;

    /// <summary>
    /// Reads the index from <c>00.LFL</c> (on the C64 and the NES, from the disk images or the
    /// cartridge) and returns a loader
    /// over it. Throws <see cref="NotSupportedException"/> for a release Maniacal knows but does
    /// not play (another language or platform; the message says which),
    /// <see cref="FileNotFoundException"/> when the container has no <c>00.LFL</c>, and
    /// <see cref="InvalidDataException"/> when its index is not Maniac Mansion's or ends early.
    /// Data that is no release ScummVM knows is opened as it is.
    /// </summary>
    public static ResourceLoader Open(IGameContainer game)
    {
        game = GameImages.Unpack(game);
        var found = ScummDetector.Identify(game);
        if (found is { Unsupported: { } reason })
        {
            throw new NotSupportedException($"This is {found.Description}, which Maniacal does not play: {reason}.");
        }

        string extension = found?.Variant == "V1 Demo" ? LflFile.DemoExtension : LflFile.Extension;
        return new(game, ResourceIndex.Parse(LflFile.Read(game, 0, extension)), found?.IsDemo ?? false, extension);
    }

    /// <summary>The decoded contents of a room file.</summary>
    internal ReadOnlyMemory<byte> RoomFile(int room)
    {
        if (!_files.TryGetValue(room, out var data))
        {
            data = LflFile.Read(_game, room, _extension);
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
        if (!location.IsPresent || !LflFile.Exists(_game, location.Room, _extension))
        {
            return null;
        }

        var file = RoomFile(location.Room);
        if (location.Offset + 2 > file.Length)
        {
            throw new InvalidDataException($"{type} {index}: offset {location.Offset} is past the end of {LflFile.NameOf(location.Room, _extension)} ({file.Length} bytes).");
        }

        int size = BinaryPrimitives.ReadUInt16LittleEndian(file.Span[location.Offset..]);
        if (size < 2 || location.Offset + size > file.Length)
        {
            throw new InvalidDataException($"{type} {index}: size {size} at offset {location.Offset} does not fit in {LflFile.NameOf(location.Room, _extension)} ({file.Length} bytes).");
        }

        return file.Slice(location.Offset, size);
    }

    /// <summary>
    /// Whether an index entry names a resource that can be read: one in a room file (an entry in
    /// room 0 is how the index keeps an empty slot, and to ScummVM's <c>loadResource</c> it means the
    /// room on screen, which these releases never ask for out of play) that fits in that file. The
    /// releases keep a few entries that are neither, numbers the game never loads.
    /// </summary>
    internal bool IsReadable(ResourceType type, int index)
    {
        var location = Index.Locate(type, index);
        if (!location.IsPresent || location.Room == 0 || !LflFile.Exists(_game, location.Room, _extension))
        {
            return false;
        }

        var file = RoomFile(location.Room);
        if (location.Offset + 2 > file.Length)
        {
            return false;
        }

        int size = BinaryPrimitives.ReadUInt16LittleEndian(file.Span[location.Offset..]);
        return size >= 2 && location.Offset + size <= file.Length;
    }

    /// <summary>Loads and parses a room, or returns null when the index has no such room.</summary>
    internal Room? LoadRoom(int room)
    {
        if (Load(ResourceType.Room, room) is not { } data)
        {
            return null;
        }

        var parsed = Room.Parse(room, Index.Version, data, Index.IsNes);
        if (Index.IsNes)
        {
            parsed.NesPatterns = Graphics.NesBitmap.Patterns(data.Span, n => Load(ResourceType.Costume, n));
        }

        return parsed;
    }

    /// <summary>Loads and parses a costume, or returns null when the index has no such costume.</summary>
    internal Costume? LoadCostume(int costume)
        => Load(ResourceType.Costume, costume) is { } data ? Costume.Parse(costume, data) : null;

    /// <summary>Loads and parses a C64 costume, or returns null when the index has no such costume.</summary>
    internal V0Costume? LoadCostumeV0(int costume)
        => Load(ResourceType.Costume, costume) is { } data ? V0Costume.Parse(costume, data) : null;
}
