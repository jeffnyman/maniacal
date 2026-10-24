namespace Maniacal.Scumm.Rooms;

/// <summary>A point in actor units: 8-pixel columns and 2-pixel lines.</summary>
public readonly record struct Point(int X, int Y)
{
    public int SquareDistance(Point other) => (X - other.X) * (X - other.X) + (Y - other.Y) * (Y - other.Y);
}

/// <summary>A walkbox's corners. Mirrors ScummVM's <c>BoxCoords</c>.</summary>
public readonly record struct BoxCorners(Point UpperLeft, Point UpperRight, Point LowerRight, Point LowerLeft)
{
    /// <summary>The corners of a version 1 or 2 box: horizontal top and bottom edges. Mirrors the <c>version &lt;= 2</c> branch of <c>getBoxCoordinates</c>.</summary>
    public static BoxCorners Of(WalkBox box) => new(
        new Point(box.UpperLeftX, box.UpperY),
        new Point(box.UpperRightX, box.UpperY),
        new Point(box.LowerRightX, box.LowerY),
        new Point(box.LowerLeftX, box.LowerY));
}

/// <summary>
/// The walkbox geometry of versions 1 and 2. Mirrors ScummVM's <c>compareSlope</c>,
/// <c>closestPtOnLine</c>, <c>checkXYInBoxBounds</c> and <c>getClosestPtOnBox</c> in
/// <c>boxes.cpp</c>, and the <c>checkXYInBoxBounds</c> of <c>actor.cpp</c> that version 2
/// actors use to find the nearest point of a box.
/// </summary>
public static class BoxGeometry
{
    /// <summary>The flag bits of a box. Mirrors <c>kBoxPlayerOnly</c>, <c>kBoxLocked</c> and <c>kBoxInvisible</c>.</summary>
    public const int PlayerOnly = 0x20, Locked = 0x40, Invisible = 0x80;

    /// <summary>No box. Mirrors <c>kOldInvalidBox</c>.</summary>
    public const int NoBox = 255;

    private static bool CompareSlope(Point p1, Point p2, Point p3)
        => (p2.Y - p1.Y) * (p3.X - p1.X) <= (p3.Y - p1.Y) * (p2.X - p1.X);

    /// <summary>Mirrors <c>closestPtOnLine</c>, with its integer arithmetic.</summary>
    public static Point ClosestPointOnLine(Point start, Point end, Point p)
    {
        int lxdiff = end.X - start.X;
        int lydiff = end.Y - start.Y;
        Point result;

        if (end.X == start.X)
        {
            result = new Point(start.X, p.Y);
        }
        else if (end.Y == start.Y)
        {
            result = new Point(p.X, start.Y);
        }
        else
        {
            int dist = lxdiff * lxdiff + lydiff * lydiff;
            if (Math.Abs(lxdiff) > Math.Abs(lydiff))
            {
                int a = start.X * lydiff / lxdiff;
                int b = p.X * lxdiff / lydiff;
                int c = (a + b - start.Y + p.Y) * lydiff * lxdiff / dist;
                result = new Point(c, c * lydiff / lxdiff - a + start.Y);
            }
            else
            {
                int a = start.Y * lxdiff / lydiff;
                int b = p.Y * lydiff / lxdiff;
                int c = (a + b - start.X + p.X) * lydiff * lxdiff / dist;
                result = new Point(c * lxdiff / lydiff - a + start.X, c);
            }
        }

        if (Math.Abs(lydiff) < Math.Abs(lxdiff))
        {
            if (lxdiff > 0)
            {
                if (result.X < start.X)
                {
                    result = start;
                }
                else if (result.X > end.X)
                {
                    result = end;
                }
            }
            else if (result.X > start.X)
            {
                result = start;
            }
            else if (result.X < end.X)
            {
                result = end;
            }
        }
        else if (lydiff > 0)
        {
            if (result.Y < start.Y)
            {
                result = start;
            }
            else if (result.Y > end.Y)
            {
                result = end;
            }
        }
        else if (result.Y > start.Y)
        {
            result = start;
        }
        else if (result.Y < end.Y)
        {
            result = end;
        }

        return result;
    }

