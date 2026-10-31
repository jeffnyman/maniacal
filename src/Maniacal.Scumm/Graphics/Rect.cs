namespace Maniacal.Scumm.Graphics;

/// <summary>An integer rectangle. Right and Bottom are exclusive.</summary>
public readonly record struct Rect(int X, int Y, int Width, int Height)
{
    /// <summary>The first column inside.</summary>
    public int Left => X;

    /// <summary>The first row inside.</summary>
    public int Top => Y;

    /// <summary>The first column past the right edge.</summary>
    public int Right => X + Width;

    /// <summary>The first row below the bottom edge.</summary>
    public int Bottom => Y + Height;

    /// <summary>Whether it covers no pixel.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>The rectangle from its edges, right and bottom exclusive.</summary>
    public static Rect FromBounds(int left, int top, int right, int bottom)
        => new(left, top, right - left, bottom - top);

    /// <summary>Whether the pixel is inside.</summary>
    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;

    /// <summary>The part both rectangles cover, or an empty rectangle.</summary>
    public Rect Intersect(Rect other)
    {
        int l = Math.Max(X, other.X);
        int t = Math.Max(Y, other.Y);
        int r = Math.Min(Right, other.Right);
        int b = Math.Min(Bottom, other.Bottom);
        return r <= l || b <= t ? default : FromBounds(l, t, r, b);
    }

    /// <summary>The rectangle moved by the distances.</summary>
    public Rect Offset(int dx, int dy) => new(X + dx, Y + dy, Width, Height);
}
