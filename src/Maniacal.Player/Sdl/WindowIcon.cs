using System.Buffers.Binary;
using SDL;
using static SDL.SDL3;

namespace Maniacal.Player.Sdl;

/// <summary>
/// The window's icon: the house, <c>maniac-mansion-house.ico</c> in this project, embedded in
/// the player. Its 32-bit images are read from the icon file (each a bitmap of blue, green, red and
/// alpha, bottom row first) and given to SDL, the largest as the icon and the others as its
/// smaller sizes, so the system can pick the one it shows.
/// </summary>
internal static unsafe class WindowIcon
{
    private const string Resource = "Maniacal.Player.maniac-mansion-house.ico";

    /// <summary>An icon image: its size and its pixels, 0xAARRGGBB, top row first.</summary>
    internal sealed record Image(int Width, int Height, uint[] Pixels);

    /// <summary>Sets the window's icon; a window without one is no failure, so nothing is thrown.</summary>
    public static void Set(SDL_Window* window)
    {
        using var stream = typeof(WindowIcon).Assembly.GetManifestResourceStream(Resource);
        if (stream is null)
        {
            return;
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var images = Read(memory.ToArray()).OrderByDescending(i => i.Width).ToList();
        if (images.Count == 0)
        {
            return;
        }

        var icon = Surface(images[0]);
        if (icon is null)
        {
            return;
        }

        foreach (var smaller in images.Skip(1))
        {
            var alternate = Surface(smaller);
            if (alternate is not null)
            {
                SDL_AddSurfaceAlternateImage(icon, alternate);
                SDL_DestroySurface(alternate);
            }
        }

        SDL_SetWindowIcon(window, icon);
        SDL_DestroySurface(icon);
    }

    /// <summary>
    /// The icon file's 32-bit bitmap images, one of each size. Its other images (fewer colours,
    /// or a PNG) are left out: the 32-bit ones come in every size the system asks for.
    /// </summary>
    internal static List<Image> Read(byte[] ico)
    {
        var images = new List<Image>();
        if (ico.Length < 6 || BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(2)) != 1)
        {
            return images;
        }

        int count = BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4));
        for (int i = 0; i < count && 6 + 16 * (i + 1) <= ico.Length; i++)
        {
            var entry = ico.AsSpan(6 + 16 * i, 16);
            int size = BinaryPrimitives.ReadInt32LittleEndian(entry[8..]);
            int offset = BinaryPrimitives.ReadInt32LittleEndian(entry[12..]);
            if (offset < 0 || size < 40 || offset + size > ico.Length || ReadBitmap(ico.AsSpan(offset, size)) is not { } image)
            {
                continue;
            }

            if (!images.Exists(other => other.Width == image.Width))
            {
                images.Add(image);
            }
        }

        return images;
    }

    // a BITMAPINFOHEADER and its pixels, 32 bits each, the height counting the mask below them
    private static Image? ReadBitmap(ReadOnlySpan<byte> data)
    {
        int header = BinaryPrimitives.ReadInt32LittleEndian(data);
        int width = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
        int height = BinaryPrimitives.ReadInt32LittleEndian(data[8..]) / 2;
        int bits = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]);
        int compression = BinaryPrimitives.ReadInt32LittleEndian(data[16..]);
        if (header != 40 || bits != 32 || compression != 0 || width <= 0 || height <= 0 || header + width * height * 4 > data.Length)
        {
            return null;
        }

        var pixels = new uint[width * height];
        for (int y = 0; y < height; y++)
        {
            var row = data.Slice(header + (height - 1 - y) * width * 4, width * 4);
            for (int x = 0; x < width; x++)
            {
                pixels[y * width + x] = BinaryPrimitives.ReadUInt32LittleEndian(row[(x * 4)..]);
            }
        }

        return new Image(width, height, pixels);
    }

    private static SDL_Surface* Surface(Image image)
    {
        var surface = SDL_CreateSurface(image.Width, image.Height, SDL_PixelFormat.SDL_PIXELFORMAT_ARGB8888);
        if (surface is null)
        {
            return null;
        }

        for (int y = 0; y < image.Height; y++)
        {
            var row = new Span<uint>((byte*)surface->pixels + y * surface->pitch, image.Width);
            image.Pixels.AsSpan(y * image.Width, image.Width).CopyTo(row);
        }

        return surface;
    }
}
