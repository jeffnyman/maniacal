namespace Maniacal.Scumm.Scripts;

/// <summary>Mirrors ScummVM's <c>ssDead</c>, <c>ssPaused</c> and <c>ssRunning</c>; <see cref="Frozen"/> is the 0x80 bit <c>freezeScripts</c> adds.</summary>
public static class SlotStatus
{
    public const byte Dead = 0;
    public const byte Paused = 1;
    public const byte Running = 2;
    public const byte Frozen = 0x80;
}

/// <summary>What a slot's code belongs to. Mirrors ScummVM's <c>WIO_GLOBAL</c>, <c>WIO_ROOM</c> and <c>WIO_INVENTORY</c>.</summary>
public enum ScriptKind
{
    /// <summary>A numbered global script.</summary>
    Global,

    /// <summary>Code in the current room: its entry or exit script, or an object's verb script.</summary>
    Room,

    /// <summary>The verb script of an object the kids carry.</summary>
    Inventory,
}

/// <summary>One of the cooperative threads scripts run in. Mirrors ScummVM's <c>ScriptSlot</c> for versions 1 and 2.</summary>
internal sealed class ScriptSlot
{
    /// <summary>The script, object or pseudo-number (<see cref="ScriptEngine.EntryScriptNumber"/>, <see cref="ScriptEngine.ExitScriptNumber"/>) running here.</summary>
    public int Number { get; internal set; }

    public byte Status { get; internal set; } = SlotStatus.Dead;

    public ScriptKind Kind { get; internal set; }

    /// <summary>The code the slot runs; offsets count from its start.</summary>
    public ReadOnlyMemory<byte> Code { get; internal set; }

    /// <summary>Where execution resumes.</summary>
    public int Offset { get; internal set; }

    /// <summary>
    /// Where the slot was when it last yielded, started running or started a nested script: what
    /// ScummVM's <c>offs</c> holds while the slot runs, which a save made mid-instruction records.
    /// </summary>
    public int ResumeOffset { get; internal set; }

    /// <summary>Ticks left to wait while paused.</summary>
    public int Delay { get; internal set; }

    public bool FreezeResistant { get; internal set; }

    public bool Recursive { get; internal set; }

    public bool IsDead => Status == SlotStatus.Dead;

    internal void Kill()
    {
        Number = 0;
        Status = SlotStatus.Dead;
        Code = ReadOnlyMemory<byte>.Empty;
    }
}
