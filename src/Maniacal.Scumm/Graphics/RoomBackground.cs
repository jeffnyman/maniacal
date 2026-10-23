using Maniacal.Scumm.Rooms;

namespace Maniacal.Scumm.Graphics;

/// <summary>Decodes a room's background picture and mask. Both versions give EGA colour numbers, for <see cref="Palettes.Ega"/>.</summary>
public static class RoomBackground
{
    public static MaskedImage Decode(Room room)
    {
        if (room.Version == 1)
        {
            return V1Bitmap.DecodeRoom(room);
        }

        var data = room.Data.Span;
        if (room.ImageOffset <= 0 || room.ImageOffset >= data.Length)
        {
            throw new InvalidDataException($"Room {room.Number}: picture offset {room.ImageOffset} is outside the room ({data.Length} bytes).");
        }

        return V2Bitmap.Decode(data[room.ImageOffset..], room.Width, room.Height, out _);
    }
}
