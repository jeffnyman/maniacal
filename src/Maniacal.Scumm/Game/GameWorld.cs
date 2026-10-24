using Daventry.Core.Graphics;
using Maniacal.Scumm.Graphics;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Rooms;
using Maniacal.Scumm.Scripts;
using Maniacal.Scumm.View;

namespace Maniacal.Scumm.Game;

/// <summary>
/// The game as the scripts see it: the script engine, the current room and the camera, run a step
/// at a time as ScummVM's <c>scummLoop</c> does. It is the scripts' host for the instructions that
/// work on rooms and the camera; the others are recorded in <see cref="Unhandled"/> until their
/// subsystems exist.
/// </summary>
public sealed class GameWorld : IScriptHost
{
    // engine variables of version 2 (ScummEngine_v2::setupScummVars)
    public const int VarCurrentLights = 12, VarCameraMinX = 23, VarCameraMaxX = 24;

    /// <summary>The script <c>startScene</c> runs after every room's entry script in versions 1 and 2.</summary>
    public const int AfterEntryScript = 5;

    private readonly ResourceLoader _loader;
    private readonly RecordingHost _recorder = new();

    public GameWorld(ResourceLoader loader, int seed = 1)
    {
        _loader = loader;
        Engine = new ScriptEngine(
            loader.Index.Version,
            n => loader.Index.Locate(ResourceType.Script, n).IsPresent ? loader.Load(ResourceType.Script, n) : null,
            this,
            loader.Index.ObjectOwners,
            loader.Index.ObjectStates,
            seed);
    }

    public ScriptEngine Engine { get; }

    public Camera Camera { get; } = new();

    public int Version => Engine.Version;

    /// <summary>The room on screen, or null for room 0, no room.</summary>
    public Room? CurrentRoom { get; private set; }

    /// <summary>The world instructions with no subsystem yet, as the scripts ran them.</summary>
    public IReadOnlyList<string> Unhandled => _recorder.Calls;

    public int Steps { get; private set; }

    /// <summary>Starts the game: runs script 1, as <c>runBootscript</c> does for the full game.</summary>
    public void Boot() => Engine.RunScript(1);

    /// <summary>
    /// One step of the main loop, in the order of <c>scummLoop</c> for these versions: count the
    /// script delays down by the step's length (<c>VAR_TIMER_NEXT</c> ticks, at least 1), run every
    /// script once, then move the camera.
    /// </summary>
    public void Step()
    {
        Engine.DecreaseScriptDelay(Math.Max(1, Engine.Variables[ScriptEngine.VarTimerNext]));
        Engine.RunAllScripts();
        SyncCameraLimits();
        Camera.Move();
        Steps++;
    }

    /// <summary>The 320x200 screen: the room through the camera, with its objects in their states. Room 0 is black.</summary>
    public Surface8 Screen()
    {
        var layout = ScreenLayout.ForVersion(Version);
        if (CurrentRoom is not { } room)
        {
            return new Surface8(ScreenLayout.Width, ScreenLayout.Height);
        }

        var image = RoomRenderer.Render(room, Engine.GetState);
        return RoomView.Screen(RoomView.Window(image, Camera), layout);
    }

    public int Execute(ScriptEngine engine, Instruction instruction, IReadOnlyList<int> arguments)
    {
        switch (instruction.Handler)
        {
            // o5_loadRoom: old-bundle games only switch when the room changes
            case "o5_loadRoom":
                if (arguments[0] != (CurrentRoom?.Number ?? 0))
                {
                    StartScene(arguments[0]);
                }

                return 0;

            // o2_setCameraAt and o2_panCameraTo take 8-pixel columns
            case "o2_setCameraAt":
                SyncCameraLimits();
                Camera.SetAtEx(arguments[0] * 8);
                return 0;
            case "o2_panCameraTo":
                Camera.PanTo(arguments[0] * 8);
                return 0;

            // o2_roomOps sub-operation 1: the camera's limits, in columns, kept inside the room
            case "o2_roomOps" when (arguments[2] & 0x1F) == 1:
                {
                    int width = CurrentRoom?.Width ?? ScreenLayout.Width;
                    int Clamp(int column) => Math.Clamp(column * 8, ScreenLayout.Width / 2, Math.Max(ScreenLayout.Width / 2, width - ScreenLayout.Width / 2));
                    engine.Variables[VarCameraMinX] = Clamp(arguments[0]);
                    engine.Variables[VarCameraMaxX] = Clamp(arguments[1]);
                    return 0;
                }

            // o2_lights: the light level (version 1 translates it); a third byte of 1 sets the flashlight's size instead
            case "o2_lights":
                if (arguments[2] == 0)
                {
                    engine.Variables[VarCurrentLights] = Version == 1
                        ? arguments[0] switch { 2 => 11, 1 => 4, _ => 0 }
                        : arguments[0];
                }

                return 0;

            // the object state changes need nothing more: the screen is drawn from the states
            case "o2_setStateIntrinsicOn" or "o2_setStateIntrinsicOff" or "o2_setStateLocked" or "o2_setStateUnlocked"
                or "o2_setStateTouchable" or "o2_setStateUntouchable" or "o2_setStatePickupable" or "o2_setStateUnpickupable":
                return 0;

            default:
                return _recorder.Execute(engine, instruction, arguments);
        }
    }

    /// <summary>
    /// Changes room. Mirrors <c>startScene</c> for versions 1 and 2: the running room script stops,
    /// the old room's exit script runs and its scripts die, the room variable and camera limits
    /// are set, the new room's entry script runs, then script 5. The camera keeps its x, clamped to
    /// the new room; actors and the sentence are not there yet.
    /// </summary>
    public void StartScene(int number)
    {
        Engine.LeaveRoomScript();
        if (CurrentRoom is { } old)
        {
            Engine.RunRoomScript(ScriptEngine.ExitScriptNumber, old.ExitScript);
        }

        Engine.KillRoomScripts();
        Engine.Variables[ScriptEngine.VarRoom] = number;

        if (number == 0)
        {
            CurrentRoom = null;
            return;
        }

        var room = _loader.LoadRoom(number) ?? throw new InvalidOperationException($"Room {number} does not exist.");
        CurrentRoom = room;
        Engine.Variables[VarCameraMinX] = ScreenLayout.Width / 2;
        Engine.Variables[VarCameraMaxX] = room.Width - ScreenLayout.Width / 2;
        Camera.SetRoom(room.Width);
        SyncCameraLimits();

        Engine.RunRoomScript(ScriptEngine.EntryScriptNumber, room.EntryScript);
        Engine.RunScript(AfterEntryScript);
    }

    /// <summary>The camera reads its limits from the scripts' variables, as ScummVM's camera code does.</summary>
    private void SyncCameraLimits()
    {
        Camera.MinX = Engine.Variables[VarCameraMinX];
        Camera.MaxX = Engine.Variables[VarCameraMaxX];
    }
}
