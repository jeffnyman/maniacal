namespace Maniacal.Scumm.Scripts;

/// <summary>
/// The variables the engine itself reads and writes, by the numbers a version gives them, or
/// <see cref="None"/> where it has no such variable. Mirrors ScummVM's <c>VAR_*</c> numbers, which
/// <c>setupScummVars</c> sets and which stay 0xFF where a version leaves them out: version 0 has
/// a handful, version 2 many more. The other numbers are the scripts' own.
/// </summary>
public sealed record EngineVariables
{
    /// <summary>The number of a variable a version does not have.</summary>
    public const int None = -1;

    public int Ego { get; init; }

    public int CameraPosX { get; init; } = None;

    public int HaveMessage { get; init; } = None;

    public int Room { get; init; } = None;

    public int Override { get; init; } = None;

    public int CharCount { get; init; } = None;

    public int ActiveVerb { get; init; } = None;

    public int ActiveObject2 { get; init; } = None;

    public int CurrentLights { get; init; } = None;

    public int MusicTimer { get; init; } = None;

    public int CameraMinX { get; init; } = None;

    public int CameraMaxX { get; init; } = None;

    public int TimerNext { get; init; } = None;

    public int LastSound { get; init; } = None;

    public int TalkActor { get; init; } = None;

    /// <summary>Version 0: whether the last sound started still plays (<c>VAR_IS_SOUND_RUNNING</c>).</summary>
    public int IsSoundRunning { get; init; } = None;

    /// <summary>Mirrors <c>ScummEngine_v0::setupScummVars</c>.</summary>
    public static readonly EngineVariables Version0 = new()
    {
        Ego = 0, CameraPosX = 2, HaveMessage = 3, Room = 4, ActiveObject2 = 5, Override = 6,
        IsSoundRunning = 8, ActiveVerb = 9, CharCount = 10,
    };

    /// <summary>Mirrors <c>ScummEngine_v2::setupScummVars</c>, which version 1 shares.</summary>
    public static readonly EngineVariables Version2 = new()
    {
        Ego = 0, CameraPosX = 2, HaveMessage = 3, Room = 4, Override = 5, CharCount = 7, ActiveVerb = 8,
        ActiveObject2 = 10, CurrentLights = 12, MusicTimer = 17, CameraMinX = 23, CameraMaxX = 24,
        TimerNext = 25, LastSound = 37, TalkActor = 41,
    };

    /// <summary>The variables of a version.</summary>
    public static EngineVariables For(int version) => version == 0 ? Version0 : Version2;
}
