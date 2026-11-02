using SDL;

namespace Maniacal.Player.Sdl;

/// <summary>
/// The cursor steered from the keyboard, as the original DOS interpreter allowed: the arrow keys
/// and the numpad's 2, 4, 6 and 8 move it, faster while a key is held; the numpad's 7, 9, 1 and 3
/// put it in the screen's corners; the numpad's 5 clicks. The game sees a mouse moved and clicked,
/// so a real mouse goes on working beside it. The numpad's digits are not typed for the game;
/// the digits above the letters are.
/// </summary>
internal sealed class KeyboardPointer
{
    private const int FirstStep = 2, MaxStep = 8;

    private int _held;

    /// <summary>What a key does to the cursor.</summary>
    internal enum Action
    {
        /// <summary>The key is not one that steers: it goes to the game.</summary>
        None,

        /// <summary>The cursor moves to the place given.</summary>
        Move,

        /// <summary>The cursor clicks where it is.</summary>
        Click,
    }

    /// <summary>
    /// The numpad's digit key at a place on the keyboard, whatever Num Lock makes it type (with it
    /// off, SDL may name the keys Home, Page Up and so on), or null for any other key.
    /// </summary>
    public static SDL_Keycode? Numpad(SDL_Scancode scancode) => scancode switch
    {
        >= SDL_Scancode.SDL_SCANCODE_KP_1 and <= SDL_Scancode.SDL_SCANCODE_KP_9
            => SDL_Keycode.SDLK_KP_1 + (uint)(scancode - SDL_Scancode.SDL_SCANCODE_KP_1),
        _ => null,
    };

    /// <summary>Whether a key is one that steers the cursor, pressed or let go.</summary>
    public static bool Steers(SDL_Keycode key) => Direction(key) is not null || Corner(key) || key == SDL_Keycode.SDLK_KP_5;

    /// <summary>
    /// A steering key pressed (or repeating, held) with the cursor at a place on a screen of a
    /// size: where the cursor goes, or that it clicks.
    /// </summary>
    public (Action Action, int X, int Y) Press(SDL_Keycode key, bool repeat, int x, int y, int width, int height)
    {
        if (key == SDL_Keycode.SDLK_KP_5)
        {
            return repeat ? (Action.None, x, y) : (Action.Click, x, y);
        }

        if (Corner(key))
        {
            return (Action.Move,
                key is SDL_Keycode.SDLK_KP_9 or SDL_Keycode.SDLK_KP_3 ? width - 1 : 0,
                key is SDL_Keycode.SDLK_KP_1 or SDL_Keycode.SDLK_KP_3 ? height - 1 : 0);
        }

        if (Direction(key) is not { } direction)
        {
            return (Action.None, x, y);
        }

        // a press moves a little; held, each repeat a pixel further, up to a limit
        _held = repeat ? Math.Min(_held + 1, MaxStep) : FirstStep;
        return (Action.Move,
            Math.Clamp(x + direction.Dx * _held, 0, width - 1),
            Math.Clamp(y + direction.Dy * _held, 0, height - 1));
    }

    private static (int Dx, int Dy)? Direction(SDL_Keycode key) => key switch
    {
        SDL_Keycode.SDLK_UP or SDL_Keycode.SDLK_KP_8 => (0, -1),
        SDL_Keycode.SDLK_DOWN or SDL_Keycode.SDLK_KP_2 => (0, 1),
        SDL_Keycode.SDLK_LEFT or SDL_Keycode.SDLK_KP_4 => (-1, 0),
        SDL_Keycode.SDLK_RIGHT or SDL_Keycode.SDLK_KP_6 => (1, 0),
        _ => null,
    };

    private static bool Corner(SDL_Keycode key)
        => key is SDL_Keycode.SDLK_KP_7 or SDL_Keycode.SDLK_KP_9 or SDL_Keycode.SDLK_KP_1 or SDL_Keycode.SDLK_KP_3;
}
