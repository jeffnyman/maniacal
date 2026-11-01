using System.Buffers.Binary;
using Maniacal.Scumm.Rooms;

namespace Maniacal.Scumm.Graphics;

/// <summary>
/// The picture format of the NES's rooms and objects: 8x8 tiles of two bit planes from the NES's
/// pattern table, laid out by a nametable, coloured by four-colour sub-palettes that the attribute
/// table picks for each 2x2 tile block, and masked by a bit table, a bit a column. Mirrors
/// ScummVM's <c>GdiNES</c> (<c>decodeNESGfx</c>, <c>decodeNESObject</c>, <c>drawStripNES</c>,
/// <c>drawStripNESMask</c>) and <c>decodeNESTileData</c>.
/// </summary>
/// <remarks>
/// <para>The room header points at three blocks: at 0x0A the graphics (the tileset, the 16-colour
/// palette, then the nametable's 16 rows), at 0x0C the 64 attribute bytes and at 0x0E the mask (a
/// count, 0 for none, the width in bytes, then 16 rows). Each is run-length coded: a count byte
/// with bit 7 for that many bytes to copy, else one byte to repeat that many times.</para>
/// <para>The nametable keeps a gap of two tiles at each edge, so the room's column <c>x</c> is the
/// nametable's <c>x + 2</c>; the mask is not shifted. Colours are NES colour numbers, which
/// <see cref="Palettes.Nes"/> shows. In the dark the room is drawn in a dark palette, the flashlight
/// showing it so.</para>
/// <para>An object's image patches the nametable, the attributes and the mask where it lies, and
/// is drawn from them; the patches add up, as they do in ScummVM, while the room is on screen.</para>
/// </remarks>
internal sealed class NesBitmap
{
    private const int TileSize = 8, TileBytes = 16, Rows = 16, NametableWidth = 64, MaskBytes = 8, Gap = 2;

    /// <summary>The 256 tiles of the room graphics' pattern table, 16 bytes each.</summary>
    public const int PatternTableSize = 256 * TileBytes;

    // the costume slots holding the tiles every room shares and, after them, each tileset (costume 37 + tileset)
    private const int BaseTiles = 37;

    // the NES's black, which the screen is cleared to (0x1D in ScummVM's NES code)
    public const byte Black = 0x1D;

    // the colours of a room without its lights (drawStripNES)
    private static ReadOnlySpan<byte> DarkPalette => [0x2D, 0x1D, 0x3D, 0x20, 0x2D, 0x1D, 0x3D, 0x20, 0x2D, 0x1D, 0x3D, 0x20, 0x2D, 0x1D, 0x3D, 0x20];

    private readonly byte[] _patterns;
    private readonly byte[] _palette = new byte[16];
    private readonly byte[,] _nametable = new byte[Rows, NametableWidth];
    private readonly byte[] _attributes = new byte[64];
    private readonly byte[,] _mask = new byte[Rows, MaskBytes];
    private readonly bool _hasMask;
    private readonly int _width;

    // the tables objects patch (nametableObj, attributesObj, masktableObj)
    private readonly byte[,] _objectNametable;
    private readonly byte[] _objectAttributes;
    private readonly byte[,] _objectMask;

    private NesBitmap(Room room, byte[] patterns)
    {
        _patterns = patterns;
        var data = room.Data.Span;
        int width = Word(data, 4);
        _width = room.Width / TileSize;
        StartStrip = width < 32 ? (32 - width) >> 1 : 0;

        // the graphics: the tileset (already in the patterns), the palette, the nametable
        int at = Word(data, 0x0A) + 1;
        data.Slice(at, 16).CopyTo(_palette);
        at += 16;
        for (int row = 0; row < Rows; row++)
        {
            int n = 0;
            while (n < width)
            {
                byte count = Byte(data, at++);
                for (int j = 0; j < (count & 0x7F); j++)
                {
                    byte value = Byte(data, (count & 0x80) != 0 ? at++ : at);
                    if (Gap + n < NametableWidth)
                    {
                        _nametable[row, Gap + n] = value;
                    }

                    n++;
                }

                if ((count & 0x80) == 0)
                {
                    at++;
                }
            }
        }

        // the attributes: a byte for each 4x4 tile block; a room 28 tiles wide skips a row's
        // last 8 after each run that ends a row
        at = Word(data, 0x0C);
        for (int n = 0; n < 64;)
        {
            byte count = Byte(data, at++);
            for (int j = 0; j < (count & 0x7F); j++)
            {
                byte value = Byte(data, (count & 0x80) != 0 ? at++ : at);
                if (n < 64)
                {
                    _attributes[n] = value;
                }

                n++;
            }

            if ((n & 7) == 0 && width == 0x1C)
            {
                n += 8;
            }

            if ((count & 0x80) == 0)
            {
                at++;
            }
        }

        // the mask: whether there is one, its width in bytes, its rows
        at = Word(data, 0x0E);
        _hasMask = Byte(data, at++) != 0;
        if (_hasMask)
        {
            int maskWidth = Byte(data, at++);
            for (int row = 0; row < Rows; row++)
            {
                int n = 0;
                while (n < maskWidth)
                {
                    byte count = Byte(data, at++);
                    for (int j = 0; j < (count & 0x7F); j++)
                    {
                        byte value = Byte(data, (count & 0x80) != 0 ? at++ : at);
                        if (n < MaskBytes)
                        {
                            _mask[row, n] = value;
                        }

                        n++;
                    }

                    if ((count & 0x80) == 0)
                    {
                        at++;
                    }
                }
            }
        }

        _objectNametable = (byte[,])_nametable.Clone();
        _objectAttributes = (byte[])_attributes.Clone();
        _objectMask = (byte[,])_mask.Clone();
    }

