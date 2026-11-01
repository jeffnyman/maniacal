using Maniacal.Scumm.Resources;

namespace Maniacal.Scumm.Graphics;

/// <summary>
/// The NES's two title screens, shown before the boot script: the first for five seconds, the
/// second for five and a half, with eight sparkles appearing on it one after another, each with a
/// twinkle; a click or Escape moves on to the next. Mirrors <c>playNESTitleScreens</c> and
/// <c>nesTitle2WaitOrSkipWithSpriteAnim</c>, a frame a tick, the 60th of a second the NES's clock
/// counts (ScummVM's loop counts 16 milliseconds).
/// </summary>
internal sealed class NesTitles
{
    public const int Width = 256, Height = 240;

    // the screen's 32 by 30 tiles of 8 by 8
    private const int Columns = 32, Rows = 30, TileBytes = 16;

    // a sparkle's pictures (the sparkle tiles), and the sub-palette they are drawn in
    private const int SparklePictures = 6, SparkleSubPalette = 0;

    private const int TicksPerSecond = 60;

    private readonly Surface8[] _screens;
    private readonly byte[] _sparkleTiles;
    private readonly byte[] _sparklePalette;
    private readonly Action<int>? _twinkle;

    private int _screen, _ticks;

    // the second screen's sparkles: the frames until their pictures change and the next appears,
    // the next's place, how many have appeared, those showing, and the twinkles waiting to start
    private int _untilPicture = NesTitleTables.FramesPerPicture, _untilSparkle = NesTitleTables.FramesPerSparkle;
    private int _nextSparkle, _sparklesSpawned;
    private readonly List<(int X, int Y, int Picture)> _sparkles = [];
    private readonly List<(int Sparkle, int Frames)> _twinkles = [];

    private NesTitles(Surface8[] screens, byte[] sparkleTiles, byte[] sparklePalette, Action<int>? twinkle)
    {
        _screens = screens;
        _sparkleTiles = sparkleTiles;
        _sparklePalette = sparklePalette;
        _twinkle = twinkle;
    }

    /// <summary>The cartridge's title screens; <paramref name="twinkle"/> starts the twinkle of a sparkle, 0 to 7.</summary>
    public static NesTitles Load(NesRom rom, Action<int>? twinkle = null) => new(
        [Decode(rom.Raw(NesResourceKind.Titles, 0, toEnd: true).Span), Decode(rom.Raw(NesResourceKind.Titles, 1, toEnd: true).Span)],
        rom.Raw(NesResourceKind.TitleSparkleTiles, 0).ToArray(),
        rom.Raw(NesResourceKind.TitleSparklePalette, 0).ToArray(),
        twinkle);

    /// <summary>Whether both screens have been shown.</summary>
    public bool Done => _screen >= _screens.Length;

    /// <summary>The screen showing: 0 or 1, or 2 once both are done.</summary>
    public int Showing => _screen;

    /// <summary>The ticks a screen stays, its milliseconds on the NES's clock.</summary>
    public static int Ticks(int screen) => (NesTitleTables.WaitMilliseconds[screen] * TicksPerSecond + 999) / 1000;

    /// <summary>Moves on to the next screen, as a click or Escape does.</summary>
    public void Skip()
    {
        _screen++;
        _ticks = 0;
    }

    /// <summary>A tick: the second screen's sparkles move on, and a screen whose time is up gives way to the next.</summary>
    public void Tick()
    {
        if (Done)
        {
            return;
        }

        if (_screen == 1)
        {
            TickSparkles();
        }

        if (++_ticks >= Ticks(_screen))
        {
            Skip();
        }
    }

    // a tick of nesTitle2WaitOrSkipWithSpriteAnim: the pictures move on, ending each sparkle after
    // its sixth; a new sparkle appears, until all eight have; and a twinkle waiting starts
    private void TickSparkles()
    {
        if (--_untilPicture < 0)
        {
            _untilPicture = NesTitleTables.FramesPerPicture;
            for (int i = _sparkles.Count - 1; i >= 0; i--)
            {
                var sparkle = _sparkles[i];
                if (sparkle.Picture + 1 >= SparklePictures)
                {
                    _sparkles.RemoveAt(i);
                }
                else
                {
                    _sparkles[i] = sparkle with { Picture = sparkle.Picture + 1 };
                }
            }
        }

        if (_sparklesSpawned < NesTitleTables.Sparkles.Length && --_untilSparkle < 0)
        {
            _untilSparkle = NesTitleTables.FramesPerSparkle;
            var (x, y) = NesTitleTables.Sparkles[_nextSparkle];
            _sparkles.Add((x, y, 0));
            _twinkles.Add((_nextSparkle, 1));
            _nextSparkle = (_nextSparkle + 1) & 7;
            _sparklesSpawned++;
        }

        for (int i = _twinkles.Count - 1; i >= 0; i--)
        {
            var (sparkle, frames) = _twinkles[i];
            if (--frames <= 0)
            {
                _twinkle?.Invoke(sparkle);
                _twinkles.RemoveAt(i);
                break;
            }

            _twinkles[i] = (sparkle, frames);
        }
    }

