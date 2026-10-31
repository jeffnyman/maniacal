using Maniacal.Scumm.Graphics;

namespace Maniacal.Scumm.Costumes;

/// <summary>A drawn figure: its pixels, and where in them the actor's position is.</summary>
public sealed record CostumeFrame(Surface8 Pixels, int OriginX, int OriginY);

/// <summary>
/// Draws costume cels and whole figures in EGA colour numbers, with <see cref="Transparent"/> where
/// nothing is drawn. Mirrors ScummVM's <c>ClassicCostumeRenderer</c>: <c>drawLimb</c>,
/// <c>byleRLEDecode</c> (version 2), <c>byleRLEDecode_C64</c> (version 1) and <c>setPalette</c>.
/// </summary>
public static class CostumeRenderer
{
    public const byte Transparent = 0xFF;

    /// <summary>Directions, as ScummVM numbers them for animations (<c>newDirToOldDir</c>).</summary>
    public const int Left = 0, Right = 1, Front = 2, Back = 3;

    /// <summary>The animation frames of version 1 and 2 actors. Mirrors <c>Actor_v2::initActor</c>.</summary>
    public const int WalkFrame = 0, StandFrame = 1, InitFrame = 2, TalkStopFrame = 4, TalkStartFrame = 5;

    // version 1 costumes take colours 1 and 2 from the actor; C64 colour numbers.
    // Mirrors v1MMActorPalatte1 (skin) and v1MMActorPalatte2 (clothes) in costume.cpp
    private static readonly byte[] V1SkinColors = [8, 8, 8, 8, 4, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8];
    private static readonly byte[] V1ClothesColors = [0, 7, 2, 6, 9, 1, 3, 7, 7, 1, 1, 9, 1, 4, 5, 5, 4, 1, 0, 5, 4, 2, 2, 7, 7];

