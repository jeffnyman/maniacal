using Maniacal.Scumm.Hosting;
using Maniacal.Scumm.Scripts;

namespace Maniacal.Scumm.Game;

/// <summary>An actor as it is at the moment it was looked at: its number, name, costume, position, facing (in degrees) and whether it is drawn.</summary>
public readonly record struct ActorInfo(int Number, string Name, int Costume, int X, int Y, int Facing, bool Visible);

/// <summary>
/// A script alive in a slot at the moment it was looked at: what its code belongs to, its number
/// (a script's, or an object's for a verb script), where it is in its code, its
/// <see cref="SlotStatus"/> and, while paused, its delay in ticks.
/// </summary>
public readonly record struct RunningScript(int Slot, ScriptKind Kind, int Number, int Offset, byte Status, int Delay);

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
            .Select(p => new RunningScript(p.i, p.slot.Kind, p.slot.Number, p.slot.Offset, p.slot.Status, p.slot.Delay))
            .ToList();

    /// <summary>The actors in the room on screen.</summary>
    public IReadOnlyList<ActorInfo> ActorsHere()
        => Actors.Skip(1).Where(a => a.InCurrentRoom)
            .Select(a => new ActorInfo(a.Number, a.Name, a.CostumeNumber, a.X, a.Y, a.Facing, a.Visible))
            .ToList();
}
