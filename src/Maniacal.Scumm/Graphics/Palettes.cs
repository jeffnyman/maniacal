
namespace Maniacal.Scumm.Graphics;

/// <summary>The fixed palettes of the releases. Mirrors the tables in ScummVM's <c>ScummEngine::resetPalette</c>.</summary>
public static partial class Palettes
{
    /// <summary>The 16 EGA colours the DOS releases draw with. Mirrors <c>tableEGAPalette</c>.</summary>
    public static ReadOnlySpan<Rgb> Ega => EgaColors;

    /// <summary>The colours the C64 release draws with, 17 of them. Mirrors <c>tableC64Palette</c>.</summary>
    public static ReadOnlySpan<Rgb> C64 => C64Rgb;

    /// <summary>
    /// The C64 colour numbers of version 1 rooms, as the EGA colours the DOS release shows them.
    /// Mirrors the "MM: EGA, Tandy, MCGA, CGA Composite" row of <c>setV1ColorTable</c>.
    /// </summary>
    public static ReadOnlySpan<byte> ManiacV1ToEga => ManiacV1EgaColors;

    /// <summary>The C64's own colour numbers, which the C64 release keeps. Mirrors the "C-64" row of <c>setV1ColorTable</c>.</summary>
    public static ReadOnlySpan<byte> C64Identity => C64Colors;

    /// <summary>The palette a version draws with: the C64's for version 0, EGA for the DOS releases.</summary>
    public static ReadOnlySpan<Rgb> ForVersion(int version) => version == 0 ? C64 : Ega;

    /// <summary>How a version shows the C64 colour numbers of its room pictures (<c>setV1ColorTable</c>).</summary>
    public static ReadOnlySpan<byte> RoomColorMap(int version) => version == 0 ? C64Identity : ManiacV1ToEga;
}
