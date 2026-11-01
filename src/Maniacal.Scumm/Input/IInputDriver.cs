namespace Maniacal.Scumm.Input;

/// <summary>Queue of input events the host has collected since the engine last drained it.</summary>
public interface IInputDriver
{
    /// <summary>
    /// Takes the oldest event waiting, or returns false when there is none. The engine follows
    /// the mouse by these events alone: a host reports every move as a <see cref="InputEventType.MouseMove"/>.
    /// </summary>
    bool TryRead(out InputEvent inputEvent);
}
