using System.Buffers.Binary;
using Maniacal.Scumm.Graphics;

namespace Maniacal.Scumm.Costumes;

/// <summary>
/// A costume of the NES: its animations, each a list of frames, the frames themselves kept in the
/// costume set (<see cref="NesCostumeSet"/>). An animation is <c>frame * 4 + direction</c>, as in
/// the other releases, with one limb. Mirrors <c>NESCostumeLoader</c>.
/// </summary>
/// <remarks>
/// After the size word, a pair of bytes for each animation: where its frame list starts, counted
/// from the same place, and how many frames it has.
/// </remarks>
internal sealed class NesCostume
{
    /// <summary>The last animation a costume has (<c>_numAnim</c>, 0x17).</summary>
    public const int LastAnimation = 0x17;

    private readonly ReadOnlyMemory<byte> _data;

    private NesCostume(int number, ReadOnlyMemory<byte> data)
    {
        Number = number;
        _data = data;
    }

    /// <summary>The costume's number, which finds its frames in the set (<see cref="NesCostumeTables.Lookup"/>).</summary>
    public int Number { get; }

    /// <summary>A costume from its resource, its size word first.</summary>
    public static NesCostume Parse(int number, ReadOnlyMemory<byte> resource)
        => resource.Length >= 2 + 2 * (LastAnimation + 1)
            ? new NesCostume(number, resource[2..])
            : throw new InvalidDataException($"NES costume {number} is too short ({resource.Length} bytes).");

    /// <summary>How many frames an animation has. Mirrors the <c>_dataOffsets[2 * anim + 1]</c> of <c>costumeDecodeData</c>.</summary>
    public int FrameCount(int animation) => Byte(2 * animation + 1);

    /// <summary>The set frame an animation shows at a position in its list. Mirrors <c>src[src[2 * anim] + frameNum]</c> of <c>drawLimb</c>.</summary>
    public int Frame(int animation, int position) => Byte(Byte(2 * animation) + position);

    private byte Byte(int at)
        => at < _data.Length ? _data.Span[at] : throw new InvalidDataException($"NES costume {Number} is read past its end, at {at}.");
}

/// <summary>
/// One of the NES's two costume sets, the kids' or the rest: the frames' sprite lists, the tiles
/// they draw and the 16 colours they draw in, from the costume slots of
/// <see cref="NesCostumeTables.Sets"/>. Mirrors <c>NES_loadCostumeSet</c>.
/// </summary>
internal sealed class NesCostumeSet
{
    private readonly ReadOnlyMemory<byte> _descriptions, _lengths, _offsets, _data;

    private NesCostumeSet(int number, ReadOnlyMemory<byte> descriptions, ReadOnlyMemory<byte> lengths, ReadOnlyMemory<byte> offsets, ReadOnlyMemory<byte> data, byte[] patterns, byte[] palette)
    {
        Number = number;
        _descriptions = descriptions;
        _lengths = lengths;
        _offsets = offsets;
        _data = data;
        Patterns = patterns;
        Palette = palette;
    }

    /// <summary>0 or 1.</summary>
    public int Number { get; }

    /// <summary>The tiles the sprites draw (<c>_NESPatTable[0]</c>).</summary>
    public byte[] Patterns { get; }

    /// <summary>The 16 colours, four sub-palettes of four (<c>_NESPalette[1]</c>).</summary>
    public byte[] Palette { get; }

    /// <summary>A set from its six costume slots, each read with its size word first.</summary>
    public static NesCostumeSet Load(int number, Func<int, ReadOnlyMemory<byte>?> costume)
    {
        var slots = NesCostumeTables.Sets[number];
        ReadOnlyMemory<byte> Part(int i) => costume(slots[i]) ?? throw new InvalidDataException($"The NES's costume set {number} lacks its part in costume {slots[i]}.");

        var patterns = new byte[NesBitmap.PatternTableSize];
        NesBitmap.DecodeTiles(Part(4).Span, patterns, 0);
        var palette = Part(5)[2..].Span[..16].ToArray();
        return new NesCostumeSet(number, Part(0)[2..], Part(1)[2..], Part(2)[2..], Part(3)[2..], patterns, palette);
    }

