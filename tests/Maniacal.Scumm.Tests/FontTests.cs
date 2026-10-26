using Maniacal.Scumm.Graphics;

namespace Maniacal.Scumm.Tests;

public class FontTests
{
    private const int Apostrophe = 39;

    [Fact]
    public void Glyph_A_is_the_ported_pattern()
    {
        var font = Font.English(2);

        Assert.Equal([0x18, 0x3C, 0x66, 0x7E, 0x66, 0x66, 0x66, 0x00], font.Glyph('A').ToArray());
        Assert.Equal(new byte[8], font.Glyph(' ').ToArray());
    }

    [Fact]
    public void Version_1_has_its_own_apostrophe()
    {
        var v1 = Font.English(1);
        var v1Demo = Font.English(1, demo: true);
        var v2 = Font.English(2);

        Assert.NotEqual(v2.Glyph(Apostrophe).ToArray(), v1.Glyph(Apostrophe).ToArray());
        Assert.Equal([0x30, 0x18, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00], v1.Glyph(Apostrophe).ToArray());
        Assert.Equal(v2.Glyph(Apostrophe).ToArray(), v1Demo.Glyph(Apostrophe).ToArray());

        // nothing else differs
        for (int glyph = 0; glyph < Font.GlyphCount; glyph++)
        {
            if (glyph != Apostrophe)
            {
                Assert.Equal(v2.Glyph(glyph).ToArray(), v1.Glyph(glyph).ToArray());
            }
        }
    }

    [Fact]
    public void Text_is_drawn_8_pixels_a_character_in_its_colour()
    {
        var font = Font.English(2);
        var surface = new Surface8(24, 8);
        byte[] text = "AA."u8.ToArray();

        int end = font.DrawText(surface, text, 0, 0, 14);

        Assert.Equal(24, end);
        Assert.Equal(24, Font.Width(text));
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                bool set = (font.Glyph('A')[y] & (0x80 >> (x % 8))) != 0;
                Assert.Equal(set ? 14 : 0, surface[x, y]);
            }
        }
    }

    [Fact]
    public void Drawing_clips_to_the_surface()
    {
        var surface = new Surface8(4, 4);

        Font.English(2).DrawText(surface, "W"u8, -2, -3, 15);

        Assert.Contains(surface.Pixels, pixel => pixel == 15);
    }

    [Fact]
    public void Control_codes_are_not_glyphs()
    {
        var surface = new Surface8(8, 8);

        Assert.Throws<ArgumentOutOfRangeException>(() => Font.English(2).DrawText(surface, [0x80], 0, 0, 15));
    }
}
