using Daventry.Core.Graphics;

namespace Maniacal.Scumm.View;

/// <summary>
/// A room with its lights off: black, but for the flashlight's rounded patch of the lit room around
/// the mouse. Mirrors ScummVM's <c>clear8Col</c> for dark rooms and <c>ScummEngine_v5::drawFlashlight</c>
/// for versions 1 and 2, whose corner curves are masks of half-resolution pixels, two screen pixels
/// to a bit pair.
/// </summary>
public static class Flashlight
{
    // the light bits of VAR_CURRENT_LIGHTS (LIGHTMODE_*)
    public const int RoomLightsOn = 2, FlashlightOn = 4;

    /// <summary>The size scripts start with, in strips. Mirrors the <c>ScummEngine_v2</c> constructor.</summary>
    public const int DefaultStrips = 6, DefaultRows = 4;

    // v1FwdCurveData and the rest: four bytes a line, eight lines from the corner inwards
    private static readonly byte[] V1Forward =
    [
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00, 0xFF, 0xFF,
        0x00, 0xFF, 0xFF, 0xFF, 0x00, 0xFF, 0xFF, 0xFF, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
    ];

    private static readonly byte[] V1Backward =
    [
        0xFF, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0x00,
        0xFF, 0xFF, 0xFF, 0x00, 0xFF, 0xFF, 0xFF, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
    ];

    private static readonly byte[] V2Forward =
    [
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0F, 0xFF, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x0F, 0xFF, 0xFF,
        0x00, 0xFF, 0xFF, 0xFF, 0x0F, 0xFF, 0xFF, 0xFF, 0x0F, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
    ];

    private static readonly byte[] V2Backward =
    [
        0x00, 0x00, 0x00, 0x00, 0xFF, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00,
        0xFF, 0xFF, 0xFF, 0x00, 0xFF, 0xFF, 0xFF, 0x00, 0xFF, 0xFF, 0xFF, 0x00, 0xFF, 0xFF, 0xFF, 0xFF,
    ];

    /// <summary>
    /// Where the flashlight shines on the view: centred on the mouse, which is first snapped to a
    /// column of 8 and an even line, and kept inside the view.
    /// </summary>
    /// <param name="mouseX">The mouse in the room, in pixels (screen x plus the camera's left edge).</param>
    /// <param name="mouseY">The mouse in the room view, in lines.</param>
    /// <param name="cameraLeft">The room column, in pixels, at the view's left edge.</param>
    /// <param name="strips">The flashlight's width in 8-pixel strips.</param>
    /// <param name="rows">The flashlight's height in 8-line rows.</param>
    /// <param name="viewWidth">The view's width, which the patch stays inside.</param>
    /// <param name="viewHeight">The view's height, which the patch stays inside.</param>
    public static Rect Area(int mouseX, int mouseY, int cameraLeft, int strips, int rows, int viewWidth, int viewHeight)
    {
        int width = strips * 8;
        int height = rows * 8;
        int x = (mouseX & ~7) - width / 2 - cameraLeft;
        int y = (mouseY & ~1) - height / 2;
        x = x < 0 ? 0 : x + width > viewWidth ? viewWidth - width : x;
        y = y < 0 ? 0 : y + height > viewHeight ? viewHeight - height : y;
        return new Rect(x, y, width, height);
    }

    /// <summary>
    /// Darkens a view whose lights are off: everything goes black, then, with the flashlight on,
    /// its area shows the lit room again with its corners rounded.
    /// </summary>
    public static void Darken(Surface8 view, int version, Rect? light)
    {
        var lit = light is { IsEmpty: false } ? Copy(view, light.Value) : null;
        view.Fill(0);
        if (lit is null || light is not { } area)
        {
            return;
        }

        view.Blit(lit, lit.Bounds, area.X, area.Y);
        RoundCorners(view, version, area);
    }

    private static Surface8 Copy(Surface8 view, Rect area)
    {
        var copy = new Surface8(area.Width, area.Height);
        copy.Blit(view, area, 0, 0);
        return copy;
    }

    private static void RoundCorners(Surface8 view, int version, Rect area)
    {
        var forward = version <= 1 ? V1Forward : V2Forward;
        var backward = version <= 1 ? V1Backward : V2Backward;
        int right = area.Width - 8;
        for (int line = 0; line < 8 && line < area.Height; line++)
        {
            int top = area.Y + line;
            int bottom = area.Y + area.Height - 1 - line;
            for (int pair = 0; pair < 4; pair++)
            {
                byte fwd = forward[line * 4 + pair];
                byte bkwd = backward[line * 4 + pair];
                int x = area.X + pair * 2;
                foreach (int y in (ReadOnlySpan<int>)[top, bottom])
                {
                    Keep(view, x, y, (fwd & 0xF0) != 0);
                    Keep(view, x + 1, y, (fwd & 0x0F) != 0);
                    Keep(view, x + right, y, (bkwd & 0x0F) != 0);
                    Keep(view, x + right + 1, y, (bkwd & 0xF0) != 0);
                }
            }
        }
    }

    private static void Keep(Surface8 view, int x, int y, bool lit)
    {
        if (!lit && x >= 0 && x < view.Width && y >= 0 && y < view.Height)
        {
            view[x, y] = 0;
        }
    }
}