    /// <summary>
    /// The sprites of a costume's frame, in the order they are drawn: each its y and x from the
    /// actor's position, its tile, its sub-palette (0 to 3) and whether it is drawn mirrored.
    /// Mirrors the sprite list walk of <c>NESCostumeRenderer::drawLimb</c>, which reads the frame's
    /// three-byte entries from the last to the first.
    /// </summary>
    public IEnumerable<NesSprite> Sprites(int costume, int frame)
    {
        int description = Word(_descriptions.Span, NesCostumeTables.Lookup[costume] * 2);
        int count = Byte(_lengths.Span, description + frame) + 1;
        int at = Word(_offsets.Span, 2 * (description + frame)) + count * 3;
        for (int i = 0; i < count; i++)
        {
            at -= 3;
            var data = _data.Span;
            byte first = Byte(data, at), tile = Byte(data, at + 1), last = Byte(data, at + 2);
            yield return new NesSprite((sbyte)(byte)(first << 1) >> 1, (sbyte)last >> 2, tile, last & 3, (first & 0x80) != 0);
        }
    }

    private static byte Byte(ReadOnlySpan<byte> data, int at)
        => at >= 0 && at < data.Length ? data[at] : throw new InvalidDataException($"An NES costume set is read past its end, at {at}.");

    private static int Word(ReadOnlySpan<byte> data, int at) => at + 2 <= data.Length
        ? BinaryPrimitives.ReadUInt16LittleEndian(data[at..])
        : throw new InvalidDataException($"An NES costume set is read past its end, at {at}.");
}

/// <summary>An 8x8 sprite of an NES costume frame: where it sits from the actor, its tile and sub-palette, and whether it is mirrored.</summary>
internal readonly record struct NesSprite(int Y, int X, int Tile, int SubPalette, bool Mirrored);

/// <summary>
/// Draws an NES actor's frame into the room's view, sprite by sprite. Mirrors
/// <c>NESCostumeRenderer::drawLimb</c>.
/// </summary>
internal static class NesCostumeRenderer
{
    private const int SpriteSize = 8, TileBytes = 16;

    /// <summary>
    /// Draws the sprites of a frame at a point of the view (<c>_actorX</c>, <c>_actorY</c>): a
    /// sprite that crosses an edge of the view is not drawn at all; a pixel of colour 0 is not
    /// drawn, nor, behind the mask, one where the room's mask is set. A figure facing right is
    /// drawn mirrored, its sprites placed the other way. The colours are the set's, or the dark
    /// ones without the light's (<paramref name="colors"/>). Returns the columns the figure
    /// spans, sprites drawn or not.
    /// </summary>
    public static (int Left, int Right) Draw(MaskedImage view, NesCostumeSet set, IEnumerable<NesSprite> sprites, int actorX, int actorY, bool flipped, bool colors, bool behindMask)
    {
        var palette = colors ? set.Palette : NesCostumeTables.DarkActorPalette;
        int left = 239, right = 0;
        foreach (var sprite in sprites)
        {
            bool mirrored = sprite.Mirrored ^ flipped;
            int x = flipped ? -sprite.X : sprite.X;
            int sx = actorX + x, sy = actorY + sprite.Y;
            left = Math.Min(left, sx);
            right = Math.Max(right, sx + SpriteSize);
            if (sx < 0 || sx + SpriteSize >= view.Pixels.Width || sy < 0 || sy + SpriteSize >= view.Pixels.Height)
            {
                continue;
            }

            for (int ty = 0; ty < SpriteSize; ty++)
            {
                byte plane0 = set.Patterns[sprite.Tile * TileBytes + ty];
                byte plane1 = set.Patterns[sprite.Tile * TileBytes + ty + 8];
                for (int tx = 0; tx < SpriteSize; tx++)
                {
                    int bit = mirrored ? tx : 7 - tx;
                    int color = ((plane0 >> bit) & 1) | (((plane1 >> bit) & 1) << 1);
                    if (color == 0)
                    {
                        continue;
                    }

                    int px = sx + tx, py = sy + ty;
                    if (behindMask && view.Mask[px, py] != 0)
                    {
                        continue;
                    }

                    view.Pixels[px, py] = palette[color | (sprite.SubPalette << 2)];
                }
            }
        }

        return (left, right);
    }
}