    private static readonly byte[] IdentityPalette = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15];

    /// <summary>
    /// The colours a costume is drawn with. Version 1 takes them from the actor number (for the kids
    /// and most characters the costume number); version 2 from the actor's 16-entry palette, or
    /// all grey but black in the dark. Mirrors <c>ClassicCostumeRenderer::setPalette</c> and the palette of <c>byleRLEDecode_C64</c>.
    /// </summary>
    public static byte[] Palette(Costume costume, int actor, ReadOnlySpan<byte> actorPalette = default, bool actorColors = true)
    {
        if (costume.IsVersion1)
        {
            var map = Palettes.ManiacV1ToEga;
            int index = Math.Clamp(actor, 0, V1SkinColors.Length - 1);
            return actorColors
                ? [Transparent, map[V1SkinColors[index]], map[V1ClothesColors[index]], 0]
                : [Transparent, 0, map[11], map[11]];
        }

        var palette = new byte[16];
        if (actorColors)
        {
            (actorPalette.IsEmpty ? IdentityPalette : actorPalette)[..16].CopyTo(palette);
            palette[12] = 0x0C;
        }
        else
        {
            Array.Fill(palette, (byte)8);
            palette[12] = 0;
        }

        palette[costume.ColorIndex & 0x0F] = palette[0];
        return palette;
    }

    /// <summary>Draws one cel, with <see cref="Palette"/>'s colours for the actor.</summary>
    internal static Surface8 DrawCel(Costume costume, CostumeCel cel, int actor, bool mirror = false)
        => DrawCel(costume, cel, Palette(costume, actor), mirror);

    internal static Surface8 DrawCel(Costume costume, CostumeCel cel, byte[] palette, bool mirror)
    {
        var surface = new Surface8(Math.Max(cel.Width, 1), Math.Max(cel.Height, 1));
        surface.Fill(Transparent);
        if (cel.Width == 0 || cel.Height == 0)
        {
            return surface;
        }

        if (costume.IsVersion1)
        {
            DrawV1(costume.Data.Span, cel, palette, surface, mirror);
        }
        else
        {
            DrawV2(costume.Data.Span, cel, palette, surface, mirror);
        }

        return surface;
    }

    /// <summary>
    /// Draws the figure a costume state shows: each limb with a command that is not stopped, in
    /// limb order, every cel placed from where the one before left the running position. Facing
    /// left mirrors facing right unless the costume has its own left pictures. Mirrors
    /// <c>BaseCostumeRenderer::drawCostume</c> and <c>ClassicCostumeRenderer::drawLimb</c>.
    /// </summary>
    internal static CostumeFrame DrawLimbs(Costume costume, CostumeState state, int direction, byte[] palette)
    {
        bool toRight = direction != Left || costume.HasOwnLeftPictures;
        var parts = new List<(Surface8 Image, int X, int Y)>();

        // version 2 cel offsets count from 72 pixels left of and 100 above the actor's feet
        // (BaseCostumeRenderer::drawCostume sets _xMove and _yMove so for GF_OLD_BUNDLE)
        int moveX = costume.IsVersion1 ? 0 : -72;
        int moveY = costume.IsVersion1 ? 0 : -100;
        for (int limb = 0; limb < costume.LimbCount; limb++)
        {
            int position = state.Position(limb);
            if (position == CostumeState.None || state.IsStopped(limb))
            {
                continue;
            }

            int code = costume.Command(position & ~CostumeState.NoLoop) & 0x7F;
            if (code == Costume.Hide)
            {
                continue;
            }

            var cel = costume.Cel(limb, code);
            int x = moveX + cel.RelX;
            int y = moveY + cel.RelY;
            moveX += cel.MoveX;
            moveY -= cel.MoveY;

            int left = toRight ? x : -x - cel.Width;
            parts.Add((DrawCel(costume, cel, palette, !toRight), left, y));
        }

        if (parts.Count == 0)
        {
            var empty = new Surface8(1, 1);
            empty.Fill(Transparent);
            return new CostumeFrame(empty, 0, 0);
        }

        int minX = parts.Min(p => p.X);
        int minY = parts.Min(p => p.Y);
        int width = parts.Max(p => p.X + p.Image.Width) - minX;
        int height = parts.Max(p => p.Y + p.Image.Height) - minY;
        var pixels = new Surface8(width, height);
        pixels.Fill(Transparent);
        foreach (var (image, px, py) in parts)
        {
            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    if (image[x, y] != Transparent)
                    {
                        pixels[px - minX + x, py - minY + y] = image[x, y];
                    }
                }
            }
        }

        return new CostumeFrame(pixels, -minX, -minY);
    }

    /// <summary>
    /// Draws the figure the given animation frames build, started in order on a fresh state, after
    /// <paramref name="step"/> animation steps. For looking at a costume; actors keep their own state.
    /// </summary>
    public static CostumeFrame DrawFrame(Costume costume, int direction, IEnumerable<int> frames, int actor, int step = 0)
    {
        var state = new CostumeState();
        foreach (int frame in frames)
        {
            state.Decode(costume, frame * 4 + direction);
        }

        for (int i = 0; i < step; i++)
        {
            state.Increase(costume);
        }

        return DrawLimbs(costume, state, direction, Palette(costume, actor));
    }

    /// <summary>
    /// Version 2: a byte is a colour in its high 4 bits and a run length in its low 4 (0: the
    /// length is the next byte). Runs fill columns top to bottom and carry on into the next column;
    /// colour 0 is transparent.
    /// </summary>
    private static void DrawV2(ReadOnlySpan<byte> data, CostumeCel cel, byte[] palette, Surface8 surface, bool mirror)
    {
        int position = cel.DataOffset;
        int x = 0;
        int y = 0;
        while (x < cel.Width)
        {
            byte value = Next(data, ref position);
            int color = value >> 4;
            int length = value & 0x0F;
            if (length == 0)
            {
                length = Next(data, ref position);
            }

            for (; length > 0 && x < cel.Width; length--)
            {
                if (color != 0)
                {
                    surface[mirror ? cel.Width - 1 - x : x, y] = palette[color];
                }

                if (++y == cel.Height)
                {
                    y = 0;
                    x++;
                }
            }
        }
    }

    /// <summary>
    /// Version 1: columns 8 pixels wide, a byte being one row of four 2-bit pixels each drawn
    /// 2 wide. A length byte with bit 7 set repeats the next byte, otherwise that many bytes follow.
    /// Pixel 0 is transparent; 1 to 3 take the palette's colours.
    /// </summary>
    private static void DrawV1(ReadOnlySpan<byte> data, CostumeCel cel, byte[] palette, Surface8 surface, bool mirror)
    {
        int position = cel.DataOffset;
        int columns = cel.Width / 8;
        int column = 0;
        int y = 0;
        while (column < columns)
        {
            int length = Next(data, ref position);
            bool repeat = (length & 0x80) != 0;
            byte value = repeat ? Next(data, ref position) : (byte)0;
            for (length &= 0x7F; length > 0 && column < columns; length--)
            {
                if (!repeat)
                {
                    value = Next(data, ref position);
                }

                for (int pair = 0; pair < 4; pair++)
                {
                    int pixel = (value >> (6 - 2 * pair)) & 3;
                    if (pixel == 0 || palette[pixel] == Transparent)
                    {
                        continue;
                    }

                    int x = column * 8 + 2 * pair;
                    surface[mirror ? cel.Width - 1 - x : x, y] = palette[pixel];
                    surface[mirror ? cel.Width - 2 - x : x + 1, y] = palette[pixel];
                }

                if (++y == cel.Height)
                {
                    y = 0;
                    column++;
                }
            }
        }
    }

    private static byte Next(ReadOnlySpan<byte> data, ref int position)
    {
        if (position >= data.Length)
        {
            throw new InvalidDataException($"A costume cel runs past the end of the costume, at offset {position}.");
        }

        return data[position++];
    }
}
