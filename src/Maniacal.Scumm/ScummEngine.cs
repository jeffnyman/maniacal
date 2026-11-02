using Maniacal.Scumm.Game;
using Maniacal.Scumm.Graphics;
using Maniacal.Scumm.Hosting;
using Maniacal.Scumm.Input;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Storage;
using Maniacal.Scumm.View;

namespace Maniacal.Scumm;

/// <summary>
/// Maniac Mansion for a host: a step of the world each tick, the host's mouse and keys as the
/// game's input, the screen with the cursor presented, and the sound played. Mirrors ScummVM's
/// <c>ScummEngine::go</c> loop, which waits a step's ticks of the game's clock between steps.
/// <para>
/// Of the input events it reads <see cref="InputEventType.MouseMove"/> (where the mouse is),
/// <see cref="InputEventType.MouseDown"/> of the left button (a click) and the right (a right
/// click, which skips a cutscene), <see cref="InputEventType.KeyDown"/> (Escape, Enter, Backspace,
/// F1 to F9 and the characters; Ctrl+C and Alt+X quit, as in ScummVM), and
/// <see cref="InputEventType.Quit"/>; the others it passes over.
/// Mouse positions are on the screen it sets with <c>SetMode</c>: 320x200, or the NES's 256x240.
/// </para>
/// <para>
/// It calls the host's drivers only from the thread that calls <see cref="Initialize"/>,
/// <see cref="Tick"/> and <see cref="Dispose"/>, which are made from one thread at a time. The
/// audio source it gives <c>IAudioDriver.Start</c> may be read from another thread.
/// </para>
/// </summary>
public sealed class ScummEngine : Engine
{
    private GameWorld? _world;

    // the frames of a room transition still to show, and the screen of the step that made them
    private readonly Queue<TransitionFrame> _transition = new();
    private Maniacal.Scumm.Graphics.Surface8? _stepScreen;

    /// <summary>The engine for a detected release, on a host. Nothing is read until <see cref="Initialize"/>.</summary>
    public ScummEngine(IHost host, IGameContainer game, DetectedGame detected)
        : base(host, game, detected)
    {
    }

    /// <summary>Whether the sound is the PCjr's, as ScummVM chooses by default, or the PC speaker's. Set before <see cref="Initialize"/>.</summary>
    public bool Pcjr { get; set; } = true;

    /// <summary>Whether the game's copy protection is kept (the security door's code). Off by default, as in ScummVM. Set before <see cref="Initialize"/>.</summary>
    public bool CopyProtection { get; set; }

    /// <summary>The world being played, from <see cref="Initialize"/> on.</summary>
    public GameWorld World => _world ?? throw new InvalidOperationException("The engine is not initialized.");

    /// <summary>
    /// The window's title: the game and, after it, the release in words, as a player knows it:
    /// <c>Maniacal - Maniac Mansion (DOS Enhanced)</c>, <c>(Commodore 64)</c>, <c>(NES)</c>.
    /// </summary>
    internal static string WindowTitle(DetectedGame detected)
    {
        string release = (detected.PlatformName?.ToLowerInvariant(), detected.Extra) switch
        {
            ("c64", "C64 Demo") => "Commodore 64 Demo",
            ("c64", _) => "Commodore 64",
            ("nes", _) => "NES",
            ("atari", _) => "Atari ST",
            ("amiga", _) => "Amiga",
            (_, "V1") => "DOS Original",
            (_, "V1 Demo") => "DOS Original Demo",
            (_, "V2") => "DOS Enhanced",
            (_, "V2 Demo") => "DOS Enhanced Demo",
            (_, var extra) => extra ?? string.Empty,
        };

        return release.Length == 0 ? $"Maniacal - {detected.Title}" : $"Maniacal - {detected.Title} ({release})";
    }

    /// <summary>
    /// Opens the release, sets the display and the sound going, and boots the game. Throws
    /// <see cref="NotSupportedException"/>, <see cref="FileNotFoundException"/> or
    /// <see cref="InvalidDataException"/> as <see cref="ResourceLoader.Open"/> does.
    /// </summary>
    public override void Initialize()
    {
        _world = new GameWorld(ResourceLoader.Open(Game), Host.RandomSeed ?? Environment.TickCount, Host.Audio.SampleRate, Pcjr)
        {
            SaveStore = Host.Saves,
            SaveTarget = Detected.GameId,
        };
        _world.Engine.CopyProtection = CopyProtection;
        Host.Graphics.SetMode(_world.Layout.Width, _world.Layout.Height);
        Host.Graphics.SetTitle(WindowTitle(Detected));
        Host.Graphics.SetPalette(_world.Palette.Span);
        StartAudio(_world);
        _world.Boot();
    }

