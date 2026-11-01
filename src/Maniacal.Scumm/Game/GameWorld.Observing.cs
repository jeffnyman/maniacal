using Maniacal.Scumm.Graphics;
using Maniacal.Scumm.Hosting;
using Maniacal.Scumm.Scripts;
using Maniacal.Scumm.View;

namespace Maniacal.Scumm.Game;

/// <summary>An actor as it was at the moment it was looked at.</summary>
public readonly record struct ActorInfo
{
    /// <summary>The actor's number.</summary>
    public int Number { get; init; }

    /// <summary>The actor's name, as the game shows it.</summary>
    public string Name { get; init; }

    /// <summary>The costume the actor wears.</summary>
    public int Costume { get; init; }

    /// <summary>The actor's position in the room, in actor units.</summary>
    public int X { get; init; }

    /// <summary>The actor's position in the room, in actor units.</summary>
    public int Y { get; init; }

    /// <summary>The way the actor faces, in degrees: 90 right, 270 left, 180 to the front, 0 to the back.</summary>
    public int Facing { get; init; }

    /// <summary>Whether the actor is drawn.</summary>
    public bool Visible { get; init; }
}

/// <summary>An object of the room on screen as it was at the moment it was looked at.</summary>
public readonly record struct RoomObjectInfo
{
    /// <summary>The object's number.</summary>
    public int Number { get; init; }

    /// <summary>The object's name as the game shows it now; empty for the many objects without one.</summary>
    public string Name { get; init; }

    /// <summary>The left of its rectangle in the room, in pixels, where it is now.</summary>
    public int X { get; init; }

    /// <summary>The top of its rectangle in the room, in pixels, where it is now.</summary>
    public int Y { get; init; }

    /// <summary>Its rectangle's width, in pixels.</summary>
    public int Width { get; init; }

    /// <summary>Its rectangle's height, in pixels.</summary>
    public int Height { get; init; }

    /// <summary>Its state, as the scripts set it.</summary>
    public int State { get; init; }

    /// <summary>Whether a click on it finds it: it is not untouchable, and its parent objects let it be.</summary>
    public bool Touchable { get; init; }

    /// <summary>The column of its centre on the screen, or null when its centre is out of view.</summary>
    public int? ScreenX { get; init; }

    /// <summary>The row of its centre on the screen, or null when its centre is out of view.</summary>
    public int? ScreenY { get; init; }
}

/// <summary>A script alive in a slot at the moment it was looked at.</summary>
public readonly record struct RunningScript
{
    /// <summary>The slot it runs in.</summary>
    public int Slot { get; init; }

    /// <summary>What its code belongs to.</summary>
    public ScriptKind Kind { get; init; }

    /// <summary>The script's number, or the object's for a verb script.</summary>
    public int Number { get; init; }

    /// <summary>Where it is in its code.</summary>
    public int Offset { get; init; }

    /// <summary>Its <see cref="SlotStatus"/>, with <see cref="SlotStatus.Frozen"/> added while frozen.</summary>
    public byte Status { get; init; }

    /// <summary>While paused, the ticks left before it runs again.</summary>
    public int Delay { get; init; }
}

/// <summary>
/// What a host, a recording or a tool needs of a running game beyond the screen and input: the
/// options, the sound, skipping cutscenes, and looking on at the scripts, variables and actors.
/// What it gives back are copies, so holding one does not hold the world.
/// </summary>
public sealed partial class GameWorld
{
    /// <summary>
    /// Whether the copy protection is kept. Off by default, as in ScummVM: both sides of the steel
    /// security door stay open, so its code is never asked for.
    /// </summary>
    public bool CopyProtection
    {
        get => Engine.CopyProtection;
        set => Engine.CopyProtection = value;
    }

    /// <summary>
    /// The music and sound effects, interleaved stereo at the sample rate the world was made with.
    /// Read it as the game runs, a step's worth after each step: sound nobody has read for a
    /// quarter of a second is made and thrown away, so that a silent host does not hold up what
    /// the scripts time by the sound. It may be read from another thread than the one stepping
    /// the world, as an audio device's callback reads it.
    /// </summary>
    public IAudioSource Audio => Sound.Output;

    /// <summary>
    /// The colours <see cref="Screen"/>'s colour numbers stand for, the release's palette, in a form
    /// that can be kept: <c>PngWriter.Write(file, world.Screen(), world.Palette.Span)</c>.
    /// </summary>
    public ReadOnlyMemory<Rgb> Palette => Palettes.MemoryForVersion(Version);

