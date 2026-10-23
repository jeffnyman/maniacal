using Maniacal.Scumm.Rooms;

namespace Maniacal.Scumm.Graphics;

/// <summary>Decodes a room's background picture and mask, in the room's own colour numbers (see <see cref="Palettes"/>).</summary>
public static class RoomBackground
{
    public static MaskedImage Decode(Room room)
    {
        if (room.Version != 2)
        {
            throw new NotSupportedException($"Room {room.Number}: version {room.Version} room graphics are not ported yet.");
        }

        var data = room.Data.Span;
        if (room.ImageOffset <= 0 || room.ImageOffset >= data.Length)
        {
            throw new InvalidDataException($"Room {room.Number}: picture offset {room.ImageOffset} is outside the room ({data.Length} bytes).");
        }

        return V2Bitmap.Decode(data[room.ImageOffset..], room.Width, room.Height, out _);
    }
}