    /// <summary>
    /// How many strips a narrow room is moved right by, to the middle of the screen
    /// (<c>_NESStartStrip</c>): the screen is 32 tiles wide, the narrow rooms 28.
    /// </summary>
    public int StartStrip { get; }

    /// <summary>A room's pictures, from its data and the pattern table the loader gave it.</summary>
    public static NesBitmap ForRoom(Room room)
        => room.NesPatterns is { } patterns
            ? new NesBitmap(room, patterns)
            : throw new ArgumentException($"Room {room.Number} is not the NES's, or has no tiles.", nameof(room));

    /// <summary>
    /// The pattern table a room draws with: the tiles every room shares (costume 37) and the
    /// room's tileset after them (costume 37 + the tileset). Mirrors <c>decodeNESBaseTiles</c>
    /// and the start of <c>decodeNESGfx</c>.
    /// </summary>
    public static byte[] Patterns(ReadOnlySpan<byte> room, Func<int, ReadOnlyMemory<byte>?> costume)
    {
        var patterns = new byte[PatternTableSize];
        if (costume(BaseTiles) is not { } baseTiles)
        {
            throw new InvalidDataException("The NES's shared tiles (costume 37) are missing.");
        }

        int shared = DecodeTiles(baseTiles.Span, patterns, 0);
        int tileset = Byte(room, Word(room, 0x0A));
        if (costume(BaseTiles + tileset) is not { } tiles)
        {
            throw new InvalidDataException($"The NES's tileset {tileset} (costume {BaseTiles + tileset}) is missing.");
        }

        DecodeTiles(tiles.Span, patterns, shared * TileBytes);
        return patterns;
    }

    /// <summary>
    /// Decodes a resource of tiles into a pattern table from an offset, and returns how many tiles
    /// it says it holds (0 for 256): its size word, the count, then the tiles' bytes run-length
    /// coded. Mirrors <c>decodeNESTileData</c>, up to the tiles the count names.
    /// </summary>
    public static int DecodeTiles(ReadOnlySpan<byte> resource, Span<byte> patterns, int offset)
    {
        int count = Byte(resource, 2);
        int tiles = count == 0 ? 256 : count;
        int at = 3, n = 0, length = tiles * TileBytes;
        while (n < length && at < resource.Length)
        {
            byte run = resource[at++];
            for (int j = 0; j < (run & 0x7F); j++)
            {
                byte value = Byte(resource, (run & 0x80) != 0 ? at++ : at);
                if (offset + n < patterns.Length)
                {
                    patterns[offset + n] = value;
                }

                n++;
            }

            if ((run & 0x80) == 0)
            {
                at++;
            }
        }

        return count;
    }

    /// <summary>The room's background as wide as the room, in the room's palette, or the dark one when its lights are off.</summary>
    public MaskedImage DrawBackground(bool lit)
    {
        var image = Create(_width, Rows);
        for (int strip = 0; strip < _width; strip++)
        {
            DrawStrip(image, strip, strip, 0, Rows, lit, objects: false);
        }

        return image;
    }

    /// <summary>
    /// An object's image: its tiles, attributes and mask patched into the room's tables where it
    /// lies, then drawn from them. Mirrors <c>decodeNESObject</c> and the strips drawn after it.
    /// </summary>
    public MaskedImage DrawObject(ReadOnlySpan<byte> room, RoomObject obj, bool lit)
    {
        int left = obj.X / TileSize, top = obj.Y / TileSize;
        int columns = obj.Width / TileSize, rows = obj.Height / TileSize;
        Patch(room, obj.ImageOffset, left, top, columns, rows);

        var image = Create(columns, rows);
        for (int column = 0; column < columns; column++)
        {
            DrawStrip(image, column, left + column, top, rows, lit, objects: true);
        }

        return image;
    }

