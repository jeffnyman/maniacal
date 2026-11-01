using Maniacal.Scumm.Graphics;
using Maniacal.Scumm.Input;
using Maniacal.Scumm.Storage;

namespace Maniacal.Scumm.Hosting;

/// <summary>Everything an engine needs from the platform, in one place.</summary>
public interface IHost
{
    /// <summary>The display the engine presents its frames to.</summary>
    IGraphicsDriver Graphics { get; }

    /// <summary>The keys and mouse.</summary>
    IInputDriver Input { get; }

    /// <summary>The sound output.</summary>
    IAudioDriver Audio { get; }

    /// <summary>Where saves are kept.</summary>
    ISaveStore Saves { get; }

    /// <summary>Where the engine's log lines go.</summary>
    ILog Log { get; }

    /// <summary>Seed for the games' random numbers, or null for a different sequence every run.</summary>
    int? RandomSeed => null;
}
