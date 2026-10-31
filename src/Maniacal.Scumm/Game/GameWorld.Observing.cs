using Maniacal.Scumm.Hosting;
using Maniacal.Scumm.Scripts;

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
    /// the scripts time by the sound.
    /// </summary>
    public IAudioSource Audio => Sound.Output;

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

    /// <summary>The actors in the room on screen.</summary>
    public IReadOnlyList<ActorInfo> ActorsHere()
        => Actors.Skip(1).Where(a => a.InCurrentRoom)
            .Select(a => new ActorInfo { Number = a.Number, Name = a.Name, Costume = a.CostumeNumber, X = a.X, Y = a.Y, Facing = a.Facing, Visible = a.Visible })
            .ToList();
}
