using System.Text;
using Maniacal.Scumm.Costumes;
using Maniacal.Scumm.Graphics;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Rooms;
using Maniacal.Scumm.Scripts;
using Maniacal.Scumm.Sound;
using Maniacal.Scumm.View;

namespace Maniacal.Scumm.Game;

/// <summary>
/// The game as the scripts see it: the script engine, the current room, the camera and the actors,
/// run a step at a time as ScummVM's <c>scummLoop</c> does. It is the scripts' host for the
/// instructions that work on rooms, the camera and actors; the others are recorded in
/// <see cref="Unhandled"/> until their subsystems exist.
/// </summary>
public sealed partial class GameWorld : IScriptHost, IActorWorld
{
    // engine variables of version 2 (ScummEngine_v2::setupScummVars)
    public const int VarCameraPosX = 2, VarCurrentLights = 12, VarMusicTimer = 17, VarActorRangeMin = 19, VarActorRangeMax = 20,
        VarCameraMinX = 23, VarCameraMaxX = 24, VarLastSound = 37;

    /// <summary>The sample rate the world's sound is made at when no host says otherwise.</summary>
    public const int DefaultSampleRate = 22050;

    /// <summary>The script <c>startScene</c> runs after every room's entry script in versions 1 and 2.</summary>
    public const int AfterEntryScript = 5;

    /// <summary>Mirrors <c>_numActors</c> for Maniac Mansion; actor 0 is not used.</summary>
    public const int ActorCount = 25;

    /// <summary>The light bit that draws actors in their own colours. Mirrors <c>LIGHTMODE_actor_use_colors</c>.</summary>
    public const int LightActorColors = 8;

    /// <summary>The light bit that draws actors in their costume's colours. Mirrors <c>LIGHTMODE_actor_use_base_palette</c>.</summary>
    public const int LightActorBasePalette = 1;

    /// <summary>The talk colours version 1 actors start with. Mirrors <c>v1MMActorTalkColor</c>.</summary>
    private static readonly int[] V1TalkColors = [1, 7, 2, 14, 8, 1, 3, 7, 7, 12, 1, 13, 1, 4, 5, 5, 4, 3, 1, 5, 1, 1, 1, 7, 7];

    private readonly ResourceLoader _loader;

    // the world before the boot script, which a restart goes back to
    private readonly Saves.ScummState _initial;
    private readonly RecordingHost _recorder = new();
    private readonly Dictionary<int, Costume?> _costumes = [];
    private readonly Dictionary<int, V0Costume?> _costumesV0 = [];
    private readonly Dictionary<int, byte[]?> _sounds = [];
    private Surface8? _frame;
    private byte[] _boxFlags = [];

    // where drawObject moved objects of the current room to, in pixels; the room's own data is kept
    private readonly Dictionary<int, (int X, int Y)> _moved = [];

    // the names setObjectName gave objects (_newNames)
    private readonly Dictionary<int, string> _newNames = [];
    private (int Strips, int Rows) _flashlight = (Flashlight.DefaultStrips, Flashlight.DefaultRows);

    /// <param name="loader">The release's resources.</param>
    /// <param name="seed">The seed of the scripts' random numbers.</param>
    /// <param name="sampleRate">The rate the sound is made at.</param>
    /// <param name="pcjr">Whether the sound is the PCjr's rather than the PC speaker's.</param>
    public GameWorld(ResourceLoader loader, int seed = 1, int sampleRate = DefaultSampleRate, bool pcjr = true)
    {
        _loader = loader;
        Sound = new SoundSystem(loader.Index.Version, LoadSound, sampleRate, pcjr);
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
            Actors[i] = Version == 0 ? new ActorV0(i, this) : new Actor(i, this);

            // resetScumm: version 0 and 1 actors start in the costume of their number, and with their own
            // talk colours; version 0's names are the engine's own (getActorName)
            if (Version <= 1)
            {
                Actors[i].SetCostume(i);
                Actors[i].TalkColor = Version == 0 ? V0Actors.TalkColors[i] : V1TalkColors[i];
            }

            if (Version == 0 && i > 0)
            {
                Actors[i].Name = V0Actors.NamesEnglish[i - 1];
            }
        }

        var font = Font.English(Version);
        Messages = new MessageLine(font, Engine, n => n is > 0 and < ActorCount ? Actors[n] : null, NameOf, VerbName);
        Verbs = new VerbArea(font, ScreenLayout.ForVersion(Version).RoomBottom, () => Engine.Interface, c64: Version == 0);

