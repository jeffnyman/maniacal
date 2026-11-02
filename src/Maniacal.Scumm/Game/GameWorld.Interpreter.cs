using System.Text;
using Maniacal.Scumm.Graphics;

namespace Maniacal.Scumm.Game;

/// <summary>
/// The keys the original interpreters kept for themselves, before the game's scripts saw them, as
/// ScummVM has them with the original interface: Space pauses, F8 asks before restarting, F6
/// turns the sounds off and on. The pause and the question wait for a key or a click, the game
/// standing still and its sound with it. Mirrors the original-interface half of
/// <c>ScummEngine::processKeyboard</c>, <c>printMessageAndPause</c>, <c>showOldStyleBannerAndPause</c>
/// and <c>queryRestart</c>.
/// </summary>
public sealed partial class GameWorld
{
    // the English version 1 and 2 interpreters' strings (getStaticResString, getGUIString); the
    // restart question's last letter is the key that says yes
    private const string PauseMessage = "Game paused, press SPACE to continue.  ";
    private const string RestartQuestion = "Are you sure you want to restart? (y/n)y";
    private const string SoundsOnMessage = "Sounds On", SoundsOffMessage = "Sounds Off";

    private const int KeyF6 = KeyF1 + 5, KeyF8 = KeyF1 + 7;

    // the pause's line on the sentence line: 40 characters, in colour 13, the C64's 16 (printMessageAndPause)
    private const int PauseChars = 40, PauseColor = 13, PauseColorV0 = 16;

    // the restart question at the top of the screen, in colour 4 (queryRestart)
    private const int RestartColor = 4;

    // the demos' banner: 12 rows from row 80, a line at the top and the bottom, the text centred
    // two rows down, all in colour 12 (initScreens, showOldStyleBannerAndPause)
    private const int BannerTop = 80, BannerHeight = 12, BannerColor = 12, BannerTextTop = 2;

    // the snails while paused: the DOS and C64 releases' 24 by 21, the Amiga's and the Atari ST's
    // 16 by 16 (setSnailCursor); a marked pixel takes the cursor's colour, the others their own
    private const int SnailWidth = 24, SnailHotspotX = 11, SnailHotspotY = 10, AmigaSnailHotspot = 7, AtariStSnailHotspot = 8;
    private const int SnailMarked = 0x0F, SnailClear = 0xFF;

    private IGlyphs _font = null!;
    private Banner _banner;
    private int _bannerEgo;
    private bool _soundsOff;

    // what the interpreter shows while the game waits
    private enum Banner
    {
        None,

        // "Game paused" on the sentence line, the snail for a cursor
        Pause,

        // "Game paused" in a banner across the room, as the demos have it
        PauseBanner,

        // the restart question at the top of the screen
        Restart,
    }

    /// <summary>Whether the game stands still on a message of the interpreter's, waiting for a key or a click.</summary>
    internal bool WaitsOnMessage => _banner != Banner.None;

    /// <summary>Whether F6 has turned the sounds off.</summary>
    internal bool SoundsOff => _soundsOff;

    /// <summary>
    /// A key the interpreter takes before the scripts can: Space, F8 and F6, none of them on the
    /// NES, whose Space opens its own menu. True when the key was one of them, and goes no further.
    /// Mirrors the original-interface part of <c>ScummEngine::processKeyboard</c>.
    /// </summary>
    private bool InterpreterKey(int key)
    {
        if (IsNes)
        {
            return false;
        }

        switch (key)
        {
            // the demos show a banner; the games, a line on the sentence line, the snail for a cursor
            case ' ':
                StartBanner(IsDemo ? Banner.PauseBanner : Banner.Pause);
                if (!IsDemo)
                {
                    Verbs.DrawLine(Encoding.Latin1.GetBytes(PauseMessage), PauseChars, Version == 0 ? PauseColorV0 : PauseColor);
                }

                return true;

            // the question, its last letter left off, said by nobody at the top of the screen
            case KeyF8:
                StartBanner(Banner.Restart);
                Say(RestartQuestion[..^1], RestartColor);
                return true;

            // the sounds off or on, said as a message is, the game going on
            case KeyF6:
                _soundsOff = !_soundsOff;
                Sound.Output.Muted = _soundsOff;
                if (_soundsOff)
                {
                    Say(SoundsOffMessage, Version <= 1 ? 2 : 4);
                }
                else
                {
                    Say(SoundsOnMessage, Version <= 1 ? 5 : 2);
                }

                return true;
        }

        return false;
    }

