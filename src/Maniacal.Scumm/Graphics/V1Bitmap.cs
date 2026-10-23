using System.Buffers.Binary;
using Daventry.Core.Graphics;
using Maniacal.Scumm.Rooms;

namespace Maniacal.Scumm.Graphics;

/// <summary>
/// The picture format of version 1 rooms and objects, kept from the C64 original: pictures are
/// grids of 8x8 tiles from the room's own character set, drawn in C64 multicolour mode. Mirrors
/// ScummVM's <c>GdiV1::roomChanged</c>, <c>decodeV1Gfx</c>, <c>drawStripV1Background</c>,
/// <c>drawStripV1Object</c> and <c>drawStripV1Mask</c>.
/// </summary>
/// <remarks>
/// <para>The room header holds four colours at 6 to 9 and the offsets of five compressed layers at
/// 10 to 18: the character set (256 tiles of 8 bytes), the tile map, the colour map, the mask map
/// (one byte per cell each, column by column) and the mask characters (preceded by a length word
/// that is 8 more than the data).</para>
/// <para>Each tile row is a byte of four 2-bit pixels, each drawn 2 pixels wide. Values 0 to 2 pick
/// the room's first three colours and 3 the cell's own colour from the colour map. Colours are C64
/// colour numbers, shown through <see cref="Palettes.ManiacV1ToEga"/>.</para>
/// <para>An object image is one compressed layer holding three maps of the object's size, row by
/// row: tiles, cell colours and mask tiles, drawn with the room's character sets.</para>
/// </remarks>
public sealed class V1Bitmap
{
    private const int TileSize = 8;
    private const int CharsetSize = 256 * TileSize;

    private readonly byte[] _colors = new byte[4];
    private readonly byte[] _charset;
    private readonly byte[] _tiles;
    private readonly byte[] _cellColors;
    private readonly byte[] _maskTiles;
    private readonly byte[] _maskChars;
    private readonly int _columns;
    private readonly int _rows;

    private V1Bitmap(Room room)
    {
        var data = room.Data.Span;
        _columns = data[4];
        _rows = data[5];
        int cells = _columns * _rows;
        data.Slice(6, 4).CopyTo(_colors);

        _charset = DecodeLayer(data, Word(data, 10), CharsetSize, out _);
        _tiles = DecodeLayer(data, Word(data, 12), cells, out _);
        _cellColors = DecodeLayer(data, Word(data, 14), cells, out _);
        _maskTiles = DecodeLayer(data, Word(data, 16), cells, out _);
        int maskOffset = Word(data, 18);
        _maskChars = DecodeLayer(data, maskOffset + 2, Word(data, maskOffset) - 8, out int end);
        GraphicsEnd = end;
    }

    /// <summary>The offset just past the room's graphics, where its object images begin.</summary>
    public int GraphicsEnd { get; }

    /// <summary>Decodes a room's character sets and maps, which its background and objects are drawn from.</summary>
    public static V1Bitmap ForRoom(Room room)
    {
        if (room.Version != 1)
        {
            throw new ArgumentException($"Room {room.Number} is version {room.Version}, not 1.", nameof(room));
        }

        return new V1Bitmap(room);
    }

    public MaskedImage DrawBackground()
    {
        var image = Create(_columns, _rows);
        for (int column = 0; column < _columns; column++)
        {
            for (int row = 0; row < _rows; row++)
            {
                int cell = row + column * _rows;
                DrawCell(image, column, row, _tiles[cell], _cellColors[cell], _maskTiles[cell]);
            }
        }

        return image;
    }

    /// <summary>Draws an object's image from the room data at <paramref name="offset"/>; width and height in pixels.</summary>
    public MaskedImage DrawObject(ReadOnlySpan<byte> room, int offset, int width, int height)
    {
        int columns = width / TileSize;
        int rows = height / TileSize;
        int cells = columns * rows;
        var maps = DecodeLayer(room, offset, cells * 3, out _);

        var image = Create(columns, rows);
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                int cell = row * columns + column;
                DrawCell(image, column, row, maps[cell], maps[cells + cell], maps[2 * cells + cell]);
            }
        }

        return image;
    }

    /// <summary>
    /// Decompresses one layer: four common byte values, then runs. A control byte with bit 7 set
    /// repeats common value <c>(byte &gt;&gt; 5) &amp; 3</c>, with bit 6 set repeats the next byte,
    /// and otherwise copies that many literal bytes; the count is the low bits plus one.
    /// </summary>
    public static byte[] DecodeLayer(ReadOnlySpan<byte> data, int offset, int size, out int end)
    {
        if (size < 0 || offset < 0 || offset + 4 > data.Length)
        {
            throw new InvalidDataException($"A picture layer at offset {offset} of size {size} does not fit in the room.");
        }

        Span<byte> common = stackalloc byte[4];
        data.Slice(offset, 4).CopyTo(common);
        int source = offset + 4;
        var output = new byte[size];
        int x = 0;

        while (x < size)
        {
            byte control = Next(data, ref source);
            if ((control & 0x80) != 0)
            {
                Fill(output, ref x, common[(control >> 5) & 3], (control & 0x1F) + 1);
            }
            else if ((control & 0x40) != 0)
            {
                Fill(output, ref x, Next(data, ref source), (control & 0x3F) + 1);
            }
            else
            {
                for (int count = control + 1; count > 0; count--)
                {
                    byte value = Next(data, ref source);
                    if (x < size)
                    {
                        output[x++] = value;
                    }
                }
            }
        }

        end = source;
        return output;
    }

    private static MaskedImage Create(int columns, int rows)
        => new(new Surface8(columns * TileSize, rows * TileSize), new Surface8(columns * TileSize, rows * TileSize));

    private void DrawCell(MaskedImage image, int column, int row, byte tile, byte cellColor, byte maskTile)
    {
        if ((maskTile + 1) * TileSize > _maskChars.Length)
        {
            throw new InvalidDataException($"Mask tile {maskTile} is past the room's {_maskChars.Length / TileSize} mask tiles.");
        }

        var colorMap = Palettes.ManiacV1ToEga;
        _colors[3] = (byte)(cellColor & 7);
        int x = column * TileSize;
        for (int line = 0; line < TileSize; line++)
        {
            int y = row * TileSize + line;
            byte bits = _charset[tile * TileSize + line];
            for (int pair = 0; pair < 4; pair++)
            {
                byte color = colorMap[_colors[(bits >> (6 - 2 * pair)) & 3]];
                image.Pixels[x + 2 * pair, y] = color;
                image.Pixels[x + 2 * pair + 1, y] = color;
            }

            // the mask characters set the bits of the pixels behind the foreground, the opposite
            // of the masks of later versions
            byte front = (byte)(_maskChars[maskTile * TileSize + line] ^ 0xFF);
            for (int bit = 0; bit < 8; bit++)
            {
                image.Mask[x + bit, y] = (byte)((front >> (7 - bit)) & 1);
            }
        }
    }

    private static void Fill(byte[] output, ref int x, byte value, int count)
    {
        // a run may overshoot the layer's size; ScummVM's loop does not check, here the excess is dropped
        for (; count > 0 && x < output.Length; count--)
        {
            output[x++] = value;
        }
    }

    private static byte Next(ReadOnlySpan<byte> data, ref int position)
    {
        if (position >= data.Length)
        {
            throw new InvalidDataException($"A picture layer runs past the end of the room, at offset {position}.");
        }

        return data[position++];
    }

    private static int Word(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
}
