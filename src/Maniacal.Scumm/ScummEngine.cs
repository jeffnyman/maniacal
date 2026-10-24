using Daventry.Core.Graphics;
using Daventry.Core.Hosting;
using Daventry.Core.Input;
using Daventry.Core.Storage;
using Maniacal.Scumm.Game;
using Maniacal.Scumm.Graphics;
using Maniacal.Scumm.Resources;
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

    public ScummEngine(IHost host, IGameContainer game, DetectedGame detected)
        : base(host, game, detected)
    {
    }

    /// <summary>Whether the sound is the PCjr's, as ScummVM chooses by default, or the PC speaker's. Set before <see cref="Initialize"/>.</summary>
    public bool Pcjr { get; set; } = true;

    /// <summary>The world being played, from <see cref="Initialize"/> on.</summary>
    public GameWorld World => _world ?? throw new InvalidOperationException("The engine is not initialized.");

    public override void Initialize()
    {
        _world = new GameWorld(ResourceLoader.Open(Game), Host.RandomSeed ?? Environment.TickCount, Host.Audio.SampleRate, Pcjr);
        Host.Graphics.SetMode(ScreenLayout.Width, ScreenLayout.Height);
        Host.Graphics.SetTitle($"Maniacal - {Detected.Title}");
        Host.Graphics.SetPalette(Palettes.Ega);
        StartAudio(_world);
        _world.Boot();
    }

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

        world.Step();
        var screen = world.Screen();
        world.DrawCursor(screen);
        Host.Graphics.Present(screen);

        return TickResult.After(world.StepLength);
    }

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