        // ScummEngine_v0::resetScumm: the C64's engine makes its verbs itself, and resetScummVars
        // resets the sentence and starts it with Dave as the kid
        if (Version == 0)
        {
            ResetVerbsV0();
            ResetSentenceV0();
            Engine.Variables[ScriptEngine.VarEgo] = V0FirstKid;
        }
        _initial = Capture();
    }

    internal ScriptEngine Engine { get; }

    /// <summary>The music and sound effects.</summary>
    internal SoundSystem Sound { get; }

    /// <summary>
    /// How fast the game's clock runs, in quarter-frames a second: 1193182 / 5041 for version 2,
    /// and for version 1 twelve a tick of the PC's 18.2 Hz timer. Mirrors <c>setTimerAndShakeFrequency</c>.
    /// </summary>
    public double TimerFrequency => Version switch
    {
        0 => C64NtscTimerRate,
        1 => 1193182.0 / 65536 * 12,
        _ => 1193182.0 / 5041,
    };

    // the C64's screen refresh in quarter-frames a second (C64_NTSC_VBLANK_RATE)
    private const double C64NtscTimerRate = 240.0;

    /// <summary>
    /// The ticks the next step lasts: <c>VAR_TIMER_NEXT</c>, at least 1, and in version 1 rounded
    /// up to a multiple of 3; version 0 has no such variable and takes 4, and as many more as the C64
    /// took to draw the last step (<c>DelayCalculateDelta</c>). Mirrors the <c>delta</c> of
    /// <c>ScummEngine::go</c>.
    /// </summary>
    public int StepTicks
    {
        get
        {
            const int NoTimerNext = 4;
            int timerNext = Engine.Vars.TimerNext;
            int delta = Math.Max(1, timerNext == EngineVariables.None ? NoTimerNext : Engine.Variables[timerNext]);
            return Version switch
            {
                0 => delta + _redrawTicks,
                1 => (delta + 2) / 3 * 3,
                _ => delta,
            };
        }
    }

    /// <summary>
    /// How long the next step lasts: four quarter-frames a tick (<c>waitForTimer(delta * 4)</c>),
    /// and before it the last step's room transitions (<see cref="Transition"/>).
    /// </summary>
    public TimeSpan StepLength => TimeSpan.FromSeconds((StepTicks * 4 + TransitionQuarterFrames) / TimerFrequency);

    /// <summary>The game time the steps so far have taken.</summary>
    public TimeSpan Time { get; private set; }

    public Camera Camera { get; } = new();

    internal Actor[] Actors { get; }

    public int Version => Engine.Version;

    /// <summary>The room on screen, or null for room 0, no room.</summary>
    public Room? CurrentRoom { get; private set; }

    /// <summary>The message lines above the room.</summary>
    internal MessageLine Messages { get; }

    /// <summary>The world instructions with no subsystem yet, as the scripts ran them.</summary>
    public IReadOnlyList<string> Unhandled => _recorder.Calls;

    public int Steps { get; private set; }

    int IActorWorld.CurrentRoom => CurrentRoom?.Number ?? 0;

    int IActorWorld.BoxCount => CurrentRoom?.WalkBoxes.Count ?? 0;

    BoxCorners IActorWorld.BoxCorners(int box) => Rooms.BoxCorners.Of(CurrentRoom!.WalkBoxes.Boxes[box]);

    /// <summary>A box's flags as the scripts have left them (<c>setBoxFlags</c>), or 0 for no box.</summary>
    public int BoxFlags(int box) => box >= 0 && box < _boxFlags.Length ? _boxFlags[box] : 0;

    int IActorWorld.NextBox(int from, int to)
    {
        if (from == to)
        {
            return to;
        }

        if (to == BoxGeometry.NoBox)
        {
            return -1;
        }

        if (from == BoxGeometry.NoBox)
        {
            return to;
        }

        return CurrentRoom!.WalkBoxes.NextBox(from, to);
    }

    /// <summary>
    /// Whether the player controls an actor: in version 1 the kid being played, in version 2 the
    /// actors from Var[42] to Var[43]. Mirrors <c>Actor_v2::isPlayer</c>.
    /// </summary>
    public bool IsPlayer(int actor) => Version == 1
        ? actor == Engine.Variables[ScriptEngine.VarEgo]
        : Engine.Variables[42] <= actor && actor <= Engine.Variables[43];

    Costume? IActorWorld.LoadCostume(int costume) => LoadCostume(costume);

    int IActorWorld.BoxMask(int box) => CurrentRoom is { } room && box >= 0 && box < room.WalkBoxes.Count ? room.WalkBoxes.Boxes[box].Mask : 0;

    IReadOnlyList<byte> IActorWorld.BoxNeighbours(int box)
        => CurrentRoom?.WalkBoxes.Neighbours is { } neighbours && box >= 0 && box < neighbours.Count ? neighbours[box] : [];

    bool IActorWorld.AreBoxesNeighbours(int box1, int box2)
        => CurrentRoom is { } room && box1 >= 0 && box1 < room.WalkBoxes.Count && room.WalkBoxes.AreNeighboursV0(box1, box2);

    V0Costume? IActorWorld.LoadCostumeV0(int costume)
    {
        if (!_costumesV0.TryGetValue(costume, out var loaded))
        {
            loaded = _loader.LoadCostumeV0(costume);
            _costumesV0[costume] = loaded;
        }

        return loaded;
    }

    /// <summary>Starts the game: runs script 1, as <c>runBootscript</c> does for the full game.</summary>
    public void Boot() => Engine.RunScript(1);

    /// <summary>
    /// One step of the main loop, in the order of <c>scummLoop</c> for these versions: count the
    /// script delays down by the step's length (<c>VAR_TIMER_NEXT</c> ticks, at least 1), move the
    /// message on, take the input, run every script once, hand the input to the input script, run a
    /// waiting sentence, walk the actors, move the camera, draw the frame, animate the actors in the
    /// room (which <c>processActors</c> does after drawing them, and in version 0 before), then move
    /// the highlights to the mouse. Before the scripts run, the variables the engine keeps for them
    /// are brought up to date (<c>scummLoop_updateScummVars</c>).
    /// </summary>
    public void Step()
    {
        int oldEgo = Engine.Variables[ScriptEngine.VarEgo];

        // ScummEngine_v0::scummLoop: whether the last sound started still plays
        SetVar(Engine.Vars.IsSoundRunning, Sound.LastSound != 0 && Sound.IsRunning(Sound.LastSound) ? 1 : 0);

        // scummLoop: the step's ticks, at most 15, count the script delays and the message down;
        // then the message moves on (displayDialog, which versions up to 3 run before input)
        Time += StepLength;
        _transition.Clear();
        _display = null;
        int ticks = Math.Min(StepTicks, 15);
        Engine.DecreaseScriptDelay(ticks);
        Messages.CountDown(ticks);
        Messages.Display();

        // version 0's input, verbs and sentences are its own (ScummEngine_v0::processInput, checkExecVerbs)
        if (Version == 0)
        {
            ProcessInputV0();
        }
        else
        {
            ProcessInput();
        }

        // scummLoop_updateScummVars: the camera's column, whether a message shows, the mouse in the room
        Engine.Variables[VarCameraPosX] = Camera.X >> 3;
        Engine.Variables[ScriptEngine.VarHaveMessage] = Messages.HaveMessage;
        if (Version >= 1)
        {
            UpdateVirtualMouse();
        }

        // updateMusicTimer: the player's timer, in quarter-frames of 240 a second
        if (Engine.Vars.MusicTimer != EngineVariables.None)
        {
            Engine.Variables[Engine.Vars.MusicTimer] = (int)(Sound.Player.MusicTimer * TimerFrequency / 240.0);
        }

        Engine.RunAllScripts();
        if (Version == 0)
        {
            CheckExecVerbsV0();
            CheckAndRunSentenceScriptV0();
        }
        else
        {
            CheckExecVerbs();
            CheckAndRunSentenceScript();
        }
        foreach (var actor in ActorsInRoom())
        {
            actor.Walk();
        }

        SyncCameraLimits();
        var followed = Camera.Mode == CameraMode.FollowActor ? Actors[Camera.Follows] : null;
        Camera.Move(followed is null ? null : () => followed.X * 8);
        if (Version == 0)
        {
            NoteRoomRedrawV0();
            AnimateActorsV0();
            _fullRedraw = false;
        }

        _frame = Draw();
        if (Version >= 1)
        {
            foreach (var actor in ActorsInRoom())
            {
                actor.AnimateCostume();
            }
        }

        // scummLoop_handleEffects: a room just entered shows with its effect
        if (_doEffect)
        {
            _doEffect = false;
            FadeIn(_newEffect, Screen());
        }

        HandleMouseOver(oldEgo != Engine.Variables[ScriptEngine.VarEgo]);

        // scummLoop_handleSound: the sounds asked for go to the player
        Sound.ProcessQueue();
        Sound.Update(Time);

        // the camera's last place (camera._last), and the time the C64 took to draw this step
        _lastCameraX = Camera.X;
        if (Version == 0)
        {
            _redrawTicks = DelayCalculateDelta();
        }

        _cursorFrame++;
        Steps++;
    }

    /// <summary>
    /// The 320x200 screen as the last step drew it (or as it is now, before any step), with the
    /// verbs as they are now and without the cursor (<see cref="DrawCursor"/>). Room 0 is black.
    /// </summary>
    public Surface8 Screen()
    {
        var screen = _frame ?? Draw();
        for (int y = 0; y < Verbs.Pixels.Height; y++)
        {
            Verbs.Pixels.Row(y).CopyTo(screen.Row(Verbs.Top + y));
        }

        return screen;
    }

    int IScriptHost.Execute(ScriptEngine engine, Instruction instruction, IReadOnlyList<int> arguments)
    {
        if (Version == 0 && ExecuteV0(engine, instruction, arguments, out int v0Result))
        {
            return v0Result;
        }

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
                else if (arguments[2] == 1)
                {
                    _flashlight = (arguments[0], arguments[1]);
                }

                return 0;

            // the object state changes need nothing more, the screen being drawn from the states, but
            // an image turned on or off is redrawn (markObjectRectAsDirty)
            case "o2_setStateIntrinsicOn" or "o2_setStateIntrinsicOff":
                MarkObjectRectAsDirty(arguments[0]);
                return 0;
            case "o2_setStateLocked" or "o2_setStateUnlocked"
                or "o2_setStateTouchable" or "o2_setStateUntouchable" or "o2_setStatePickupable" or "o2_setStateUnpickupable":
                return 0;

            // o2_actorOps: sound, palette (version 2 names the colour; version 1 uses the actor number), name, costume, talk colour
            case "o2_actorOps":
                ActorOps(arguments, instruction);
                return 0;

            case "o2_putActor":
                PutActor(ActorOf(arguments[0]), arguments[1], arguments[2]);
                return 0;

            // o5_print: the first operand is the actor saying it, or 255 for nobody; o5_printEgo: the kid says it
            case "o5_print":
                Messages.Talk(arguments[0], MessageLine.Decode(instruction.Operands[1].Text));
                return 0;
            case "o5_printEgo":
                Messages.Talk(engine.Variables[ScriptEngine.VarEgo], MessageLine.Decode(instruction.Operands[0].Text));
                return 0;

            // o2_waitForMessage: the script waits here while a message shows
            case "o2_waitForMessage":
                if (engine.Variables[ScriptEngine.VarHaveMessage] != 0)
                {
                    engine.Repeat();
                }

                return 0;

            // o2_putActorInRoom: only the room changes, unless it is room 0, which puts the actor nowhere
            case "o2_putActorInRoom":
                {
                    var actor = ActorOf(arguments[0]);
                    actor.Room = arguments[1];
                    if (arguments[1] == 0)
                    {
                        if (Version == 0)
                        {
                            actor.SetFacing(180);
                        }
                        else if (Version == 1)
                        {
                            actor.SetDirection(180);
                        }

                        PutActor(actor, 0, 0, 0);
                    }

                    return 0;
                }

            // o2_putActorAtObject: at the object's walk-to point moved into a walkbox, or (30, 60) when the object is not here
            case "o2_putActorAtObject":
                {
                    var actor = ActorOf(arguments[0]);
                    var (x, y) = (30, 60);
                    if (ObjectPosition(arguments[1]) is { } position)
                    {
                        var (point, _) = actor.AdjustToBox(position.X, position.Y);
                        (x, y) = (point.X, point.Y);
                    }

                    PutActor(actor, x, y);
                    return 0;
                }

            case "o2_walkActorTo":
                ActorOf(arguments[0]).StartWalk(arguments[1], arguments[2], -1);
                return 0;

            case "o2_walkActorToObject":
                WalkActorToObject(ActorOf(arguments[0]), arguments[1]);
                return 0;

            // o5_walkActorToActor: beside the other actor, so many columns off on this side of it
            case "o5_walkActorToActor":
                {
                    if (arguments[0] is <= 0 or >= ActorCount || arguments[1] is <= 0 or >= ActorCount)
                    {
                        return 0;
                    }

                    var actor = Actors[arguments[0]];
                    var other = Actors[arguments[1]];
                    if (actor.InCurrentRoom && other.InCurrentRoom)
                    {
                        WalkActorToActor(actor, other, arguments[2]);
                    }

                    return 0;
                }

            // o2_waitForActor: the script waits here while the actor walks or turns
            case "o2_waitForActor":
                if (ActorOf(arguments[0]).Moving != 0)
                {
                    engine.Repeat();
                }

                return 0;

            case "o5_faceActor":
                if (arguments[0] is > 0 and < ActorCount && ObjectOrActorPosition(arguments[1]) is { } facing)
                {
                    Actors[arguments[0]].FaceTowards(facing.X, facing.Y);
                }

                return 0;

            // o2_drawObject: an x other than 255 moves the object first, to a column and row; then it shows its
            // image, and the objects with exactly its rectangle stop showing theirs
            case "o2_drawObject":
                if (CurrentRoom?.FindObject(arguments[0]) is { } drawn)
                {
                    if (arguments[1] != 0xFF)
                    {
                        _moved[drawn.Number] = (arguments[1] * 8, arguments[2] * 8);
                    }

                    var (x, y) = PositionOf(drawn);
                    foreach (var other in CurrentRoom.Objects.Where(o => o.Number != 0 && PositionOf(o) == (x, y) && o.Width == drawn.Width && o.Height == drawn.Height))
                    {
                        engine.PutState(other.Number, engine.GetState(other.Number) & ~ScriptEngine.StateIntrinsic);
                    }

                    engine.PutState(arguments[0], engine.GetState(arguments[0]) | ScriptEngine.StateIntrinsic);
                }

                return 0;

            // o5_setObjectName: a new name, kept across rooms; the inventory shows it at once (setObjectName)
            case "o5_setObjectName":
                if (arguments[0] < ActorCount)
                {
                    throw new InvalidOperationException($"Actor {arguments[0]} cannot be renamed with setObjectName.");
                }

                _newNames[arguments[0]] = Encoding.Latin1.GetString(instruction.Operands[1].Text!);
                RedrawInventory();
                return 0;

            // o5_getClosestObjActor: of the actors and objects from VAR_ACTOR_RANGE_MAX down to _MIN, the nearest, or 0xFF
            case "o5_getClosestObjActor":
                {
                    int closest = 0xFF, closestDistance = 0xFF;
                    for (int obj = engine.Variables[VarActorRangeMax]; obj >= engine.Variables[VarActorRangeMin]; obj--)
                    {
                        int distance = Distance(arguments[0], obj);
                        if (distance < closestDistance)
                        {
                            (closest, closestDistance) = (obj, distance);
                        }
                    }

                    return closest;
                }

            case "o5_getDist":
                return Distance(arguments[0], arguments[1]);

            // o5_startSound restarts the music timer; both note the sound in VAR_LAST_SOUND (Sound::startSound)
            case "o5_startSound":
                SetVar(engine.Vars.MusicTimer, 0);
                SetVar(engine.Vars.LastSound, arguments[0]);
                Sound.Start(arguments[0]);
                return 0;
            case "o5_startMusic":
                SetVar(engine.Vars.LastSound, arguments[0]);
                Sound.Start(arguments[0]);
                return 0;
            case "o5_stopSound":
                Sound.Stop(arguments[0]);
                return 0;
            case "o5_stopMusic":
                Sound.StopAll();
                return 0;
            case "o5_isSoundRunning":
                return arguments[0] != 0 && Sound.IsRunning(arguments[0]) ? 1 : 0;

            case "o3_setBoxFlags":
                if (arguments[0] >= 0 && arguments[0] < _boxFlags.Length)
                {
                    _boxFlags[arguments[0]] = (byte)arguments[1];
                }

                return 0;

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
                return ActorOf(arguments[0]).Moving;
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
                    return actor.InCurrentRoom ? actor.Walkbox : 0xFF;
                }

            // an opcode that does nothing, as in the ending's script (o2_dummy)
            case "o2_dummy":
                return 0;

            default:
                return ExecuteInterface(engine, instruction, arguments, out int result)
                    ? result
                    : _recorder.Execute(engine, instruction, arguments);
        }
    }

    /// <summary>
    /// Changes room. Mirrors <c>startScene</c> for versions 1 and 2: the running room script stops,
    /// the old room's exit script runs and its scripts die, the actors hide, the room variable and
    /// camera limits are set, the actors in the new room show, its entry script runs, then script
    /// 5. The sentence goes back to the default verb. The camera keeps its x, clamped to the new room.
    /// </summary>
    public void StartScene(int number)
    {
        Messages.Stop();
        FadeOut(LeaveEffect);
        _newEffect = EnterEffect;
        Engine.LeaveRoomScript();
        if (CurrentRoom is { } old)
        {
            Engine.RunRoomScript(ScriptEngine.ExitScriptNumber, old.ExitScript);
        }

        Engine.KillRoomScripts();
        if (Version >= 1)
        {
            ResetSentence();
        }

        foreach (var actor in Actors.Skip(1))
        {
            actor.Hide();
        }

        Engine.Variables[ScriptEngine.VarRoom] = number;
        _fullRedraw = true;

        if (number == 0)
        {
            CurrentRoom = null;
            return;
        }

        var room = _loader.LoadRoom(number) ?? throw new InvalidOperationException($"Room {number} does not exist.");
        CurrentRoom = room;
        _moved.Clear();
        _boxFlags = room.WalkBoxes.Boxes.Select(b => b.Flags).ToArray();
        SetVar(Engine.Vars.CameraMinX, ScreenLayout.Width / 2);
        SetVar(Engine.Vars.CameraMaxX, room.Width - ScreenLayout.Width / 2);
        Camera.SetRoom(room.Width);
        SyncCameraLimits();

        // showActors
        foreach (var actor in ActorsInRoom())
        {
            actor.Show();
        }

        Engine.RunRoomScript(ScriptEngine.EntryScriptNumber, room.EntryScript);
        if (Version >= 1)
        {
            Engine.RunScript(AfterEntryScript);
        }

        _doEffect = true;
    }

    /// <summary>
    /// The camera follows an actor, going to its room first when it is elsewhere. Mirrors
    /// <c>actorFollowCamera</c> and <c>setCameraFollows</c>.
    /// </summary>
    internal void FollowActor(Actor actor)
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
    /// the object's walk-to point (moved into a walkbox) facing away from it, the camera follows,
    /// and with a target at or above 0 the kid walks there. Mirrors <c>o2_loadRoomWithEgo</c>.
    /// </summary>
    public void LoadRoomWithEgo(int obj, int room, int walkX, int walkY)
    {
        var ego = ActorOf(Engine.Variables[ScriptEngine.VarEgo]);
        ego.Put(ego.X, ego.Y, room);
        StartScene(room);

        if (ObjectPosition(obj) is { } target)
        {
            var (point, _) = ego.AdjustToBox(target.X, target.Y);
            ego.Put(point.X, point.Y, CurrentRoom!.Number);
            ego.SetDirection(target.Direction + 180);
        }

        SyncCameraLimits();
        Camera.SetAtEx(ego.X * 8);
        Camera.Follow(ego.Number, ego.X * 8, setCamera: true);
        ResetSentence();
        if (walkX >= 0 && walkY >= 0)
        {
            ego.StartWalk(walkX, walkY, -1);
        }

        Engine.RunScript(AfterEntryScript);
    }

    /// <summary>
    /// Where an object in the current room is for walking to: its walk-to point in actor units, and
    /// the facing it asks for. Mirrors <c>getObjectXYPos</c> for versions 1 and 2.
    /// </summary>
    public (int X, int Y, int Direction)? ObjectPosition(int obj)
    {
        if (CurrentRoom?.FindObject(obj) is not { } o)
        {
            return null;
        }

        // a moved object's walk-to point moves with it (o2_drawObject); in version 0 an object with
        // no direction is walked to at its middle (getObjectXYPos)
        var (x, y) = PositionOf(o);
        if (Version == 0 && o.ActorDir == 0)
        {
            return ((x + o.Width / 2) / 8, (y + o.Height / 2) / 2, Actor.OldDirToNewDir(0));
        }

        return ((o.WalkX + x - o.X) / 8, (o.WalkY + y - o.Y) / 2, Actor.OldDirToNewDir(o.ActorDir & 3));
    }

    /// <summary>Where an object of the current room is, in pixels: where the room puts it, or where a script moved it.</summary>
    internal (int X, int Y) PositionOf(RoomObject obj) => _moved.TryGetValue(obj.Number, out var moved) ? moved : (obj.X, obj.Y);

    /// <summary>
    /// Where an actor or object is, in actor units, or null when it is not in the current room. A
    /// carried object is where the actor carrying it is. Mirrors <c>getObjectOrActorXY</c>.
    /// </summary>
    public (int X, int Y)? ObjectOrActorPosition(int obj)
    {
        if (obj < ActorCount)
        {
            return obj > 0 && Actors[obj].InCurrentRoom ? (Actors[obj].X, Actors[obj].Y) : null;
        }

        if (ObjectPosition(obj) is { } position)
        {
            return (position.X, position.Y);
        }

        int owner = Engine.GetOwner(obj);
        return owner is > 0 and < ActorCount && Actors[owner].InCurrentRoom ? (Actors[owner].X, Actors[owner].Y) : null;
    }

    /// <summary>
    /// The distance between two actors or objects in actor units, the larger of the two axes; 0xFF
    /// when one is not here. An object's point is first moved into a walkbox for an actor. Mirrors
    /// <c>getObjActToObjActDist</c> and <c>getDist</c>.
    /// </summary>
    public int Distance(int a, int b)
    {
        var actorA = a is > 0 and < ActorCount ? Actors[a] : null;
        var actorB = b is > 0 and < ActorCount ? Actors[b] : null;
        if (actorA is not null && actorB is not null && actorA.Room == actorB.Room && actorA.Room != 0 && !actorA.InCurrentRoom)
        {
            return 0;
        }

        if (ObjectOrActorPosition(a) is not { } first || ObjectOrActorPosition(b) is not { } second)
        {
            return 0xFF;
        }

        if (actorA is not null && actorB is null)
        {
            var (point, _) = actorA.AdjustToBox(second.X, second.Y);
            second = (point.X, point.Y);
        }

        return Math.Max(Math.Abs(first.Y - second.Y), Math.Abs(first.X - second.X));
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
            var blank = new Surface8(ScreenLayout.Width, ScreenLayout.Height);
            for (int y = 0; y < MessageLine.Height; y++)
            {
                Messages.Text.Row(y).CopyTo(blank.Row(y));
            }

            return blank;
        }

        var view = RoomView.Window(RoomRenderer.Render(room, StateOf, PositionOf), Camera);
        int lights = CurrentLights;
        bool colors = (lights & LightActorColors) != 0;
        _actorStrips.Clear();

        // the lights off: the room is black, but where the flashlight shines (clear8Col, drawFlashlight)
        if ((lights & Flashlight.RoomLightsOn) == 0)
        {
            Rect? light = null;
            if ((lights & Flashlight.FlashlightOn) != 0 && _flashlight.Strips > 0 && _flashlight.Rows > 0)
            {
                int height = Math.Min(view.Pixels.Height, layout.RoomHeight);
                light = Flashlight.Area(Mouse.X + Camera.Left, Mouse.Y - layout.RoomTop, Camera.Left, _flashlight.Strips, _flashlight.Rows, view.Pixels.Width, height);
            }

            Flashlight.Darken(view.Pixels, Version, light);
        }
        foreach (var actor in ActorsInRoom().Where(a => a.Visible).OrderBy(SortKey))
        {
            CostumeFrame frame;
            int box = actor.Walkbox;
            int mask = box < room.WalkBoxes.Count ? room.WalkBoxes.Boxes[box].Mask : 0;
            if (actor is ActorV0 c64)
            {
                // version 0 skips hidden kids, and the mask's low 2 bits are its layer (prepareDrawActorCostume)
                if ((c64.MiscFlags & ActorV0.MiscFlagHide) != 0 || c64.CostumeV0 is not { } costumeV0)
                {
                    continue;
                }

                frame = V0CostumeRenderer.DrawLimbs(costumeV0, c64.Limbs, V0CostumeRenderer.Palette(actor.Number, colors, (lights & Flashlight.FlashlightOn) != 0));
                mask &= 3;
            }
            else
            {
                if (actor.Costume is not { } costume)
                {
                    continue;
                }

                var palette = CostumeRenderer.Palette(costume, actor.Number, actor.Palette, colors);
                frame = CostumeRenderer.DrawLimbs(costume, actor.State, actor.OldDirection, palette);
            }

            bool behind = box == BoxGeometry.NoBox || mask != 0;
            var (left, right) = RoomView.DrawActor(view, Version, Camera, new ActorDrawing(frame, actor.X, actor.Y, actor.Facing == 270, behind, actor.Elevation));

            // markRectAsDirty: the room strips the figure covers, for getActorFromPos
            if (right > 0 && left < Camera.ScreenWidth)
            {
                _actorStrips[actor.Number] = (Math.Max(left, 0) / 8 + Camera.StartStrip, (Math.Min(right, Camera.ScreenWidth) + Camera.Left) / 8);
            }
        }

        var screen = RoomView.Screen(view, layout);
        for (int y = 0; y < MessageLine.Height && y < layout.RoomTop; y++)
        {
            Messages.Text.Row(y).CopyTo(screen.Row(y));
        }

        return screen;
    }

    /// <summary>To an object's walk-to point in a walkbox, to face the way the object says. Mirrors <c>ScummEngine_v2::walkActorToObject</c>.</summary>
    private void WalkActorToObject(Actor actor, int obj)
    {
        if (ObjectPosition(obj) is { } target)
        {
            var (point, _) = actor.AdjustToBox(target.X, target.Y);
            actor.StartWalk(point.X, point.Y, target.Direction);
        }
    }

    /// <summary>Beside another actor, so many columns off on this side of it. Mirrors <c>ScummEngine_v5::walkActorToActor</c> for versions up to 2.</summary>
    private static void WalkActorToActor(Actor actor, Actor other, int distance)
    {
        int x = other.X * 8 + (other.X * 8 < actor.X * 8 ? distance * 8 : -distance * 8);
        var (point, _) = actor.AdjustToBox(x / 8, other.Y);
        actor.StartWalk(point.X, point.Y, -1);
    }

    /// <summary>Puts an actor, stopping its speech when it leaves the current room. Mirrors the start of <c>putActor</c>.</summary>
    private void PutActor(Actor actor, int x, int y, int? room = null)
    {
        if (actor.Visible && room is { } newRoom && newRoom != (CurrentRoom?.Number ?? 0) && Messages.TalkingActor == actor.Number)
        {
            Messages.Stop();
        }

        actor.Put(x, y, room);
    }

    /// <summary>The name of an actor, or of an object: the name a script gave it, or its own, carried or in the room. Mirrors <c>getObjOrActorName</c>.</summary>
    public string NameOf(int number)
    {
        // in version 0 only an object of the actor type is an actor (objIsActor)
        if (Version == 0 ? number >> 8 == TypeActor : number is > 0 and < ActorCount)
        {
            int actor = number & 0xFF;
            return actor is > 0 and < ActorCount ? Actors[actor].Name : string.Empty;
        }

        return _newNames.TryGetValue(number, out string? name) ? name : ObjectCode(number)?.Name ?? string.Empty;
    }

    private void ActorOps(IReadOnlyList<int> arguments, Instruction instruction)
    {
        int sub = arguments[2];
        if (arguments[0] == 0 && sub == 5)
        {
            Messages.StringColor = arguments[1];
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

    /// <summary>
    /// The order actors are drawn in: from the back (top of the screen) to the front, but version
    /// 0 draws the plant first wherever it is. Mirrors <c>processActors</c>' sort.
    /// </summary>
    private int SortKey(Actor actor) => Version == 0 && actor.Number == V0Plant ? 0 : actor.Y;

    /// <summary>A sound resource, or null when there is none; some index entries point past their room file, and play nothing.</summary>
    private byte[]? LoadSound(int number)
    {
        if (!_sounds.TryGetValue(number, out var data))
        {
            try
            {
                data = _loader.Index.Locate(ResourceType.Sound, number).IsPresent ? _loader.Load(ResourceType.Sound, number)?.ToArray() : null;
            }
            catch (InvalidDataException)
            {
                data = null;
            }

            _sounds[number] = data;
        }

        return data;
    }

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

    /// <summary>
    /// The camera reads its limits from the scripts' variables, as ScummVM's camera code does. Version
    /// 0 has none, and keeps the room's own (<c>cameraMoved</c>).
    /// </summary>
    private void SyncCameraLimits()
    {
        if (Engine.Vars.CameraMinX != EngineVariables.None)
        {
            Camera.MinX = Engine.Variables[Engine.Vars.CameraMinX];
            Camera.MaxX = Engine.Variables[Engine.Vars.CameraMaxX];
        }
    }

    /// <summary>
    /// The state of an object of the room. Version 0's objects that are part of the room (type 1
    /// in their number's high byte) have none and count as 0: ScummVM never reads them from the
    /// state table (<c>updateObjectStates</c>, <c>findObject</c>).
    /// </summary>
    public int StateOf(int obj) => Version == 0 && obj >> 8 != 0 ? 0 : Engine.GetState(obj);

    /// <summary>Writes an engine variable, when the version has it.</summary>
    private void SetVar(int number, int value)
    {
        if (number != EngineVariables.None)
        {
            Engine.Variables[number] = value;
        }
    }

    /// <summary>
    /// The lights: how the room and the actors are lit. Versions 1 and 2 keep them in a variable;
    /// version 0 in the engine, set by <c>o_lights</c> (<c>getCurrentLights</c>).
    /// </summary>
    public int CurrentLights
    {
        get => Engine.Vars.CurrentLights == EngineVariables.None ? _lightsV0 : Engine.Variables[Engine.Vars.CurrentLights];
        private set
        {
            if (Engine.Vars.CurrentLights == EngineVariables.None)
            {
                _lightsV0 = value;
            }
            else
            {
                Engine.Variables[Engine.Vars.CurrentLights] = value;
            }
        }
    }

    // version 0's lights (_currentLights); resetScummVars starts them with the room lit and the actors in colour
    private int _lightsV0 = LightActorBasePalette | Flashlight.RoomLightsOn | LightActorColors;
}
