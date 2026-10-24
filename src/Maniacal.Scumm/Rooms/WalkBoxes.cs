namespace Maniacal.Scumm.Rooms;

/// <summary>
/// A walkbox: a quadrilateral with horizontal top and bottom edges. X is in 8-pixel columns and Y
/// in 2-pixel units, the units actors move in (ScummVM's <c>V12_X_MULTIPLIER</c> and
/// <c>V12_Y_MULTIPLIER</c>). Mirrors the <c>v2</c> member of ScummVM's <c>Box</c> and its conversion
/// in <c>getBoxCoordinates</c>; version 0's rectangles, the <c>v0</c> member, are read into the
/// same shape.
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

    /// <summary>Mirrors <c>SIZEOF_BOX_V0</c>.</summary>
    public const int SizeV0 = 5;

    // a version 0 box with both these mask bits is a corner: one of its top corners is the other
    private const byte CornerBits = 0x88, CornerOnLeft = 0x04;

    internal static WalkBox Read(ReadOnlySpan<byte> data)
        => new(data[0], data[1], data[2], data[3], data[4], data[5], data[6], data[7]);

    /// <summary>
    /// A version 0 box: x1, x2, y1, y2 and the mask, a rectangle but for corner boxes, whose top
    /// narrows to the left or right corner. Version 0 boxes have no flags. Mirrors the version 0
    /// branch of <c>getBoxCoordinates</c>.
    /// </summary>
    internal static WalkBox ReadV0(ReadOnlySpan<byte> data)
    {
        byte x1 = data[0], x2 = data[1], y1 = data[2], y2 = data[3], mask = data[4];
        byte upperLeft = x1, upperRight = x2;
        if ((mask & CornerBits) == CornerBits)
        {
            if ((mask & CornerOnLeft) != 0)
            {
                upperRight = x1;
            }
            else
            {
                upperLeft = x2;
            }
        }

        return new(y1, y2, upperLeft, upperRight, x1, x2, mask, 0);
    }
}

/// <summary>
/// A room's walkboxes and box matrix. The version 1 and 2 matrix is a full table: a row start per
/// box, then for each pair of boxes the next box on the way from one to the other. Version 0 keeps
/// only each box's neighbours, a list ended by 0xFF, and finds its way with them itself. Mirrors
/// the box loading of <c>ScummEngine_v3old::resetRoomSubBlocks</c> and <c>getNextBox</c>.
/// </summary>
public sealed class WalkBoxes
{
    // the list that ends a version 0 box list and each of its neighbour lists
    private const byte EndOfList = 0xFF;

    private readonly byte[] _matrix;
    private readonly byte[][]? _neighbours;

    private WalkBoxes(WalkBox[] boxes, byte[] matrix, byte[][]? neighbours = null)
    {
        Boxes = boxes;
        _matrix = matrix;
        _neighbours = neighbours;
    }

    public static readonly WalkBoxes Empty = new([], []);

    /// <summary>For version 0, the boxes each box joins directly; null for the other versions.</summary>
    public IReadOnlyList<IReadOnlyList<byte>>? Neighbours => _neighbours;

    public IReadOnlyList<WalkBox> Boxes { get; }

    public int Count => Boxes.Count;

    /// <summary>The next box on the way from one box to another, or -1 when there is no way.</summary>
    public int NextBox(int from, int to)
    {
        if (from < 0 || from >= Count || to < 0 || to >= Count)
        {
            throw new ArgumentOutOfRangeException(from < 0 || from >= Count ? nameof(from) : nameof(to));
        }

        if (_neighbours is not null)
        {
            // version 0: the box itself when it is a neighbour, else none
            return Array.IndexOf(_neighbours[from], (byte)to) >= 0 ? to : -1;
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

    /// <summary>Version 0: the boxes up to the 0xFF after them, then a neighbour list for each.</summary>
    internal static WalkBoxes ParseV0(ReadOnlySpan<byte> data)
    {
        var boxes = new List<WalkBox>();
        int position = 0;
        while (position < data.Length && data[position] != EndOfList)
        {
            if (position + WalkBox.SizeV0 > data.Length)
            {
                throw new InvalidDataException("The walkboxes run past the end of the room.");
            }

            boxes.Add(WalkBox.ReadV0(data.Slice(position, WalkBox.SizeV0)));
            position += WalkBox.SizeV0;
        }

        position++;
        var neighbours = new byte[boxes.Count][];
        for (int i = 0; i < boxes.Count; i++)
        {
            int end = position < data.Length ? data[position..].IndexOf(EndOfList) : -1;
            if (end < 0)
            {
                throw new InvalidDataException($"The neighbours of walkbox {i} run past the end of the room.");
            }

            neighbours[i] = data.Slice(position, end).ToArray();
            position += end + 1;
        }

        return new WalkBoxes([.. boxes], [], neighbours);
    }
}
