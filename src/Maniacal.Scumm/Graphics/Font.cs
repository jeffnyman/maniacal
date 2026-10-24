using Daventry.Core.Graphics;

namespace Maniacal.Scumm.Graphics;

/// <summary>
/// The font built into the version 1 and 2 interpreters: 128 glyphs of 8x8 pixels, one bit per
/// pixel with bit 7 leftmost, every character 8 pixels wide. Text bytes are glyph numbers. Mirrors
/// ScummVM's <c>CharsetRendererV2</c> (the English font) and <c>CharsetRendererV3::drawBits1</c>.
/// </summary>
public sealed class Font
{
    public const int GlyphCount = 128;
    public const int CharWidth = 8;
    public const int Height = 8;

    private readonly byte[] _glyphs;

    private Font(byte[] glyphs) => _glyphs = glyphs;

    /// <summary>
    /// The English font of a release. Version 1, except its demo, has a different apostrophe:
    /// glyph 39 comes from the special glyphs (<c>englishCharsetDataV1</c>).
    /// </summary>
    public static Font English(int version, bool demo = false)
    {
        var glyphs = FontData.EnglishV2.ToArray();
        if (version == 1 && !demo)
        {
            FontData.Special.Slice(2 * Height, Height).CopyTo(glyphs.AsSpan(39 * Height, Height));
        }

        return new Font(glyphs);
    }

    /// <summary>The 8 rows of a glyph.</summary>
    public ReadOnlySpan<byte> Glyph(int glyph)
    {
        if (glyph is < 0 or >= GlyphCount)
        {
            throw new ArgumentOutOfRangeException(nameof(glyph), glyph, $"The font has glyphs 0 to {GlyphCount - 1}.");
        }

        return _glyphs.AsSpan(glyph * Height, Height);
    }

    public static int Width(ReadOnlySpan<byte> text) => text.Length * CharWidth;

    /// <summary>Draws one glyph with its top left at (x, y); only the set bits are drawn, clipped to the surface.</summary>
    public void DrawGlyph(Surface8 surface, int glyph, int x, int y, byte color)
    {
        var rows = Glyph(glyph);
        for (int row = 0; row < Height; row++)
        {
            int py = y + row;
            if (py < 0 || py >= surface.Height)
            {
                continue;
            }

            for (int bit = 0; bit < CharWidth; bit++)
            {
                int px = x + bit;
                if ((rows[row] & (0x80 >> bit)) != 0 && px >= 0 && px < surface.Width)
                {
                    surface[px, py] = color;
                }
            }
        }
    }

    /// <summary>
    /// Draws a line of text, a glyph per byte; returns the x just past it. Control codes (bytes of
    /// 128 and up, and the message escapes) are the caller's to handle before drawing.
    /// </summary>
    public int DrawText(Surface8 surface, ReadOnlySpan<byte> text, int x, int y, byte color)
    {
        foreach (byte glyph in text)
        {
            DrawGlyph(surface, glyph, x, y, color);
            x += CharWidth;
        }

        return x;
    }
}