    /// <summary>The screen showing, in the NES's colours, with the sparkles over the second.</summary>
    public Surface8 Frame()
    {
        var frame = new Surface8(Width, Height);
        if (Done)
        {
            return frame;
        }

        _screens[_screen].Pixels.CopyTo(frame.Pixels, 0);
        foreach (var (x, y, picture) in _sparkles)
        {
            int tile = picture * TileBytes;
            if (tile + TileBytes > _sparkleTiles.Length)
            {
                continue;
            }

            for (int row = 0; row < 8; row++)
            {
                byte plane0 = _sparkleTiles[tile + row], plane1 = _sparkleTiles[tile + 8 + row];
                for (int column = 0; column < 8; column++)
                {
                    int color = ((plane0 >> (7 - column)) & 1) | (((plane1 >> (7 - column)) & 1) << 1);
                    int px = x + column, py = y + row;
                    if (color != 0 && px < Width && py < Height)
                    {
                        frame[px, py] = (byte)(_sparklePalette[SparkleSubPalette * 4 + color] & 0x3F);
                    }
                }
            }
        }

        return frame;
    }

    /// <summary>
    /// A title screen: its tiles, nametable and attributes, each run-length coded, then its 16
    /// colours, drawn whole, colour 0 of every tile the background's. Mirrors
    /// <c>decodeTitleScreen</c> and the drawing of <c>playNESTitleScreens</c>.
    /// </summary>
    internal static Surface8 Decode(ReadOnlySpan<byte> data)
    {
        int at = 0;

        at += 4;
        int tileCount = Byte(data, ref at) + 1;
        var tiles = Unpack(data, ref at, tileCount * TileBytes);
        at += 3;
        int width = Byte(data, ref at) + 1, height = Byte(data, ref at) + 1;
        var nametable = Unpack(data, ref at, width * height);
        at += 3;
        int attrWidth = Byte(data, ref at) + 1, attrHeight = Byte(data, ref at) + 1;
        var attributes = Unpack(data, ref at, attrWidth * attrHeight);
        at++;
        var palette = new byte[16];
        for (int i = 0; i < palette.Length; i++)
        {
            palette[i] = Byte(data, ref at);
        }

        var screen = new Surface8(Width, Height);
        byte background = (byte)(palette[0] & 0x3F);
        for (int tileY = 0; tileY < Rows; tileY++)
        {
            for (int tileX = 0; tileX < Columns; tileX++)
            {
                int index = tileY * Columns + tileX;
                int tile = (index < nametable.Length ? nametable[index] : 0) * TileBytes;
                if (tile + TileBytes > tiles.Length)
                {
                    continue;
                }

                int attrIndex = tileY / 4 * 8 + tileX / 4;
                byte attribute = attrIndex < attributes.Length ? attributes[attrIndex] : (byte)0;
                int shift = ((tileY % 4 / 2) * 2 + tileX % 4 / 2) * 2;
                int subPalette = (attribute >> shift) & 3;
                for (int row = 0; row < 8; row++)
                {
                    byte plane0 = tiles[tile + row], plane1 = tiles[tile + 8 + row];
                    for (int column = 0; column < 8; column++)
                    {
                        int color = ((plane0 >> (7 - column)) & 1) | (((plane1 >> (7 - column)) & 1) << 1);
                        screen[tileX * 8 + column, tileY * 8 + row] = color == 0 ? background : (byte)(palette[subPalette * 4 + color] & 0x3F);
                    }
                }
            }
        }

        return screen;
    }

    private static byte Byte(ReadOnlySpan<byte> data, ref int at) => at < data.Length ? data[at++] : (byte)0;

    // a run-length coded block (decodeTitleRLE): a byte whose low seven bits count, with the top
    // bit that many bytes as they are, without it one byte that many times; a count of 0 is passed over
    private static byte[] Unpack(ReadOnlySpan<byte> data, ref int at, int length)
    {
        var output = new List<byte>(length);
        while (output.Count < length && at < data.Length)
        {
            byte loop = data[at++];
            int count = loop & 0x7F;
            if (count == 0)
            {
                continue;
            }

            if ((loop & 0x80) != 0)
            {
                for (int i = 0; i < count && output.Count < length; i++)
                {
                    output.Add(at < data.Length ? data[at++] : (byte)0);
                }
            }
            else
            {
                byte value = at < data.Length ? data[at++] : (byte)0;
                for (int i = 0; i < count && output.Count < length; i++)
                {
                    output.Add(value);
                }
            }
        }

        return [.. output];
    }
}
