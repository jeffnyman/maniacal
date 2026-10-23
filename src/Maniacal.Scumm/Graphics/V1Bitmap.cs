using System.Buffers.Binary;
using Daventry.Core.Graphics;
using Maniacal.Scumm.Rooms;

namespace Maniacal.Scumm.Graphics;

/// <summary>
/// The picture format of version 1 rooms, kept from the C64 original: the room is a grid of 8x8
/// tiles from the room's own character set, drawn in C64 multicolour mode. Mirrors ScummVM's
/// <c>GdiV1::roomChanged</c>, <c>decodeV1Gfx</c>, <c>drawStripV1Background</c> and <c>drawStripV1Mask</c>.
/// </summary>
/// <remarks>
/// <para>The header holds four colours at 6 to 9 and the offsets of five compressed layers at 10 to
/// 18: the character set (256 tiles of 8 bytes), the tile map, the colour map, the mask map (one
/// byte per cell each, column by column) and the mask characters (preceded by a length word that is
/// 8 more than the data).</para>
/// <para>Each tile row is a byte of four 2-bit pixels, each drawn 2 pixels wide. Values 0 to 2 pick
/// the room's first three colours and 3 the cell's own colour from the colour map. Colours are C64
/// colour numbers, shown through <see cref="Palettes.ManiacV1ToEga"/>.</para>
/// </remarks>
public static class V1Bitmap
{
    private const int TileSize = 8;
    private const int CharsetSize = 256 * TileSize;

    public static MaskedImage DecodeRoom(Room room)
    {
        var data = room.Data.Span;
        int columns = data[4];
        int rows = data[5];
        int cells = columns * rows;

        Span<byte> colors = stackalloc byte[4];
        data.Slice(6, 4).CopyTo(colors);

        var charset = DecodeLayer(data, Word(data, 10), CharsetSize, out _);
        var tiles = DecodeLayer(data, Word(data, 12), cells, out _);
        var cellColors = DecodeLayer(data, Word(data, 14), cells, out _);
        var maskTiles = DecodeLayer(data, Word(data, 16), cells, out _);
        int maskOffset = Word(data, 18);
        var maskChars = DecodeLayer(data, maskOffset + 2, Word(data, maskOffset) - 8, out _);

        var colorMap = Palettes.ManiacV1ToEga;
        var pixels = new Surface8(columns * TileSize, rows * TileSize);
        var mask = new Surface8(columns * TileSize, rows * TileSize);

        for (int column = 0; column < columns; column++)
        {
            for (int row = 0; row < rows; row++)
            {
                int cell = row + column * rows;
                colors[3] = (byte)(cellColors[cell] & 7);
                int tile = tiles[cell] * TileSize;
                int maskTile = maskTiles[cell] * TileSize;

                for (int line = 0; line < TileSize; line++)
                {
                    int y = row * TileSize + line;
                    int x = column * TileSize;
                    byte bits = charset[tile + line];
                    for (int pair = 0; pair < 4; pair++)
                    {
                        byte color = colorMap[colors[(bits >> (6 - 2 * pair)) & 3]];
                        pixels[x + 2 * pair, y] = color;
                        pixels[x + 2 * pair + 1, y] = color;
                    }

                    // the mask characters set the bits of the pixels behind the foreground, the
                    // opposite of the masks of later versions
                    byte front = (byte)(maskChars[maskTile + line] ^ 0xFF);
                    for (int bit = 0; bit < 8; bit++)
                    {
                        mask[x + bit, y] = (byte)((front >> (7 - bit)) & 1);
                    }
                }
            }
        }

        return new MaskedImage(pixels, mask);
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
