using System.Buffers.Binary;

namespace Maniacal.Scumm.Costumes;

/// <summary>A picture of one of a C64 costume's limbs, and how it is placed.</summary>
/// <param name="Columns">Width in 8-pixel columns, a byte each per line.</param>
/// <param name="Height">Height in lines.</param>
/// <param name="OffsetX">Columns from the running position to the cel.</param>
/// <param name="OffsetY">Lines from the running position up to the cel's top.</param>
/// <param name="MoveX">How many columns the running position moves for the next limb.</param>
/// <param name="MoveY">How many lines the running position moves up for the next limb.</param>
/// <param name="DataOffset">Where the cel's pixel bytes start in the costume.</param>
public readonly record struct V0Cel(int Columns, int Height, int OffsetX, int OffsetY, int MoveX, int MoveY, int DataOffset);

/// <summary>
/// A costume of version 0, the C64: 8 limbs, each with its sequences of cels, and clips that each
/// start one sequence on each limb, mirrored or not. Mirrors ScummVM's <c>V0CostumeLoader::loadCostume</c>
/// and what <c>V0CostumeRenderer::drawLimb</c> reads.
/// </summary>
/// <remarks>
/// From the size word: at 4 where the cels' high bytes are, at 5 the sequences and at 7 the clips,
/// offsets from a base 9 bytes in. The sequences table starts with each limb's list of where its
/// sequences start; a sequence is a run of frames ended by 0xFF, a frame the place in the costume of
/// its cel's number, which picks the cel's low and high offset bytes. A clip is 8 bytes, one a
/// limb: the sequence, with bit 7 set to mirror it, or 0xFF to leave the limb as it is.
/// </remarks>
public sealed class V0Costume
{
    public const int Limbs = 8;

    /// <summary>The end of a sequence, and a clip's limb left as it is.</summary>
    public const byte End = 0xFF;

    private const int Base = 9;

    private readonly int _cels;
    private readonly int _sequences;
    private readonly int _clips;

    private V0Costume(int number, ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        if (span.Length < Base + 2)
        {
            throw new InvalidDataException($"Costume {number} is too short ({span.Length} bytes).");
        }

        Number = number;
        Data = data;
        _cels = span[4];
        _sequences = Base + BinaryPrimitives.ReadUInt16LittleEndian(span[5..]);
        _clips = Base + BinaryPrimitives.ReadUInt16LittleEndian(span[7..]);
        if (_sequences + Limbs > span.Length || _clips > span.Length)
        {
            throw new InvalidDataException($"Costume {number}: its tables run past the end of the costume.");
        }
    }

    public int Number { get; }

    /// <summary>The costume resource, from its size word.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>How many 8-byte clips fit before the costume ends.</summary>
    public int ClipCount => (Data.Length - _clips) / Limbs;

    public static V0Costume Parse(int number, ReadOnlyMemory<byte> data) => new(number, data);

    /// <summary>What a clip does to a limb: its sequence, bit 7 to mirror it, or <see cref="End"/>. Mirrors <c>(_animCmds + cmd)[limb]</c>.</summary>
    public byte Clip(int clip, int limb) => At(_clips + clip * Limbs + limb, "clip");

    /// <summary>A byte of the sequences table: a limb's list of starts, or a frame. Mirrors <c>_frameOffsets[index]</c>.</summary>
    public byte Sequence(int index) => At(_sequences + index, "sequence");

    /// <summary>The cel a frame shows. Mirrors the frame pointer and header of <c>V0CostumeRenderer::drawLimb</c>.</summary>
    public V0Cel Cel(byte frame)
    {
        int low = At(Base + frame, "cel number");
        int high = low + _cels;
        int cel = Base + (At(Base + high, "cel offset") << 8 | At(Base + low + 2, "cel offset"));
        var span = Data.Span;
        if (cel + 6 > span.Length)
        {
            throw new InvalidDataException($"Costume {Number}: the cel of frame {frame} is past the end of the costume.");
        }

        var result = new V0Cel(span[cel], span[cel + 1], span[cel + 2], span[cel + 3], (sbyte)span[cel + 4], (sbyte)span[cel + 5], cel + 6);
        if (result.DataOffset + result.Columns * result.Height > span.Length)
        {
            throw new InvalidDataException($"Costume {Number}: the cel of frame {frame} runs past the end of the costume.");
        }

        return result;
    }

    private byte At(int offset, string what)
    {
        var span = Data.Span;
        if (offset < 0 || offset >= span.Length)
        {
            throw new InvalidDataException($"Costume {Number}: a {what} at offset {offset} is past the end of the costume.");
        }

        return span[offset];
    }
}