    private void Patch(ReadOnlySpan<byte> room, int at, int left, int top, int columns, int rows)
    {
        // the tiles, row by row
        for (int y = top; y < top + rows; y++)
        {
            int x = left;
            while (x < left + columns)
            {
                byte count = Byte(room, at++);
                for (int i = 0; i < (count & 0x7F); i++)
                {
                    byte value = Byte(room, (count & 0x80) != 0 ? at++ : at);
                    if (y < Rows && Gap + x < NametableWidth)
                    {
                        _objectNametable[y, Gap + x] = value;
                    }

                    x++;
                }

                if ((count & 0x80) == 0)
                {
                    at++;
                }
            }
        }

        // the sub-palettes, a 2-bit field for each 2x2 tile block, four to a byte
        for (int pair = 0, ay = top; pair < rows / 2; pair++, ay += 2)
        {
            int ax = left + Gap, data = 0;
            for (int x = 0; x < columns >> 1; x++, ax += 2)
            {
                if ((x & 3) == 0)
                {
                    data = Byte(room, at++);
                }

                int index = ((ay << 2) & 0x30) | ((ax >> 2) & 0x0F);
                int shift = ((ay & 0x02) != 0 ? 4 : 0) + ((ax & 0x02) != 0 ? 2 : 0);
                _objectAttributes[index] = (byte)((_objectAttributes[index] & ~(3 << shift)) | ((data & 3) << shift));
                data >>= 2;
            }
        }

        // the mask: its first byte, its width, the bits kept at its left and right ends, then its rows
        if (!_hasMask)
        {
            return;
        }

        int maskX = Byte(room, at++), maskWidth = Byte(room, at++);
        byte leftKeep = Byte(room, at++), rightKeep = Byte(room, at++);
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < maskWidth; x++)
            {
                byte value = Byte(room, at++);
                int row = y + top, column = maskX + x;
                if (row >= Rows || column >= MaskBytes)
                {
                    continue;
                }

                byte keep = x == 0 ? leftKeep : x + 1 == maskWidth ? rightKeep : (byte)0;
                _objectMask[row, column] = (byte)((_objectMask[row, column] & keep) | value);
            }
        }
    }

    /// <summary>
    /// One strip of tiles into an image's column: the nametable's tiles in the colours their
    /// block's sub-palette gives; masked where the mask has the strip's bit and the tile's pixel is
    /// not the background colour. Mirrors <c>drawStripNES</c> and <c>drawStripNESMask</c>.
    /// </summary>
    private void DrawStrip(MaskedImage image, int column, int strip, int top, int rows, bool lit, bool objects)
    {
        var palette = lit ? _palette : DarkPalette;
        var nametable = objects ? _objectNametable : _nametable;
        var attributes = objects ? _objectAttributes : _attributes;
        var mask = objects ? _objectMask : _mask;
        int x = strip + Gap;
        if (x >= NametableWidth)
        {
            return;
        }

        for (int y = top; y < top + rows && y < Rows; y++)
        {
            int subPalette = (attributes[((y << 2) & 0x30) | ((x >> 2) & 0x0F)] >> (((y & 2) << 1) | (x & 2))) & 3;
            int tile = nametable[y, x];
            bool masked = _hasMask && strip < MaskBytes * 8 && ((mask[y, strip >> 3] >> (strip & 7)) & 1) != 0;
            for (int line = 0; line < TileSize; line++)
            {
                byte plane0 = _patterns[tile * TileBytes + line];
                byte plane1 = _patterns[tile * TileBytes + line + 8];
                int py = (y - top) * TileSize + line;
                for (int bit = 0; bit < TileSize; bit++)
                {
                    int color = ((plane0 >> (7 - bit)) & 1) | (((plane1 >> (7 - bit)) & 1) << 1);
                    image.Pixels[column * TileSize + bit, py] = palette[color | (subPalette << 2)];
                    image.Mask[column * TileSize + bit, py] = (byte)(masked && color != 0 ? 1 : 0);
                }
            }
        }
    }

    private static MaskedImage Create(int columns, int rows)
        => new(new Surface8(columns * TileSize, rows * TileSize), new Surface8(columns * TileSize, rows * TileSize));

    private static byte Byte(ReadOnlySpan<byte> data, int at)
        => at < data.Length ? data[at] : throw new InvalidDataException($"The NES picture runs past the end of its data, at offset {at}.");

    private static int Word(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
}
