namespace Maniacal.Scumm.Graphics;

/// <summary>
/// The host's display. The engine owns an 8-bit framebuffer and a palette and hands both to the
/// host; the host decides how to scale and present them.
/// </summary>
public interface IGraphicsDriver
{
    /// <summary>Sets the logical framebuffer size the engine will present.</summary>
    void SetMode(int width, int height);

    /// <summary>Sets palette entries from <paramref name="start"/> on, for the frames presented after.</summary>
    void SetPalette(ReadOnlySpan<Rgb> colors, int start = 0);

    /// <summary>Presents a complete frame. The host must not keep a reference to the surface.</summary>
    void Present(Surface8 frame);

    /// <summary>Sets the window's title, where the host has one.</summary>
    void SetTitle(string title);
}
