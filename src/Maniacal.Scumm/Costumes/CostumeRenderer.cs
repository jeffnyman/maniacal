using Daventry.Core.Graphics;
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

    /// <summary>
    /// Draws one cel. Version 1 colours depend on the actor showing the costume; for the kids and
    /// most characters the actor number is the costume number.
    /// </summary>
    public static Surface8 DrawCel(Costume costume, CostumeCel cel, int actor, bool mirror = false)
    {
        var surface = new Surface8(Math.Max(cel.Width, 1), Math.Max(cel.Height, 1));
        surface.Fill(Transparent);
        if (cel.Width == 0 || cel.Height == 0)
        {
            return surface;
        }

        if (costume.IsVersion1)
        {
            DrawV1(costume.Data.Span, cel, V1Palette(actor), surface, mirror);
        }
        else
        {
            DrawV2(costume.Data.Span, cel, V2Palette(costume), surface, mirror);
        }

        return surface;
    }

    /// <summary>
    /// Draws the figure the given animation frames build, applied in order (an actor starts with
    /// the init frame, then the frame it shows): each frame sets the limbs it lists, and the others
    /// keep what an earlier frame gave them. Each limb shows the command <paramref name="step"/>
    /// steps into its run. Facing left mirrors facing right unless the costume has its own left
    /// pictures. Mirrors <c>costumeDecodeData</c>, <c>increaseAnim</c> and <c>drawLimb</c>.
    /// </summary>
    public static CostumeFrame DrawFrame(Costume costume, int direction, IEnumerable<int> frames, int actor, int step = 0)
    {
        var limbs = new Dictionary<int, LimbAnimation>();
        foreach (int frame in frames)
        {
            foreach (var limb in costume.Animation(frame * 4 + direction))
            {
                if (limb.Start is not { } start)
                {
                    limbs.Remove(limb.Limb);
                }
                else if (costume.Command(start) is not (Costume.Stop or Costume.Start))
                {
                    limbs[limb.Limb] = limb;
                }
            }
        }

        bool toRight = direction != Left || costume.HasOwnLeftPictures;
        var parts = new List<(Surface8 Image, int X, int Y)>();
        // version 2 cel offsets count from 72 pixels left of and 100 above the actor's feet
        // (BaseCostumeRenderer::drawCostume sets _xMove and _yMove so for GF_OLD_BUNDLE)
        int moveX = costume.IsVersion1 ? 0 : -72;
        int moveY = costume.IsVersion1 ? 0 : -100;
        for (int limb = 0; limb < costume.LimbCount; limb++)
        {
            if (!limbs.TryGetValue(limb, out var animation))
            {
                continue;
            }

            int code = costume.Command(Advance(costume, animation, step)) & 0x7F;
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
            parts.Add((DrawCel(costume, cel, actor, !toRight), left, y));
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

    /// <summary>The command a limb shows after some steps: runs loop or stop at their end, and sound and counter commands are passed over.</summary>
    private static int Advance(Costume costume, LimbAnimation animation, int steps)
    {
        int start = animation.Start!.Value;
        int end = start + animation.Length;
        int position = start;
        for (int i = 0; i < steps; i++)
        {
            // bounded, in case a run holds nothing but sound and counter commands
            for (int guard = 0; guard <= animation.Length; guard++)
            {
                if (animation.Loop)
                {
                    position = position >= end ? start : position + 1;
                }
                else if (position != end)
                {
                    position++;
                }

                if (costume.Command(position) is not (Costume.Sound or Costume.Counter) || start == end)
                {
                    break;
                }
            }
        }

        return position;
    }

    private static byte[] V1Palette(int actor)
    {
        var map = Palettes.ManiacV1ToEga;
        int index = Math.Clamp(actor, 0, V1SkinColors.Length - 1);
        return [Transparent, map[V1SkinColors[index]], map[V1ClothesColors[index]], 0];
    }

    private static byte[] V2Palette(Costume costume)
    {
        var palette = new byte[16];
        for (int i = 0; i < palette.Length; i++)
        {
            palette[i] = (byte)i;
        }

        palette[costume.ColorIndex & 0x0F] = palette[0];
        return palette;
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
    /// Pixel 0 is transparent; 1 and 2 are the actor's colours and 3 black.
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
                    if (pixel == 0)
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
