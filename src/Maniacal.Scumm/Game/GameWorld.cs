using System.Text;
using Daventry.Core.Graphics;
using Maniacal.Scumm.Costumes;
using Maniacal.Scumm.Graphics;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Rooms;
using Maniacal.Scumm.Scripts;
using Maniacal.Scumm.View;

namespace Maniacal.Scumm.Game;

/// <summary>
/// The game as the scripts see it: the script engine, the current room, the camera and the actors,
/// run a step at a time as ScummVM's <c>scummLoop</c> does. It is the scripts' host for the
/// instructions that work on rooms, the camera and actors; the others are recorded in
/// <see cref="Unhandled"/> until their subsystems exist.
/// </summary>
public sealed class GameWorld : IScriptHost
{
    // engine variables of version 2 (ScummEngine_v2::setupScummVars)
    public const int VarCurrentLights = 12, VarCameraMinX = 23, VarCameraMaxX = 24;

    /// <summary>The script <c>startScene</c> runs after every room's entry script in versions 1 and 2.</summary>
    public const int AfterEntryScript = 5;

    /// <summary>Mirrors <c>_numActors</c> for Maniac Mansion; actor 0 is not used.</summary>
    public const int ActorCount = 25;

    /// <summary>The light bit that draws actors in their own colours. Mirrors <c>LIGHTMODE_actor_use_colors</c>.</summary>
    public const int LightActorColors = 8;

    /// <summary>The talk colours version 1 actors start with. Mirrors <c>v1MMActorTalkColor</c>.</summary>
    private static readonly int[] V1TalkColors = [1, 7, 2, 14, 8, 1, 3, 7, 7, 12, 1, 13, 1, 4, 5, 5, 4, 3, 1, 5, 1, 1, 1, 7, 7];