    /// <summary>
    /// Plays one step of the game, whatever <paramref name="elapsed"/> says (this engine does not
    /// read it): the input waiting, the step, the frame presented. The delay returned is the
    /// step's length on the game's own clock (or a room transition frame's), so a host that calls
    /// again after it plays at the original's speed, and one on a clock of its own, a recording,
    /// at whatever speed it likes. <see cref="TickResult.Quit"/> once the game asks to quit.
    /// </summary>
    /// <param name="elapsed">Not read by this engine.</param>
    public override TickResult Tick(TimeSpan elapsed)
    {
        var world = World;
        while (Host.Input.TryRead(out var input))
        {
            switch (input.Type)
            {
                case InputEventType.Quit:
                    return TickResult.Quit;
                case InputEventType.MouseMove:
                    world.MoveMouse(input.X, input.Y);
                    break;
                case InputEventType.MouseDown when input.Button is MouseButton.Left or MouseButton.Right:
                    world.Click(input.X, input.Y, right: input.Button == MouseButton.Right);
                    break;
                // Ctrl+C and Alt+X quit, as ScummVM has them (processKeyboard)
                case InputEventType.KeyDown when IsQuitKey(input):
                    return TickResult.Quit;
                case InputEventType.KeyDown when KeyCode(input) is { } key:
                    world.PressKey(key);
                    break;
            }
        }

        // a room transition's frames show first, each for its quarter-frames, the game standing
        // still; then the step's own screen, for the step's ticks
        if (_transition.Count > 0)
        {
            var frame = _transition.Dequeue();
            Present(world, frame.Screen);
            return TickResult.After(TimeSpan.FromSeconds(frame.QuarterFrames / world.TimerFrequency));
        }

        if (_stepScreen is { } stepScreen)
        {
            _stepScreen = null;
            Present(world, stepScreen);
            return TickResult.After(TimeSpan.FromSeconds(world.StepTicks * 4 / world.TimerFrequency));
        }

        world.Step();
        foreach (var frame in world.Transition)
        {
            _transition.Enqueue(frame);
        }

        if (_transition.Count > 0)
        {
            _stepScreen = world.Screen();
            var first = _transition.Dequeue();
            Present(world, first.Screen);
            return TickResult.After(TimeSpan.FromSeconds(first.QuarterFrames / world.TimerFrequency));
        }

        Present(world, world.Screen());
        return TickResult.After(TimeSpan.FromSeconds(world.StepTicks * 4 / world.TimerFrequency));
    }

    private void Present(GameWorld world, Maniacal.Scumm.Graphics.Surface8 screen)
    {
        world.DrawCursor(screen);
        Host.Graphics.Present(screen);
    }

    /// <summary>Stops the sound.</summary>
    public override void Dispose()
    {
        Host.Audio.Stop();
        base.Dispose();
    }

    /// <summary>No audio device is not fatal: the sound clocks itself, so scripts that wait for music still go on.</summary>
    private void StartAudio(GameWorld world)
    {
        try
        {
            Host.Audio.Start(world.Sound.Output);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Host.Log.Warning("maniacal.sound", $"Audio output unavailable, running silent: {ex.Message}");
        }
    }

    // Ctrl+C or Alt+X, and no other modifier but the locks
    private static bool IsQuitKey(InputEvent input)
    {
        var held = input.Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Meta);
        return (input.Key == (Key)'c' && held == KeyModifiers.Ctrl) || (input.Key == (Key)'x' && held == KeyModifiers.Alt);
    }

    /// <summary>
    /// The game's code for a key: Escape, Return and Backspace, the function keys from 315 as
    /// ScummVM numbers them, or the character typed.
    /// </summary>
    private static int? KeyCode(InputEvent input) => input.Key switch
    {
        Key.Escape => GameWorld.KeyEscape,
        Key.Return or Key.KeypadEnter => 13,
        Key.Backspace => 8,
        >= Key.F1 and <= Key.F9 => GameWorld.KeyF1 + (input.Key - Key.F1),
        _ when input.Ascii != '\0' => input.Ascii,
        _ => null,
    };
}