    /// <summary>Whether a point is inside a box. Mirrors <c>ScummEngine::checkXYInBoxBounds</c> for versions before 5.</summary>
    public static bool Contains(BoxCorners box, int x, int y)
    {
        var (ul, ur, lr, ll) = box;
        var p = new Point(x, y);
        if (x < ul.X && x < ur.X && x < lr.X && x < ll.X)
        {
            return false;
        }

        if (x > ul.X && x > ur.X && x > lr.X && x > ll.X)
        {
            return false;
        }

        if (y < ul.Y && y < ur.Y && y < lr.Y && y < ll.Y)
        {
            return false;
        }

        if (y > ul.Y && y > ur.Y && y > lr.Y && y > ll.Y)
        {
            return false;
        }

        return CompareSlope(ul, ur, p) && CompareSlope(ur, lr, p) && CompareSlope(lr, ll, p) && CompareSlope(ll, ul, p);
    }

    /// <summary>The nearest point on a box's edges and its squared distance. Mirrors <c>getClosestPtOnBox</c>.</summary>
    public static (int SquareDistance, Point Point) ClosestPointOnBox(BoxCorners box, int x, int y)
    {
        var p = new Point(x, y);
        int best = 0xFFFFFF;
        var bestPoint = p;
        foreach (var (start, end) in new[] { (box.UpperLeft, box.UpperRight), (box.UpperRight, box.LowerRight), (box.LowerRight, box.LowerLeft), (box.LowerLeft, box.UpperLeft) })
        {
            var point = ClosestPointOnLine(start, end, p);
            int dist = p.SquareDistance(point);
            if (dist < best)
            {
                best = dist;
                bestPoint = point;
            }
        }

        return (best, bestPoint);
    }

    /// <summary>
    /// The nearest point of a box to a point, inside or on it, and a rough distance to it (0 when
    /// the point is in the box; lines count a quarter of columns). Mirrors the static
    /// <c>checkXYInBoxBounds</c> of <c>actor.cpp</c> for versions 1 and 2.
    /// </summary>
    public static (int Distance, Point Point) NearestInBox(BoxCorners box, int x, int y)
    {
        int destX = x;
        int destY;
        int xmin, xmax;
        if (y < box.UpperLeft.Y)
        {
            destY = box.UpperLeft.Y;
            xmin = box.UpperLeft.X;
            xmax = box.UpperRight.X;
        }
        else if (y >= box.LowerLeft.Y)
        {
            destY = box.LowerLeft.Y;
            xmin = box.LowerLeft.X;
            xmax = box.LowerRight.X;
        }
        else if (x >= box.UpperLeft.X && x >= box.LowerLeft.X && x < box.UpperRight.X && x < box.LowerRight.X)
        {
            destY = y;
            xmin = xmax = x;
        }
        else
        {
            // halve the box until the line at y is found, to get the edges there (bounded, for a malformed box)
            destY = y;
            int ul = box.UpperLeft.X, ll = box.LowerLeft.X, ur = box.UpperRight.X, lr = box.LowerRight.X;
            int top = box.UpperLeft.Y, bottom = box.LowerLeft.Y;
            int cury;
            int guard = 0;
            do
            {
                xmin = (ul + ll) / 2;
                xmax = (ur + lr) / 2;
                cury = (top + bottom) / 2;
                if (cury < y)
                {
                    top = cury;
                    ul = xmin;
                    ur = xmax;
                }
                else if (cury > y)
                {
                    bottom = cury;
                    ll = xmin;
                    lr = xmax;
                }
            }
            while (cury != y && ++guard < 32);
        }

        destX = x < xmin ? xmin : x > xmax ? xmax : x;

        int xDist = Math.Abs(x - destX);
        int yDist = Math.Abs(y - destY) / 4;
        int dist = xDist < yDist ? (xDist >> 1) + yDist : (yDist >> 1) + xDist;
        return (dist, new Point(destX, destY));
    }
}
