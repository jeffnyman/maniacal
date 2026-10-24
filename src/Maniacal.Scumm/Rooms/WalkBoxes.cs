namespace Maniacal.Scumm.Rooms;

/// <summary>
/// A walkbox of versions 1 and 2: a quadrilateral with horizontal top and bottom edges. X is in
/// 8-pixel columns and Y in 2-pixel units, the units actors move in (ScummVM's
/// <c>V12_X_MULTIPLIER</c> and <c>V12_Y_MULTIPLIER</c>). Mirrors the <c>v2</c> member of ScummVM's
/// <c>Box</c> and its conversion in <c>getBoxCoordinates</c>.
/// </summary>
public readonly record struct WalkBox(
    byte UpperY,
    byte LowerY,
    byte UpperLeftX,
    byte UpperRightX,
    byte LowerLeftX,
    byte LowerRightX,
    byte Mask,
    byte Flags)
{
    /// <summary>Mirrors <c>SIZEOF_BOX_V2</c>.</summary>
    public const int Size = 8;

    internal static WalkBox Read(ReadOnlySpan<byte> data)
        => new(data[0], data[1], data[2], data[3], data[4], data[5], data[6], data[7]);
}

/// <summary>
/// A room's walkboxes and box matrix. The version 2 matrix is a full table: a row start per box,
/// then for each pair of boxes the next box on the way from one to the other. Mirrors
/// <c>ScummEngine_v3old::resetRoomSubBlocks</c> and the version 2 branch of <c>getNextBox</c>.
/// </summary>
public sealed class WalkBoxes
{
    private readonly byte[] _matrix;

    private WalkBoxes(WalkBox[] boxes, byte[] matrix)
    {
        Boxes = boxes;
        _matrix = matrix;
    }

    public static readonly WalkBoxes Empty = new([], []);

    public IReadOnlyList<WalkBox> Boxes { get; }

    public int Count => Boxes.Count;

    /// <summary>The next box on the way from one box to another, or -1 when there is no way.</summary>
    public int NextBox(int from, int to)
    {
        if (from < 0 || from >= Count || to < 0 || to >= Count)
        {
            throw new ArgumentOutOfRangeException(from < 0 || from >= Count ? nameof(from) : nameof(to));
        }

        return (sbyte)_matrix[Count + _matrix[from] + to];
    }

    /// <summary>
    /// The first box containing a point in actor units, or -1. Each box spans its top and bottom
    /// lines, with left and right edges running straight from the upper to the lower corners. This is
    /// a plain containment test for placing actors; ScummVM's <c>checkXYInBoxBounds</c> comes with walking.
    /// </summary>
    public int FindBox(int x, int y)
    {
        for (int i = 0; i < Count; i++)
        {
            var box = Boxes[i];
            if (y < box.UpperY || y > box.LowerY)
            {
                continue;
            }

            int span = box.LowerY - box.UpperY;
            int left = span == 0 ? Math.Min(box.UpperLeftX, box.LowerLeftX) : box.UpperLeftX + (box.LowerLeftX - box.UpperLeftX) * (y - box.UpperY) / span;
            int right = span == 0 ? Math.Max(box.UpperRightX, box.LowerRightX) : box.UpperRightX + (box.LowerRightX - box.UpperRightX) * (y - box.UpperY) / span;
            if (x >= left && x <= right)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Reads the box count, the boxes and the matrix that follows them.</summary>
    internal static WalkBoxes Parse(ReadOnlySpan<byte> data)
    {
        int count = data[0];
        int matrixSize = count * (count + 1);
        if (1 + count * WalkBox.Size + matrixSize > data.Length)
        {
            throw new InvalidDataException($"{count} walkboxes and their matrix do not fit in the room.");
        }

        var boxes = new WalkBox[count];
        for (int i = 0; i < count; i++)
        {
            boxes[i] = WalkBox.Read(data.Slice(1 + i * WalkBox.Size, WalkBox.Size));
        }

        return new WalkBoxes(boxes, data.Slice(1 + count * WalkBox.Size, matrixSize).ToArray());
    }
}
