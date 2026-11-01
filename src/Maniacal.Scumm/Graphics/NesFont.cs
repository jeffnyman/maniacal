using System.Buffers.Binary;
using Maniacal.Scumm.Rooms;

namespace Maniacal.Scumm.Graphics;

/// <summary>Draws a font's glyphs: the version 1 and 2 interpreters' own, or the NES's.</summary>
internal interface IGlyphs
{
    /// <summary>Draws one glyph with its top left at (x, y), clipped to the surface.</summary>
    void DrawGlyph(Surface8 surface, int glyph, int x, int y, byte color);
}

/// <summary>
/// The NES's font: its characters are tiles of the room graphics' pattern table, which a
/// translation table (costume 77) finds for each character from 32 on, every one 8 by 8. A
/// character is drawn whole, its background too, in the room's palette: the third sub-palette for
/// colour 0, the fourth for any other. Mirrors <c>CharsetRendererNES</c> (<c>printChar</c>,
/// <c>drawBits1</c>).
/// </summary>
internal sealed class NesFont(ReadOnlyMemory<byte> translation, Func<Room?> room) : IGlyphs
{
    private const int TileBytes = 16, Size = 8, FirstCharacter = 32;

    // the room last drawn in: ScummVM keeps the last room's palette and tiles (_NESPalette[0], _NESPatTable[1])
    private byte[]? _patterns;
    private byte[] _palette = new byte[16];

    /// <summary>The font of a cartridge, its translation table from costume 77, its size word first.</summary>
    public static NesFont Load(ReadOnlyMemory<byte> costume77, Func<Room?> room) => new(costume77[2..], room);

    public void DrawGlyph(Surface8 surface, int glyph, int x, int y, byte color)
    {
        if (room() is { NesPatterns: { } patterns } current)
        {
            _patterns = patterns;
            _palette = NesBitmap.RoomPalette(current.Data.Span);
        }

        if (_patterns is null || glyph < FirstCharacter || glyph - FirstCharacter >= translation.Length)
        {
            return;
        }

        int tile = translation.Span[glyph - FirstCharacter];
        int subPalette = color != 0 ? 12 : 8;
        for (int row = 0; row < Size; row++)
        {
            byte plane0 = _patterns[tile * TileBytes + row];
            byte plane1 = _patterns[tile * TileBytes + row + 8];
            int py = y + row;
            if (py < 0 || py >= surface.Height)
            {
                continue;
            }

            for (int bit = 0; bit < Size; bit++)
            {
                int px = x + bit;
                if (px >= 0 && px < surface.Width)
                {
                    surface[px, py] = _palette[((plane0 >> (7 - bit)) & 1) | (((plane1 >> (7 - bit)) & 1) << 1) | subPalette];
                }
            }
        }
    }
}
