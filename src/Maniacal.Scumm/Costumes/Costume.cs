using System.Buffers.Binary;

namespace Maniacal.Scumm.Costumes;

/// <summary>What an animation does to one limb: play a run of commands, or hide the limb.</summary>
/// <param name="Limb">The limb, 0 being drawn first.</param>
/// <param name="Start">Index of the first command, or null when the animation hides the limb.</param>
/// <param name="Length">How many commands follow the first one.</param>
/// <param name="Loop">Whether the run starts again after its last command.</param>
internal readonly record struct LimbAnimation(int Limb, int? Start, int Length, bool Loop);

/// <summary>A picture of one limb, and how it is placed. Offsets are in pixels.</summary>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="RelX">Where the cel's left edge is, from the running position.</param>
/// <param name="RelY">Where the cel's top edge is, from the running position.</param>
/// <param name="MoveX">How far the running position moves right for the next limb.</param>
/// <param name="MoveY">How far the running position moves up for the next limb.</param>
/// <param name="DataOffset">Where the cel's pixel data starts in the costume.</param>
internal readonly record struct CostumeCel(int Width, int Height, int RelX, int RelY, int MoveX, int MoveY, int DataOffset);

/// <summary>
/// A costume of versions 1 and 2: a character's animations. Mirrors ScummVM's
/// <c>ClassicCostumeLoader::loadCostume</c> and <c>costumeDecodeData</c> for <c>GF_OLD_BUNDLE</c>,
/// and the cel headers read in <c>ClassicCostumeRenderer::drawLimb</c>.
/// </summary>
/// <remarks>
/// <para>From the size word: the number of animations at 4 and the format at 5 (bit 7: the costume
/// has its own left-facing pictures and is never mirrored). Format <c>0x57</c> is version 1's, kept
/// from the C64: 8 limbs, cels in 8-pixel columns of 2-bit pixels. Formats <c>0x58</c> and
/// <c>0x59</c> are version 2's: 16 limbs, cels in 1-pixel columns of 4-bit pixels, with one colour
/// byte at 6.</para>
/// <para>Then the offset of the command bytes, one frame-table offset per limb and one offset per
/// animation; offsets count from a base 4 bytes in (version 1) or at the size word (version 2).
/// Animation <c>frame * 4 + direction</c> lists the limbs it changes. Each command picks a cel from
/// the limb's frame table; commands <see cref="Hide"/>, <see cref="Stop"/> and <see cref="Start"/>
/// are not cels.</para>
/// </remarks>
internal sealed class Costume
{
    public const int FormatV1 = 0x57;

    /// <summary>The limb shows nothing for this command. Mirrors the <c>0x7B</c> check in <c>drawLimb</c>.</summary>
    public const byte Hide = 0x7B;

    /// <summary>The limb stops animating. Mirrors the <c>0x79</c> check in <c>costumeDecodeData</c>.</summary>
    public const byte Stop = 0x79;

    /// <summary>The limb starts animating again. Mirrors the <c>0x7A</c> check in <c>costumeDecodeData</c>.</summary>
    public const byte Start = 0x7A;

    /// <summary>Plays the actor's sound; skipped when stepping. Mirrors the <c>0x78</c> check in <c>increaseAnim</c>.</summary>
    public const byte Sound = 0x78;

    /// <summary>Counts an animation step; skipped when stepping. Mirrors the <c>0x7C</c> check in <c>increaseAnim</c>.</summary>
    public const byte Counter = 0x7C;

    private readonly int _base;
    private readonly int _frameOffsets;
    private readonly int _animationOffsets;
    private readonly int _commands;

