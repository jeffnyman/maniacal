using Maniacal.Scumm.Graphics;

namespace Maniacal.Scumm.Costumes;

/// <summary>
/// Draws a C64 actor's limbs in C64 colour numbers, with <see cref="CostumeRenderer.Transparent"/>
/// where nothing is drawn. Mirrors ScummVM's <c>V0CostumeRenderer::drawLimb</c> and its palette.
/// </summary>
public static class V0CostumeRenderer
{
    /// <summary>Half an actor's width, which a limb's offset counts from (<c>a->_width / 2</c>, of <c>initActor</c>'s 24).</summary>
    public const int HalfWidth = 12;

    /// <summary>
    /// The colours of a cel's pixels 1 to 3. Lit actors in colour are 10 (skin), their own colour and
    /// black; in the dark the light grey 11, with their own colour where the flashlight shines.
    /// Mirrors the palette of <c>drawLimb</c>.
    /// </summary>
    public static byte[] Palette(int actor, bool actorColors, bool flashlight)
    {
        byte own = V0ActorColor(actor);
        return actorColors
            ? [CostumeRenderer.Transparent, 10, own, 0]
            : [CostumeRenderer.Transparent, 0, flashlight ? own : (byte)11, 11];
    }

    /// <summary>
    /// The figure the limbs show, from limb 0 up, each cel placed from where the one before left the
    /// running position, a mirrored limb drawn leftward from the other side. The origin is the
    /// actor's point: 12 pixels right of its column, on its line. A pixel is drawn 2 wide; colour 0
    /// is transparent. Mirrors <c>V0CostumeRenderer::drawLimb</c>.
    /// </summary>
    public static CostumeFrame DrawLimbs(V0Costume costume, V0CostumeState state, byte[] palette)
    {
        var data = costume.Data.Span;
        var parts = new List<(V0Cel Cel, bool Flipped, int Left, int Top)>();
        int moveX = 0, moveY = 0;
        for (int limb = 0; limb < V0Costume.Limbs; limb++)
        {
            if (state.Position(limb) == V0CostumeState.None)
            {
                continue;
            }

            var cel = costume.Cel(costume.Sequence(state.Position(limb) + state.AnimType(limb)));
            int offsetX = moveX + cel.OffsetX;
            int offsetY = moveY + cel.OffsetY;
            moveX += cel.MoveX;
            moveY += cel.MoveY;
            if (cel.Columns == 0 || cel.Height == 0)
            {
                continue;
            }

            bool flipped = state.Flipped(limb);
            int x = (flipped ? -1 : 1) * (offsetX * 8 - HalfWidth);

            // ScummVM: "+1 as we appear to be 1 pixel away from the original interpreter"
            int y = -offsetY + 1;
            parts.Add((cel, flipped, flipped ? x - cel.Columns * 8 : x, y));
        }

        if (parts.Count == 0)
        {
            var empty = new Surface8(1, 1);
            empty.Fill(CostumeRenderer.Transparent);
            return new CostumeFrame(empty, 0, 0);
        }

        int minX = parts.Min(p => p.Left);
        int minY = parts.Min(p => p.Top);
        int width = parts.Max(p => p.Left + p.Cel.Columns * 8) - minX;
        int height = parts.Max(p => p.Top + p.Cel.Height) - minY;
        var pixels = new Surface8(width, height);
        pixels.Fill(CostumeRenderer.Transparent);
        foreach (var (cel, flipped, left, top) in parts)
        {
            for (int y = 0; y < cel.Height; y++)
            {
                for (int column = 0; column < cel.Columns; column++)
                {
                    byte value = data[cel.DataOffset + y * cel.Columns + column];
                    int x = (flipped ? cel.Columns - 1 - column : column) * 8 + left - minX;
                    for (int pair = 0; pair < 4; pair++)
                    {
                        // a mirrored limb takes its pairs from the low bits up
                        int pixel = (value >> (flipped ? 2 * pair : 6 - 2 * pair)) & 3;
                        if (pixel != 0)
                        {
                            pixels[x + 2 * pair, top - minY + y] = palette[pixel];
                            pixels[x + 2 * pair + 1, top - minY + y] = palette[pixel];
                        }
                    }
                }
            }
        }

        return new CostumeFrame(pixels, -minX, -minY);
    }

    /// <summary>
    /// Draws the figure a clip shows, started on a fresh state and held, after
    /// <paramref name="step"/> more animation steps. For looking at a costume; actors keep their own state.
    /// </summary>
    public static CostumeFrame DrawClip(V0Costume costume, int clip, int actor, int step = 0)
    {
        var state = new V0CostumeState { NewClip = clip, Repeat = -1 };
        state.Decode(costume);
        for (int i = 0; i <= step; i++)
        {
            state.Increase(costume);
        }

        return DrawLimbs(costume, state, Palette(actor, actorColors: true, flashlight: false));
    }

    /// <summary>How many frames the longest of a clip's limb sequences has: the steps of its cycle.</summary>
    public static int ClipLength(V0Costume costume, int clip)
    {
        int longest = 1;
        for (int limb = 0; limb < V0Costume.Limbs; limb++)
        {
            byte sequence = costume.Clip(clip, limb);
            if (sequence == V0Costume.End)
            {
                continue;
            }

            int start = costume.Sequence(costume.Sequence(limb) + (sequence & 0x7F));
            int length = 0;
            while (costume.Sequence(start + length) != V0Costume.End)
            {
                length++;
            }

            longest = Math.Max(longest, length);
        }

        return longest;
    }

    private static byte V0ActorColor(int actor) => actor >= 0 && actor < Game.V0Actors.CostumeColors.Length ? Game.V0Actors.CostumeColors[actor] : (byte)0;
}
