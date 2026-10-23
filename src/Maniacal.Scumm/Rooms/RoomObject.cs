using System.Text;

namespace Maniacal.Scumm.Rooms;

/// <summary>One entry of an object's verb table: the verb and where its script starts in the object's code block.</summary>
public readonly record struct VerbEntry(byte Verb, int Offset);

/// <summary>
/// An object as its room defines it: the object code block (OBCD) of versions 1 and 2. Positions
/// and sizes are in pixels, as ScummVM's <c>ObjectData</c> keeps them. Mirrors
/// <c>ScummEngine_v4::resetRoomObject</c> with its <c>GF_OLD_BUNDLE</c> and <c>version &lt;= 2</c>
/// branches.
/// </summary>
/// <remarks>
/// The code block starts with its size word. Byte 4 holds the object number, bytes 7 to 13 the
/// geometry, byte 14 the offset of the name and byte 15 the start of the verb table: pairs of verb
/// and offset, ended by a 0 verb.
/// </remarks>
public sealed class RoomObject
{
    /// <summary>The verb-table entry that matches any verb. Mirrors <c>kFallbackEntry</c> in <c>getVerbEntrypoint</c>.</summary>
    public const byte FallbackVerb = 0xFF;

    private RoomObject(ReadOnlyMemory<byte> code, int imageOffset, int codeOffset)
    {
        var block = code.Span;
        Code = code;
        ImageOffset = imageOffset;
        CodeOffset = codeOffset;
        Number = block[4] | (block[5] << 8);
        X = block[7] * 8;
        Y = (block[8] & 0x7F) * 8;
        ParentState = (block[8] & 0x80) != 0 ? 8 : 0;
        Width = block[9] * 8;
        Parent = block[10];
        WalkX = block[11] * 8;
        WalkY = (block[12] & 0x1F) * 8;
        Preposition = block[12] >> 5;
        ActorDir = block[13] & 0x07;
        Height = block[13] & 0xF8;
        Name = ReadName(block, block[14]);
        Verbs = ReadVerbs(block);
    }

    /// <summary>The global object number.</summary>
    public int Number { get; }

    /// <summary>Offset of the object's image in the room (OBIM), used when the object draws itself.</summary>
    public int ImageOffset { get; }

    /// <summary>Offset of the object's code block in the room (OBCD).</summary>
    public int CodeOffset { get; }

    /// <summary>The whole code block, from its size word; verb offsets are relative to its start.</summary>
    public ReadOnlyMemory<byte> Code { get; }

    public int X { get; }

    public int Y { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>1-based index in the room's object list of the object this one depends on, or 0.</summary>
    public int Parent { get; }

    /// <summary>The state the parent must be in for this object to be visible: 0 or 8.</summary>
    public int ParentState { get; }

    public int WalkX { get; }

    public int WalkY { get; }

    /// <summary>The preposition "Use" takes with this object. Mirrors <c>o2_getObjPreposition</c>.</summary>
    public int Preposition { get; }

    /// <summary>The direction the actor faces to use the object (the low 3 bits of byte 13).</summary>
    public int ActorDir { get; }

    public string Name { get; }

    public IReadOnlyList<VerbEntry> Verbs { get; }

    /// <summary>
    /// The offset of the script that handles a verb, or null when the object has none. The fallback
    /// entry matches any verb. Mirrors the version 1 and 2 branch of <c>getVerbEntrypoint</c>.
    /// </summary>
    public int? FindVerb(byte verb)
    {
        foreach (var entry in Verbs)
        {
            if (entry.Verb == verb || entry.Verb == FallbackVerb)
            {
                return entry.Offset;
            }
        }

        return null;
    }

    internal static RoomObject Parse(ReadOnlyMemory<byte> room, int imageOffset, int codeOffset)
    {
        var span = room.Span;
        if (codeOffset + 16 > span.Length)
        {
            throw new InvalidDataException($"Object code block at {codeOffset} is past the end of the room ({span.Length} bytes).");
        }

        int size = span[codeOffset] | (span[codeOffset + 1] << 8);
        if (size < 16 || codeOffset + size > span.Length)
        {
            throw new InvalidDataException($"Object code block at {codeOffset} has a bad size {size}.");
        }

        return new RoomObject(room.Slice(codeOffset, size), imageOffset, codeOffset);
    }

    private static string ReadName(ReadOnlySpan<byte> block, int offset)
    {
        if (offset == 0 || offset >= block.Length)
        {
            return string.Empty;
        }

        int end = block[offset..].IndexOf((byte)0);
        var name = end < 0 ? block[offset..] : block.Slice(offset, end);
        return Encoding.Latin1.GetString(name);
    }

    private static VerbEntry[] ReadVerbs(ReadOnlySpan<byte> block)
    {
        var verbs = new List<VerbEntry>();
        for (int i = 15; i + 1 < block.Length && block[i] != 0; i += 2)
        {
            verbs.Add(new VerbEntry(block[i], block[i + 1]));
        }

        return [.. verbs];
    }
}
