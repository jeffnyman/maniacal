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
    /// Opens the release, sets the display and the sound going, and boots the game. Throws
    /// <see cref="FileNotFoundException"/> or <see cref="InvalidDataException"/> as
    /// <see cref="ResourceLoader.Open"/> does.
    /// </summary>
    public override void Initialize()
    {
        _world = new GameWorld(ResourceLoader.Open(Game), Host.RandomSeed ?? Environment.TickCount, Host.Audio.SampleRate, Pcjr)
        {
            SaveStore = Host.Saves,
            SaveTarget = Detected.GameId,
        };
        _world.Engine.CopyProtection = CopyProtection;
        Host.Graphics.SetMode(ScreenLayout.Width, ScreenLayout.Height);
        Host.Graphics.SetTitle($"Maniacal - {Detected.Title}");
        Host.Graphics.SetPalette(Palettes.ForVersion(_world.Version));
        StartAudio(_world);
        _world.Boot();
    }

    /// <inheritdoc />
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
