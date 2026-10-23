using Daventry.Core.Graphics;
using Maniacal.Scumm.Rooms;

namespace Maniacal.Scumm.Graphics;

/// <summary>
/// Draws rooms and their objects. Both versions give EGA colour numbers, for <see cref="Palettes.Ega"/>.
/// </summary>
public static class RoomRenderer
{
    /// <summary>The state bit that makes an object draw its image. Mirrors ScummVM's <c>kObjectStateIntrinsic</c>.</summary>
    public const int ImageState = 8;

    public static MaskedImage DrawBackground(Room room) => room.Version switch
    {
        1 => V1Bitmap.ForRoom(room).DrawBackground(),
        _ => DecodeV2(room, room.ImageOffset, room.Width, room.Height),
    };

    /// <summary>
    /// Whether the object's image offset points into the room's images. Most objects without an
    /// image point it at the room's first object code block, past the end of the images.
    /// </summary>
    public static bool HasImage(Room room, RoomObject obj)
        => obj.Width > 0 && obj.Height > 0 && obj.ImageOffset > 0 && obj.ImageOffset < FirstCodeOffset(room);

    /// <summary>
    /// Whether the object's image is clearly its own. Objects painted into the background (the
    /// hallway's clock and vases, the windows of the upstairs hall) have no image either, but point
    /// at another object's, which has a different size. The game never draws them, because their
    /// state never has <see cref="ImageState"/> set; this test only matters when drawing images
    /// regardless of state.
    /// </summary>
    public static bool OwnsImage(Room room, RoomObject obj)
        => HasImage(room, obj)
        && !room.Objects.Any(other => other.ImageOffset == obj.ImageOffset && (other.Width != obj.Width || other.Height != obj.Height));

    /// <summary>Decodes an object's image, or returns null when it has none.</summary>
    public static MaskedImage? DrawObject(Room room, RoomObject obj, V1Bitmap? v1 = null)
    {
        if (!HasImage(room, obj))
        {
            return null;
        }

        return room.Version == 1
            ? (v1 ?? V1Bitmap.ForRoom(room)).DrawObject(room.Data.Span, obj.ImageOffset, obj.Width, obj.Height)
            : DecodeV2(room, obj.ImageOffset, obj.Width, obj.Height);
    }

    /// <summary>
    /// Draws the room with the objects whose state, given by global object number, makes them
    /// visible. Mirrors ScummVM's <c>drawRoomObjects</c> and <c>drawRoomObject</c>: objects are
    /// drawn from the last to the first, each only when its state has <see cref="ImageState"/> set
    /// and every parent up the chain is in the state its child expects. An object's mask replaces
    /// the room's mask where it is drawn.
    /// </summary>
    public static MaskedImage Render(Room room, Func<int, int> stateOf)
    {
        var image = DrawBackground(room);
        var v1 = room.Version == 1 ? V1Bitmap.ForRoom(room) : null;

        foreach (var obj in VisibleObjects(room, stateOf))
        {
            if (DrawObject(room, obj, v1) is { } objectImage)
            {
                Paste(image, objectImage, obj.X, obj.Y);
            }
        }

        return image;
    }

    /// <summary>The objects <see cref="Render"/> draws, in the order it draws them.</summary>
    public static IEnumerable<RoomObject> VisibleObjects(Room room, Func<int, int> stateOf)
    {
        for (int i = room.Objects.Count - 1; i >= 0; i--)
        {
            var obj = room.Objects[i];
            if (obj.Number != 0 && (stateOf(obj.Number) & ImageState) != 0 && ParentsAllow(room, obj, stateOf))
            {
                yield return obj;
            }
        }
    }

    /// <summary>
    /// Draws the room with every image that clearly belongs to its object (<see cref="OwnsImage"/>),
    /// whatever the states: for looking at the images, not as the game shows the room.
    /// </summary>
    public static MaskedImage RenderAll(Room room)
    {
        var image = DrawBackground(room);
        var v1 = room.Version == 1 ? V1Bitmap.ForRoom(room) : null;
        for (int i = room.Objects.Count - 1; i >= 0; i--)
        {
            if (OwnsImage(room, room.Objects[i]) && DrawObject(room, room.Objects[i], v1) is { } objectImage)
            {
                Paste(image, objectImage, room.Objects[i].X, room.Objects[i].Y);
            }
        }

        return image;
    }

    private static bool ParentsAllow(Room room, RoomObject obj, Func<int, int> stateOf)
    {
        // the parent indexes are 1-based; walk up while each parent's image state matches what its child expects
        var current = obj;
        for (int depth = 0; depth < room.Objects.Count; depth++)
        {
            if (current.Parent == 0)
            {
                return true;
            }

            int expected = current.ParentState;
            current = room.Objects[current.Parent - 1];
            if ((stateOf(current.Number) & ImageState) != expected)
            {
                return false;
            }
        }

        return false;
    }

    private static void Paste(MaskedImage target, MaskedImage source, int left, int top)
    {
        int width = Math.Min(source.Pixels.Width, target.Pixels.Width - left);
        int height = Math.Min(source.Pixels.Height, target.Pixels.Height - top);
        for (int y = Math.Max(0, -top); y < height; y++)
        {
            for (int x = Math.Max(0, -left); x < width; x++)
            {
                target.Pixels[left + x, top + y] = source.Pixels[x, y];
                target.Mask[left + x, top + y] = source.Mask[x, y];
            }
        }
    }

    private static MaskedImage DecodeV2(Room room, int offset, int width, int height)
    {
        var data = room.Data.Span;
        if (offset <= 0 || offset >= data.Length)
        {
            throw new InvalidDataException($"Room {room.Number}: picture offset {offset} is outside the room ({data.Length} bytes).");
        }

        return V2Bitmap.Decode(data[offset..], width, height, out _);
    }

    private static int FirstCodeOffset(Room room)
        => room.Objects.Count == 0 ? room.Data.Length : room.Objects.Min(o => o.CodeOffset);
}
