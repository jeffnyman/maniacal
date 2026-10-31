using Maniacal.Scumm.Graphics;
using Maniacal.Scumm.View;

namespace Maniacal.Scumm.Game;

/// <summary>A frame of a room transition, and how many quarter-frames it shows for.</summary>
internal readonly record struct TransitionFrame(Surface8 Screen, int QuarterFrames);

/// <summary>
/// The transitions between rooms: leaving one, the room on screen goes black block by block (an
/// iris closing); entering one, the next frame shows it block by block (an inverse iris opening).
/// ScummVM waits between the blocks within the step, the game standing still; here the step makes
/// the frames, and a host shows them, for so many quarter-frames each, before the step's own
/// screen. Mirrors <c>fadeOut</c>, <c>fadeIn</c> and <c>transitionEffect</c> for versions 0 to 2.
/// </summary>
public sealed partial class GameWorld
{
    // the effects resetScumm gives versions 1 and 2 and the C64: the iris to leave, the inverse
    // iris to enter (_switchRoomEffect2, _switchRoomEffect), and the effect of the first frame (_newEffect)
    private const int LeaveEffect = 1, EnterEffect = 5, NoEffect = 129;

    // the quarter-frames between a transition's iterations (kC64Delay, kPictureDelay, kNoDelay)
    private int TransitionDelay => Version switch
    {
        0 => 6,
        1 => 0,
        _ => 4,
    };

    private readonly List<TransitionFrame> _transition = [];
    private bool _screenEffectFlag, _doEffect;
    private int _newEffect = NoEffect;
    private Surface8? _display;

    /// <summary>The frames of the last step's room transitions, to show before its own screen; their length is in <see cref="StepLength"/>.</summary>
    internal IReadOnlyList<TransitionFrame> Transition => _transition;

    /// <summary>How many quarter-frames the last step's transitions last.</summary>
    private int TransitionQuarterFrames => _transition.Sum(f => f.QuarterFrames);

    /// <summary>
    /// Leaving a room: the room on screen goes black with an effect, when the last room came in
    /// with one; the C64 shows the message line first. Mirrors <c>fadeOut</c> for effects 1 to 6 and 129.
    /// </summary>
    private void FadeOut(int effect)
    {
        if (_screenEffectFlag && effect != 0)
        {
            var display = Displayed();
            if (Version == 0)
            {
                for (int y = 0; y < MessageLine.Height; y++)
                {
                    Messages.Text.Row(y).CopyTo(display.Row(y));
                }
            }

            var black = new Surface8(ScreenLayout.Width, ScreenLayout.Height);
            if (effect is >= 1 and <= 6)
            {
                TransitionEffect(effect - 1, black);
            }
            else if (effect == NoEffect)
            {
                var layout = ScreenLayout.ForVersion(Version);
                for (int y = layout.RoomTop; y < layout.RoomBottom; y++)
                {
                    black.Row(y).CopyTo(display.Row(y));
                }
            }
            else
            {
                throw new InvalidOperationException($"The room effect {effect} is not one these versions use.");
            }
        }

        _screenEffectFlag = false;
    }

    /// <summary>The new room's first frame shows with an effect. Mirrors <c>fadeIn</c> for effects 0 to 6 and 129, run by <c>scummLoop_handleEffects</c>.</summary>
    private void FadeIn(int effect, Surface8 screen)
    {
        if (effect is >= 1 and <= 6)
        {
            TransitionEffect(effect - 1, screen);
        }
        else if (effect is not (0 or NoEffect))
        {
            throw new InvalidOperationException($"The room effect {effect} is not one these versions use.");
        }

        _screenEffectFlag = true;
    }

    /// <summary>
    /// The room part of the screen as displayed takes another's, a line of 8x8 blocks at a time
    /// along four lines that move each iteration, a frame after each. Versions before 3 work the
    /// iterations out from the room's height. Mirrors <c>transitionEffect</c>.
    /// </summary>
    private void TransitionEffect(int effect, Surface8 source)
    {
        var display = Displayed();
        var layout = ScreenLayout.ForVersion(Version);
        int height = layout.RoomHeight;
        int bottom = height / 8;
        var (iterations, deltas, starts) = TransitionEffects.All[effect];
        if (Version <= 2)
        {
            iterations = effect is 0 or 4 ? (height + 15) / 16 : height / 8;
        }

        var lines = starts.Select(s => s == 24 ? height / 8 - 1 : s).ToArray();
        for (int j = 0; j < iterations; j++)
        {
            for (int i = 0; i < 4; i++)
            {
                int l = lines[i * 4], t = lines[i * 4 + 1], r = lines[i * 4 + 2], b = lines[i * 4 + 3];
                if (t == b)
                {
                    for (; l <= r; l++)
                    {
                        if (l >= 0 && l < Camera.Strips && t < bottom)
                        {
                            CopyBlocks(source, display, l, t, b + 1, layout);
                        }
                    }
                }
                else if (l >= 0 && l < Camera.Strips && b > t)
                {
                    CopyBlocks(source, display, l, Math.Max(t, 0), Math.Min(b, bottom) + 1, layout);
                }
            }

            for (int i = 0; i < 16; i++)
            {
                lines[i] += deltas[i];
            }

            if (TransitionDelay > 0)
            {
                _transition.Add(new TransitionFrame(Copy(display), TransitionDelay));
            }
        }
    }

    /// <summary>The screen as the player sees it now: the last transition frame, or the last frame drawn.</summary>
    private Surface8 Displayed() => _display ??= Screen();

    /// <summary>A strip's rows of blocks from one screen to another, kept inside the room.</summary>
    private static void CopyBlocks(Surface8 from, Surface8 to, int strip, int topBlock, int endBlock, ScreenLayout layout)
    {
        int top = Math.Max(topBlock * 8, 0), end = Math.Min(endBlock * 8, layout.RoomHeight);
        for (int y = top; y < end; y++)
        {
            from.Row(layout.RoomTop + y).Slice(strip * 8, 8).CopyTo(to.Row(layout.RoomTop + y).Slice(strip * 8, 8));
        }
    }

    private static Surface8 Copy(Surface8 screen)
    {
        var copy = new Surface8(screen.Width, screen.Height);
        screen.Pixels.CopyTo(copy.Pixels);
        return copy;
    }
}
