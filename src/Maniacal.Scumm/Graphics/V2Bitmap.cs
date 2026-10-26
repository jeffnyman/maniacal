
namespace Maniacal.Scumm.Graphics;

/// <summary>A decoded picture and its mask: in the mask, 1 marks a pixel drawn in front of actors.</summary>
public sealed record MaskedImage(Surface8 Pixels, Surface8 Mask);

/// <summary>
/// The picture format of version 2 rooms and objects: one run-length encoded stream of 4-bit
/// colours, column by column from the top, followed by the mask. Mirrors ScummVM's
/// <c>GdiV2::prepareDrawBitmap</c> and <c>GdiV2::generateStripTable</c>, decoding the whole
/// picture at once instead of strip by strip.
/// </summary>
/// <remarks>
/// <para>Picture: a byte with bit 7 clear is a run of <c>byte &gt;&gt; 4</c> pixels of colour
/// <c>byte &amp; 0x0F</c>; with bit 7 set it is a "dither" run of <c>byte &amp; 0x7F</c> pixels,
/// each repeating the colour of the same row in the previous column. A run length of 0 means the
/// length is in the next byte. Runs continue from one column into the next.</para>
/// <para>Mask: one byte per row for each 8-pixel strip, strip by strip, bit 7 the leftmost pixel.
/// A length byte with bit 7 set repeats the following byte <c>length &amp; 0x7F</c> times; with
/// bit 7 clear it is followed by that many literal bytes.</para>
/// </remarks>
public static class V2Bitmap
{
    /// <summary>Decodes a picture and its mask; <paramref name="end"/> is the offset just past the mask.</summary>
    public static MaskedImage Decode(ReadOnlySpan<byte> data, int width, int height, out int end)
    {
        if (width <= 0 || height <= 0 || width % 8 != 0)
        {
            throw new ArgumentException($"A version 2 picture is a whole number of 8-pixel strips, not {width}x{height}.");
        }

        var reader = new Reader(data);
        var pixels = DecodePicture(ref reader, width, height);
        var mask = DecodeMask(ref reader, width, height);
        end = reader.Position;
        return new MaskedImage(pixels, mask);
    }

    private static Surface8 DecodePicture(ref Reader reader, int width, int height)
    {
        var surface = new Surface8(width, height);
        var previousColumn = new byte[height];
        int run = 1;
        byte color = 0;
        bool dither = false;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (--run == 0)
                {
                    byte data = reader.Next();
                    if ((data & 0x80) != 0)
                    {
                        run = data & 0x7F;
                        dither = true;
                    }
                    else
                    {
                        run = data >> 4;
                        dither = false;
                    }

                    color = (byte)(data & 0x0F);
                    if (run == 0)
                    {
                        run = reader.Next();
                    }
                }

                if (!dither)
                {
                    previousColumn[y] = color;
                }

                surface[x, y] = previousColumn[y];
            }
        }

        return surface;
    }

    private static Surface8 DecodeMask(ref Reader reader, int width, int height)
    {
        var mask = new Surface8(width, height);
        int strips = width / 8;
        int strip = 0;
        int y = 0;

        while (strip < strips)
        {
            int length = reader.Next();
            bool repeat = (length & 0x80) != 0;
            byte data = 0;
            if (repeat)
            {
                length &= 0x7F;
                data = reader.Next();
            }

            do
            {
                if (!repeat)
                {
                    data = reader.Next();
                }

                for (int bit = 0; bit < 8; bit++)
                {
                    mask[strip * 8 + bit, y] = (byte)((data >> (7 - bit)) & 1);
                }

                if (++y == height)
                {
                    y = 0;
                    if (++strip == strips)
                    {
                        break;
                    }
                }
            }
            while (--length > 0);
        }

        return mask;
    }

    private ref struct Reader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;

        public int Position { get; private set; }

        public byte Next()
        {
            if (Position >= _data.Length)
            {
                throw new InvalidDataException($"The picture data ends early, at offset {Position}.");
            }

            return _data[Position++];
        }
    }
}