    // a message said by nobody in a colour (printMessageAndPause: actorTalk with _actorToPrintStrFor 0xFF)
    private void Say(string message, int color)
    {
        int before = Messages.StringColor;
        Messages.StringColor = color;
        Messages.Talk(MessageLine.NoActor, Encoding.Latin1.GetBytes(message));
        Messages.StringColor = before;
    }

    // a load or a restart from outside puts an end to a wait, the sound going on
    private void EndBanner()
    {
        _banner = Banner.None;
        Sound.Output.Paused = false;
    }

    // the engine paused, and the sound with it (pauseEngine)
    private void StartBanner(Banner banner)
    {
        _banner = banner;
        Sound.Output.Paused = true;
    }

    /// <summary>
    /// A step while the game waits: nothing moves until a key or a click, which ends the wait and
    /// goes no further. True when the wait has ended and the step it interrupted should go on.
    /// Mirrors <c>waitForBannerInput</c> and the ends of <c>printMessageAndPause</c> and
    /// <c>queryRestart</c>.
    /// </summary>
    private bool StepBanner()
    {
        _transition.Clear();
        _display = null;
        int key = _key;
        bool answered = key != 0 || _clicked || _clickAfterHover || _rightClicked;
        (_clicked, _clickAfterHover, _rightClicked, _key) = (false, false, false, 0);
        if (!answered)
        {
            return false;
        }

        var banner = _banner;
        _banner = Banner.None;
        Sound.Output.Paused = false;
        switch (banner)
        {
            // the sentence line cleared, and its sentence shown again, but in version 1 and on the C64
            case Banner.Pause:
                Verbs.ClearLine();
                if (Version >= 2)
                {
                    DrawSentence();
                }

                break;

            // the question goes; its letter restarts the game, the room fading out first
            case Banner.Restart:
                Messages.Stop();
                if (char.ToLowerInvariant((char)key) == RestartQuestion[^1])
                {
                    FadeOut(1);
                    Restart();
                }

                break;
        }

        return true;
    }

    /// <summary>
    /// What a wait puts on the screen over the last step's: the question at the top, or the
    /// demos' banner across the room.
    /// </summary>
    private void DrawBanner(Surface8 screen)
    {
        switch (_banner)
        {
            case Banner.Restart:
                for (int y = 0; y < MessageLine.Height && TextTop + y < Layout.RoomTop; y++)
                {
                    Messages.Text.Row(y).CopyTo(screen.Row(TextTop + y));
                }

                break;

            case Banner.PauseBanner:
                screen.FillRect(new Rect(0, BannerTop, screen.Width, BannerHeight), 0);
                screen.FillRect(new Rect(0, BannerTop, screen.Width, 1), BannerColor);
                screen.FillRect(new Rect(0, BannerTop + BannerHeight - 1, screen.Width, 1), BannerColor);
                int width = PauseMessage.Count(c => c != '@') * Font.CharWidth;
                int x = screen.Width / 2 - width / 2;
                foreach (char c in PauseMessage)
                {
                    if (c != '@' && x + Font.CharWidth <= screen.Width)
                    {
                        _font.DrawGlyph(screen, c, x, BannerTop + BannerTextTop, BannerColor);
                        x += Font.CharWidth;
                    }
                }

                break;
        }
    }

    /// <summary>
    /// The snail shown for the cursor while the game is paused: the Amiga's and the Atari ST's
    /// pictures as they are, the DOS and C64 releases' in the cursor's colour. Mirrors
    /// <c>ScummEngine_v2::setSnailCursor</c>.
    /// </summary>
    private void DrawSnail(Surface8 screen)
    {
        var (image, width, hotX, hotY) = IsAmiga ? (CursorImages.AmigaSnail, 16, AmigaSnailHotspot, AmigaSnailHotspot)
            : IsAtariSt ? (CursorImages.AtariStSnail, 16, AtariStSnailHotspot, AtariStSnailHotspot)
            : (CursorImages.Snail, SnailWidth, SnailHotspotX, SnailHotspotY);
        byte color = (Version == 0 ? CursorColorsV0 : CursorColors)[1];
        bool recolour = !IsAmiga && !IsAtariSt;
        var (mx, my) = Mouse;
        for (int i = 0; i < image.Length; i++)
        {
            int px = mx - hotX + i % width, py = my - hotY + i / width;
            if (image[i] != SnailClear && px >= 0 && px < screen.Width && py >= 0 && py < screen.Height)
            {
                screen[px, py] = recolour && image[i] == SnailMarked ? color : image[i];
            }
        }
    }
}
