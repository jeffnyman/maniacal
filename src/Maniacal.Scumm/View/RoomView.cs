using Daventry.Core.Graphics;
using Maniacal.Scumm.Costumes;
using Maniacal.Scumm.Graphics;

namespace Maniacal.Scumm.View;

/// <summary>
/// Where the parts of the 320x200 screen are. Mirrors the <c>initScreens</c> calls of
/// <c>ScummEngine::resetScumm</c>: the message lines above the room, the verbs and inventory below.
/// </summary>
public readonly record struct ScreenLayout(int RoomTop, int RoomBottom)
{
    public const int Width = 320;
    public const int Height = 200;

    /// <summary>Version 1 shows 136 room lines (<c>initScreens(16, 152)</c>), version 2 shows 128 (<c>initScreens(16, 144)</c>).</summary>
    public static ScreenLayout ForVersion(int version) => version <= 1 ? new(16, 152) : new(16, 144);

    public int RoomHeight => RoomBottom - RoomTop;
}

/// <summary>An actor to draw: a costume figure, where it stands and how it is clipped.</summary>
/// <param name="Frame">The figure, from <see cref="CostumeRenderer.DrawFrame"/>.</param>
/// <param name="X">Position in actor units: 8-pixel columns (<c>V12_X_MULTIPLIER</c>).</param>
/// <param name="Y">Position in actor units: 2-pixel lines (<c>V12_Y_MULTIPLIER</c>).</param>
/// <param name="FacingLeft">Whether the actor faces left (270 degrees), which moves it right by 16 pixels.</param>
/// <param name="BehindMask">Whether the room mask covers it: true when its walkbox's mask is not 0.</param>
/// <param name="Elevation">Height above its position, in actor units.</param>
public readonly record struct ActorDrawing(CostumeFrame Frame, int X, int Y, bool FacingLeft, bool BehindMask, int Elevation = 0);

/// <summary>
/// The room as the screen shows it: the part the camera looks at, with actors drawn on it.
/// Mirrors <c>Actor_v2::prepareDrawActorCostume</c>, <c>BaseCostumeRenderer::drawCostume</c> and
/// the mask test of the costume decoders for versions 1 and 2.
/// </summary>
public static class RoomView
{
    /// <summary>The 320-pixel-wide part of a drawn room the camera shows, and its mask.</summary>
    public static MaskedImage Window(MaskedImage room, Camera camera)
    {
        int width = Math.Min(Camera.ScreenWidth, room.Pixels.Width);
        int height = room.Pixels.Height;
        var view = new MaskedImage(new Surface8(Camera.ScreenWidth, height), new Surface8(Camera.ScreenWidth, height));
        int left = Math.Clamp(camera.Left, 0, room.Pixels.Width - width);
        for (int y = 0; y < height; y++)
        {
            room.Pixels.Row(y).Slice(left, width).CopyTo(view.Pixels.Row(y));
            room.Mask.Row(y).Slice(left, width).CopyTo(view.Mask.Row(y));
        }

        return view;
    }

    /// <summary>
    /// Draws an actor on the view. Its point is its position in pixels less the view's left edge,
    /// moved right by 8 pixels in version 2, or by 16 in either version when facing left, and
    /// down a line in version 1. A pixel under the room mask is not drawn when the actor is behind it.
    /// </summary>
    public static void DrawActor(MaskedImage view, int version, Camera camera, ActorDrawing actor)
    {
        int x = actor.X * 8 - camera.Left;
        int y = (actor.Y - actor.Elevation) * 2;

        if (actor.FacingLeft)
        {
            x += 16;
        }
        else if (version == 2)
        {
            x += 8;
        }

        if (version <= 1)
        {
            y += 1;
        }

        var frame = actor.Frame;
        int left = x - frame.OriginX;
        int top = y - frame.OriginY;
        for (int fy = 0; fy < frame.Pixels.Height; fy++)
        {
            int sy = top + fy;
            if (sy < 0 || sy >= view.Pixels.Height)
            {
                continue;
            }

            for (int fx = 0; fx < frame.Pixels.Width; fx++)
            {
                int sx = left + fx;
                byte pixel = frame.Pixels[fx, fy];
                if (pixel == CostumeRenderer.Transparent || sx < 0 || sx >= view.Pixels.Width)
                {
                    continue;
                }

                if (actor.BehindMask && view.Mask[sx, sy] != 0)
                {
                    continue;
                }

                view.Pixels[sx, sy] = pixel;
            }
        }
    }

    /// <summary>The whole 320x200 screen with the room view in its place; the rest is black for now.</summary>
    public static Surface8 Screen(MaskedImage view, ScreenLayout layout)
    {
        var screen = new Surface8(ScreenLayout.Width, ScreenLayout.Height);
        int lines = Math.Min(layout.RoomHeight, view.Pixels.Height);
        for (int y = 0; y < lines; y++)
        {
            view.Pixels.Row(y).CopyTo(screen.Row(layout.RoomTop + y));
        }

        return screen;
    }
}