    /// <summary>The rate the world makes its sound at, in frames a second: the one it was made with.</summary>
    public int SampleRate { get; }

    // the stereo frames ReadAudio has handed out
    private long _audioFramesRead;

    /// <summary>
    /// The sound made since the last call, or since the world was made: interleaved stereo at
    /// <see cref="SampleRate"/>, as many frames as the game time that has passed holds, none when
    /// none has. Called after each step, as a recording does, it keeps up with <see cref="Audio"/>
    /// by itself. Read the sound this way or from <see cref="Audio"/>, not both.
    /// </summary>
    public short[] ReadAudio()
    {
        long due = (long)(Time.TotalSeconds * SampleRate);
        if (due <= _audioFramesRead)
        {
            return [];
        }

        var buffer = new short[(due - _audioFramesRead) * 2];
        Audio.Read(buffer);
        _audioFramesRead = due;
        return buffer;
    }

    /// <summary>Skips the running cutscene, its script jumping to its override. Mirrors <c>abortCutscene</c>.</summary>
    public void SkipCutscene() => Engine.AbortCutscene();

    /// <summary>The script variables as they are now.</summary>
    public IReadOnlyList<int> Variables => Engine.Variables;

    /// <summary>Sets a script variable, as a script's assignment does.</summary>
    public void SetVariable(int number, int value) => Engine.WriteVar(number, value);

    /// <summary>The number of the script running at the moment, or 0 between scripts: what an error in a step was running.</summary>
    public int CurrentScript => Engine.CurrentScriptNumber;

    /// <summary>The scripts alive in their slots.</summary>
    public IReadOnlyList<RunningScript> RunningScripts()
        => Engine.Slots.Select((slot, i) => (slot, i)).Where(p => !p.slot.IsDead)
            .Select(p => new RunningScript { Slot = p.i, Kind = p.slot.Kind, Number = p.slot.Number, Offset = p.slot.Offset, Status = p.slot.Status, Delay = p.slot.Delay })
            .ToList();

    /// <summary>
    /// The objects of the room on screen, in the order a click looks through them (the first one
    /// a click's point is in, of those <see cref="RoomObjectInfo.Touchable"/>, is the one it finds).
    /// Mirrors what <c>findObject</c> sees.
    /// </summary>
    public IReadOnlyList<RoomObjectInfo> ObjectsHere()
    {
        if (CurrentRoom is not { } room)
        {
            return [];
        }

        var layout = ScreenLayout.ForVersion(Version);
        var objects = new List<RoomObjectInfo>();
        foreach (var obj in room.Objects.Where(o => o.Number >= 1))
        {
            var (x, y) = PositionOf(obj);
            int centreX = x + obj.Width / 2 - Camera.Left, centreY = y + obj.Height / 2 + layout.RoomTop;
            bool inView = centreX is >= 0 and < ScreenLayout.Width && centreY >= layout.RoomTop && centreY < layout.RoomBottom;
            objects.Add(new RoomObjectInfo
            {
                Number = obj.Number,
                Name = NameOf(obj.Number),
                X = x,
                Y = y,
                Width = obj.Width,
                Height = obj.Height,
                State = StateOf(obj.Number),
                Touchable = (StateOf(obj.Number) & ScriptEngine.StateUntouchable) == 0 && RoomRenderer.ParentsAllow(room, obj, StateOf),
                ScreenX = inView ? centreX : null,
                ScreenY = inView ? centreY : null,
            });
        }

        return objects;
    }

    /// <summary>
    /// Clicks the centre of an object of the room on screen, as <see cref="Click"/> does at that
    /// point. False, and no click, when the object is not in the room on screen or its centre is
    /// out of view.
    /// </summary>
    public bool ClickObject(int number)
    {
        if (ObjectsHere().FirstOrDefault(o => o.Number == number) is not { ScreenX: { } x, ScreenY: { } y })
        {
            return false;
        }

        Click(x, y);
        return true;
    }

    /// <summary>The actors in the room on screen.</summary>
    public IReadOnlyList<ActorInfo> ActorsHere()
        => Actors.Skip(1).Where(a => a.InCurrentRoom)
            .Select(a => new ActorInfo { Number = a.Number, Name = a.Name, Costume = a.CostumeNumber, X = a.X, Y = a.Y, Facing = a.Facing, Visible = a.Visible })
            .ToList();
}
