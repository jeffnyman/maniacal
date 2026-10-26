using Maniacal.Scumm.Detection;
using Maniacal.Scumm.Hosting;
using Maniacal.Scumm.Storage;

namespace Maniacal.Scumm;

/// <summary>Detects Maniac Mansion releases and creates the SCUMM interpreter for them.</summary>
public sealed class ScummEngineProvider : IEngineProvider
{
    public const string EngineId = "scumm";

    public string Id => EngineId;

    public IReadOnlyList<DetectedGame> Detect(IGameContainer game)
    {
        if (ScummDetector.Identify(game) is not { } found)
        {
            return [];
        }

        return [new DetectedGame(EngineId, found.Entry.GameId, "Maniac Mansion", found.Variant, found.Language, found.Platform)];
    }

    public Engine Create(IHost host, IGameContainer game, DetectedGame detected)
        => new ScummEngine(host, game, detected);
}
