using Maniacal.Scumm.Detection;
using Maniacal.Scumm.Hosting;
using Maniacal.Scumm.Storage;

namespace Maniacal.Scumm;

/// <summary>Detects Maniac Mansion releases and creates the SCUMM interpreter for them.</summary>
public sealed class ScummEngineProvider : IEngineProvider
{
    /// <summary>The engine's id in detections: <c>scumm</c>, as ScummVM names it.</summary>
    public const string EngineId = "scumm";

    /// <inheritdoc />
    public string Id => EngineId;

    /// <summary>
    /// The Maniac Mansion release in the container, by ScummVM's MD5 table: none if there is none,
    /// or if the release is one ScummVM's table does not know.
    /// </summary>
    public IReadOnlyList<DetectedGame> Detect(IGameContainer game)
    {
        if (ScummDetector.Identify(game) is not { } found)
        {
            return [];
        }

        return [new DetectedGame(EngineId, found.Entry.GameId, "Maniac Mansion", found.Variant, found.Language, found.Platform)];
    }

    /// <inheritdoc />
    public Engine Create(IHost host, IGameContainer game, DetectedGame detected)
        => new ScummEngine(host, game, detected);
}