    private Costume(int number, ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        if (span.Length < 10)
        {
            throw new InvalidDataException($"Costume {number} is too short ({span.Length} bytes).");
        }

        Number = number;
        Data = data;
        AnimationCount = span[4] + 1;
        Format = span[5] & 0x7F;
        HasOwnLeftPictures = (span[5] & 0x80) != 0;

        if (Format == FormatV1)
        {
            LimbCount = 8;
            _base = 4;
            _commands = _base + Word(span, 6);
            _frameOffsets = 8;
            _animationOffsets = 24;
        }
        else if (Format is 0x58 or 0x59)
        {
            LimbCount = 16;
            ColorIndex = span[6];
            _base = 0;
            _commands = _base + Word(span, 7);
            _frameOffsets = 9;
            _animationOffsets = 41;
        }
        else
        {
            throw new InvalidDataException($"Costume {number} has format 0x{Format:X2}, which is not a version 1 or 2 costume.");
        }

        if (_animationOffsets + 2 * AnimationCount > span.Length || _commands > span.Length)
        {
            throw new InvalidDataException($"Costume {number}: its tables run past the end of the costume.");
        }

        // the command bytes run up to the first limb frame table
        int firstFrameTable = span.Length;
        for (int limb = 0; limb < LimbCount; limb++)
        {
            int table = _base + Word(span, _frameOffsets + 2 * limb);
            if (table >= _commands && table < firstFrameTable)
            {
                firstFrameTable = table;
            }
        }

        CommandCount = firstFrameTable - _commands;
    }

    public int Number { get; }

    /// <summary>The costume resource, from its size word.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    public int Format { get; }

    public bool IsVersion1 => Format == FormatV1;

    /// <summary>Whether the costume has its own left-facing pictures; otherwise facing left mirrors facing right.</summary>
    public bool HasOwnLeftPictures { get; }

    public int AnimationCount { get; }

    /// <summary>
    /// How many command bytes there are, up to the first limb frame table. A few animations have
    /// runs that reach past them (V2 costume 23, animation 30); the bytes there are not commands.
    /// </summary>
    public int CommandCount { get; }

    public int LimbCount { get; }

    /// <summary>Version 2: the colour number drawn as black instead of itself. Mirrors <c>_palette[_loaded._palette[0]] = _palette[0]</c> in <c>setPalette</c>.</summary>
    public int ColorIndex { get; }

    public static Costume Parse(int number, ReadOnlyMemory<byte> data) => new(number, data);

    /// <summary>The command byte at an index; the low 7 bits pick a cel.</summary>
    public byte Command(int index)
    {
        var span = Data.Span;
        if (_commands + index >= span.Length)
        {
            throw new InvalidDataException($"Costume {Number}: command {index} is past the end of the costume.");
        }

        return span[_commands + index];
    }

    /// <summary>The limbs an animation changes, or an empty list when the costume has no such animation.</summary>
    internal IReadOnlyList<LimbAnimation> Animation(int index)
    {
        var span = Data.Span;
        if (index < 0 || index >= AnimationCount)
        {
            return [];
        }

        int position = _base + Word(span, _animationOffsets + 2 * index);
        if (position == _base)
        {
            return [];
        }

        int mask;
        if (IsVersion1)
        {
            mask = span[position++] << 8;
        }
        else
        {
            mask = Word(span, position);
            position += 2;
        }

        var limbs = new List<LimbAnimation>();
        for (int limb = 0; (mask & 0xFFFF) != 0; limb++, mask <<= 1)
        {
            if ((mask & 0x8000) == 0)
            {
                continue;
            }

            byte start = span[position++];
            if (start == 0xFF)
            {
                limbs.Add(new LimbAnimation(limb, null, 0, false));
                continue;
            }

            byte extra = span[position++];
            limbs.Add(new LimbAnimation(limb, start, extra & 0x7F, (extra & 0x80) == 0));
        }

        return limbs;
    }

    /// <summary>The cel a command picks for a limb.</summary>
    internal CostumeCel Cel(int limb, int code)
    {
        var span = Data.Span;
        int frames = _base + Word(span, _frameOffsets + 2 * limb);
        int cel = _base + Word(span, frames + 2 * code);
        if (cel + (IsVersion1 ? 6 : 12) > span.Length)
        {
            throw new InvalidDataException($"Costume {Number}: the cel of limb {limb}, command {code}, is past the end of the costume.");
        }

        if (IsVersion1)
        {
            return new CostumeCel(
                span[cel] * 8,
                span[cel + 1],
                (sbyte)span[cel + 2] * 8,
                -(sbyte)span[cel + 3],
                (sbyte)span[cel + 4] * 8,
                (sbyte)span[cel + 5],
                cel + 6);
        }

        return new CostumeCel(
            Word(span, cel),
            Word(span, cel + 2),
            (short)Word(span, cel + 4),
            (short)Word(span, cel + 6),
            (short)Word(span, cel + 8),
            (short)Word(span, cel + 10),
            cel + 12);
    }

    private static int Word(ReadOnlySpan<byte> span, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(span[offset..]);
}