    private readonly ResourceLoader _loader;
    private readonly RecordingHost _recorder = new();
    private readonly Dictionary<int, Costume?> _costumes = [];
    private Surface8? _frame;

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
        Actors = new Actor[ActorCount];
        for (int i = 0; i < ActorCount; i++)
        {
            Actors[i] = new Actor(i, LoadCostume, () => CurrentRoom?.Number ?? 0);

            // resetScumm: version 1 actors start in the costume of their number, and with their own talk colours
            if (Version <= 1)
            {
                Actors[i].SetCostume(i);
                Actors[i].TalkColor = V1TalkColors[i];
            }
        }
    }

    public ScriptEngine Engine { get; }

    public Camera Camera { get; } = new();

    public Actor[] Actors { get; }

    public int Version => Engine.Version;

    /// <summary>The room on screen, or null for room 0, no room.</summary>
    public Room? CurrentRoom { get; private set; }

    /// <summary>The colour of text not said by an actor, set by <c>actorOps</c> on actor 0. Mirrors <c>_string[0].color</c>.</summary>
    public int StringColor { get; private set; } = 15;

    /// <summary>The world instructions with no subsystem yet, as the scripts ran them.</summary>
    public IReadOnlyList<string> Unhandled => _recorder.Calls;

    public int Steps { get; private set; }

    /// <summary>Starts the game: runs script 1, as <c>runBootscript</c> does for the full game.</summary>
    public void Boot() => Engine.RunScript(1);

    /// <summary>
    /// One step of the main loop, in the order of <c>scummLoop</c> for these versions: count the
    /// script delays down by the step's length (<c>VAR_TIMER_NEXT</c> ticks, at least 1), run every
    /// script once, move the camera, draw the frame, then animate the actors in the room, which
    /// <c>processActors</c> does after drawing them.
    /// </summary>
    public void Step()
    {
        Engine.DecreaseScriptDelay(Math.Max(1, Engine.Variables[ScriptEngine.VarTimerNext]));
        Engine.RunAllScripts();
        SyncCameraLimits();
        var followed = Camera.Mode == CameraMode.FollowActor ? Actors[Camera.Follows] : null;
        Camera.Move(followed is null ? null : () => followed.X * 8);
        _frame = Draw();
        foreach (var actor in ActorsInRoom())
        {
            actor.AnimateCostume();
        }

        Steps++;
    }

    /// <summary>The 320x200 screen as the last step drew it (or as it is now, before any step). Room 0 is black.</summary>
    public Surface8 Screen() => _frame ?? Draw();

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

            // o2_actorOps: sound, palette (version 2 names the colour; version 1 uses the actor number), name, costume, talk colour
            case "o2_actorOps":
                ActorOps(arguments, instruction);
                return 0;

            case "o2_putActor":
                ActorOf(arguments[0]).Put(arguments[1], arguments[2], version: Version);
                return 0;

            // o2_putActorInRoom: only the room changes, unless it is room 0, which puts the actor nowhere
            case "o2_putActorInRoom":
                {
                    var actor = ActorOf(arguments[0]);
                    actor.Room = arguments[1];
                    if (arguments[1] == 0)
                    {
                        if (Version <= 1)
                        {
                            actor.SetDirection(180);
                        }

                        actor.Put(0, 0, 0, Version);
                    }

                    return 0;
                }

            // o2_putActorAtObject: at the object's walk-to point, or (30, 60) when the object is not here
            case "o2_putActorAtObject":
                {
                    var actor = ActorOf(arguments[0]);
                    var (x, y) = CurrentRoom?.FindObject(arguments[1]) is { } obj ? (obj.WalkX / 8, obj.WalkY / 2) : (30, 60);
                    actor.Put(x, y, version: Version);
                    return 0;
                }

            case "o5_animateActor":
                ActorOf(arguments[0]).Animate(arguments[1]);
                return 0;

            case "o5_actorFollowCamera":
                FollowActor(ActorOf(arguments[0]));
                return 0;

            case "o2_loadRoomWithEgo":
                LoadRoomWithEgo(arguments[0], arguments[1], arguments[2], arguments[3]);
                return 0;

            case "o5_getActorRoom":
                return arguments[0] is > 0 and < ActorCount ? Actors[arguments[0]].Room : 0;
            case "o5_getActorCostume":
                return ActorOf(arguments[0]).CostumeNumber;
            case "o5_getActorFacing":
                return ActorOf(arguments[0]).OldDirection;
            case "o5_getActorMoving":
                return 0;
            case "o2_getActorX":
                return ActorOf(arguments[0]).X;
            case "o2_getActorY":
                return ActorOf(arguments[0]).Y;
            case "o2_getActorElevation":
                return ActorOf(arguments[0]).Elevation;
            case "o2_setActorElevation":
                ActorOf(arguments[0]).Elevation = (sbyte)arguments[1];
                return 0;
            case "o2_getActorWalkBox":
                {
                    var actor = ActorOf(arguments[0]);
                    return actor.InCurrentRoom && CurrentRoom is { } room ? room.WalkBoxes.FindBox(actor.X, actor.Y) & 0xFF : 0xFF;
                }

            default:
                return _recorder.Execute(engine, instruction, arguments);
        }
    }

    /// <summary>
    /// Changes room. Mirrors <c>startScene</c> for versions 1 and 2: the running room script stops,
    /// the old room's exit script runs and its scripts die, the actors hide, the room variable and
    /// camera limits are set, the actors in the new room show, its entry script runs, then script
    /// 5. The camera keeps its x, clamped to the new room; the sentence is not there yet.
    /// </summary>
    public void StartScene(int number)
    {
        Engine.LeaveRoomScript();
        if (CurrentRoom is { } old)
        {
            Engine.RunRoomScript(ScriptEngine.ExitScriptNumber, old.ExitScript);
        }

        Engine.KillRoomScripts();
        foreach (var actor in Actors.Skip(1))
        {
            actor.Hide();
        }

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

        // showActors
        foreach (var actor in ActorsInRoom())
        {
            actor.Show();
        }

        Engine.RunRoomScript(ScriptEngine.EntryScriptNumber, room.EntryScript);
        Engine.RunScript(AfterEntryScript);
    }

    /// <summary>
    /// The camera follows an actor, going to its room first when it is elsewhere. Mirrors
    /// <c>actorFollowCamera</c> and <c>setCameraFollows</c>.
    /// </summary>
    public void FollowActor(Actor actor)
    {
        if (!actor.InCurrentRoom)
        {
            StartScene(actor.Room);
            SyncCameraLimits();
            Camera.SetAt(actor.X * 8);
        }

        SyncCameraLimits();
        Camera.Follow(actor.Number, actor.X * 8);
    }

    /// <summary>
    /// The kid (<c>VAR_EGO</c>) goes into a room at an object: the scene changes, the kid stands at
    /// the object's walk-to point facing away from it, and the camera follows. A walk target of
    /// (x, y) at or above 0 sets the kid walking, once there is walking. Mirrors
    /// <c>o2_loadRoomWithEgo</c>; snapping the point into a walkbox comes with the walkboxes.
    /// </summary>
    public void LoadRoomWithEgo(int obj, int room, int walkX, int walkY)
    {
        var ego = ActorOf(Engine.Variables[ScriptEngine.VarEgo]);
        ego.Put(ego.X, ego.Y, room, Version);
        StartScene(room);

        if (CurrentRoom?.FindObject(obj) is { } target)
        {
            ego.Put(target.WalkX / 8, target.WalkY / 2, CurrentRoom.Number, Version);
            ego.SetDirection(Actor.OldDirToNewDir(target.ActorDir & 3) + 180);
        }

        SyncCameraLimits();
        Camera.SetAtEx(ego.X * 8);
        Camera.Follow(ego.Number, ego.X * 8, setCamera: true);
        if (walkX >= 0 && walkY >= 0)
        {
            _recorder.Calls.Add($"walk ego to ({walkX}, {walkY}) after loadRoomWithEgo");
        }

        Engine.RunScript(AfterEntryScript);
    }

    /// <summary>
    /// The room through the camera, with its objects in their states and its actors from the back
    /// (top of the screen) to the front. Mirrors <c>processActors</c>' sort by y.
    /// </summary>
    private Surface8 Draw()
    {
        var layout = ScreenLayout.ForVersion(Version);
        if (CurrentRoom is not { } room)
        {
            return new Surface8(ScreenLayout.Width, ScreenLayout.Height);
        }

        var view = RoomView.Window(RoomRenderer.Render(room, Engine.GetState), Camera);
        bool colors = (Engine.Variables[VarCurrentLights] & LightActorColors) != 0;
        foreach (var actor in ActorsInRoom().Where(a => a.Visible && a.Costume is not null).OrderBy(a => a.Y))
        {
            var costume = actor.Costume!;
            var palette = CostumeRenderer.Palette(costume, actor.Number, actor.Palette, colors);
            var frame = CostumeRenderer.DrawLimbs(costume, actor.State, actor.OldDirection, palette);
            int box = room.WalkBoxes.FindBox(actor.X, actor.Y);
            bool behind = box >= 0 && room.WalkBoxes.Boxes[box].Mask != 0;
            RoomView.DrawActor(view, Version, Camera, new ActorDrawing(frame, actor.X, actor.Y, actor.Facing == 270, behind, actor.Elevation));
        }

        return RoomView.Screen(view, layout);
    }

    private void ActorOps(IReadOnlyList<int> arguments, Instruction instruction)
    {
        int sub = arguments[2];
        if (arguments[0] == 0 && sub == 5)
        {
            StringColor = arguments[1];
            return;
        }

        var actor = ActorOf(arguments[0]);
        switch (sub)
        {
            case 1:
                actor.Sound = arguments[1];
                break;
            case 2:
                actor.Palette[(Version == 1 ? arguments[0] : arguments[3]) & 0x0F] = (byte)arguments[1];
                break;
            case 3:
                actor.Name = Encoding.Latin1.GetString(instruction.Operands[3].Text!);
                break;
            case 4:
                actor.SetCostume(arguments[1]);
                break;
            case 5:
                actor.TalkColor = arguments[1];
                break;
        }
    }

    private IEnumerable<Actor> ActorsInRoom() => Actors.Skip(1).Where(a => a.InCurrentRoom);

    private Costume? LoadCostume(int number)
    {
        if (!_costumes.TryGetValue(number, out var costume))
        {
            costume = _loader.LoadCostume(number);
            _costumes[number] = costume;
        }

        return costume;
    }

    private Actor ActorOf(int number)
        => number is > 0 and < ActorCount ? Actors[number] : throw new InvalidOperationException($"Actor {number} is not one of 1 to {ActorCount - 1}.");

    /// <summary>The camera reads its limits from the scripts' variables, as ScummVM's camera code does.</summary>
    private void SyncCameraLimits()
    {
        Camera.MinX = Engine.Variables[VarCameraMinX];
        Camera.MaxX = Engine.Variables[VarCameraMaxX];
    }
}
