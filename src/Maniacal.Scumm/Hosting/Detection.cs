using Maniacal.Scumm.Storage;

namespace Maniacal.Scumm.Hosting;

/// <summary>A game an engine provider recognised inside a container.</summary>
public sealed record DetectedGame
{
    /// <summary>Which engine runs it: <c>scumm</c>.</summary>
    public required string EngineId { get; init; }

    /// <summary>A stable short identifier, as ScummVM's: <c>maniac</c>.</summary>
    public required string GameId { get; init; }

    /// <summary>The title to show: <c>Maniac Mansion</c>.</summary>
    public required string Title { get; init; }

    /// <summary>The release, if the detection tells: <c>V2</c>, <c>V1 Demo</c>, <c>C64</c>.</summary>
    public string? Extra { get; init; }

    /// <summary>The ISO language code, if known: <c>en</c>.</summary>
    public string? Language { get; init; }

    /// <summary>
    /// The original platform, if known, as ScummVM's code for it: <c>dos</c>, <c>c64</c>, <c>nes</c>,
    /// <c>atari</c> (the Atari ST) or <c>amiga</c>. <see cref="ManiacMansion.Description"/> names it in words.
    /// </summary>
    public string? PlatformName { get; init; }

    /// <summary>The version notes and, for anything but DOS, the platform: what tells two detections of one folder apart.</summary>
    public string Variant
    {
        get
        {
            bool otherPlatform = !string.IsNullOrEmpty(PlatformName) && !PlatformName.Equals("dos", StringComparison.OrdinalIgnoreCase);
            return (Extra, otherPlatform) switch
            {
                (null or "", false) => string.Empty,
                (null or "", true) => PlatformName!,
                (_, false) => Extra,
                (_, true) => $"{Extra}, {PlatformName}",
            };
        }
    }

    /// <summary>The title, the variant if any, and the engine and game ids: <c>Maniac Mansion (V2) [scumm:maniac]</c>.</summary>
    public override string ToString()
        => Variant.Length == 0 ? $"{Title} [{EngineId}:{GameId}]" : $"{Title} ({Variant}) [{EngineId}:{GameId}]";
}

/// <summary>Detects and instantiates one engine family.</summary>
public interface IEngineProvider
{
    /// <summary>The engine's id, as <see cref="DetectedGame.EngineId"/> names it.</summary>
    string Id { get; }

    /// <summary>The games of this engine the container holds, none if it holds none.</summary>
    IReadOnlyList<DetectedGame> Detect(IGameContainer game);

    /// <summary>An engine for a game this provider detected.</summary>
    Engine Create(IHost host, IGameContainer game, DetectedGame detected);
}

/// <summary>The set of engines a host knows about, each asked in turn what a folder holds.</summary>
public sealed class EngineRegistry
{
    private readonly List<IEngineProvider> _providers = [];

    /// <summary>The engines, in the order they were added.</summary>
    public IReadOnlyList<IEngineProvider> Providers => _providers;

    /// <summary>Adds an engine and returns the registry, so additions chain.</summary>
    public EngineRegistry Add(IEngineProvider provider)
    {
        _providers.Add(provider);
        return this;
    }

    /// <summary>Every game any engine finds in the container.</summary>
    public IReadOnlyList<DetectedGame> Detect(IGameContainer game)
        => _providers.SelectMany(p => p.Detect(game)).ToList();

    /// <summary>
    /// Detects the game in a folder, or in a subfolder up to <paramref name="maxDepth"/> levels
    /// down when the folder itself holds none, as when a copy keeps the game's files one folder
    /// in. Returns the container the games were found in, or null when there is none.
    /// </summary>
    public (DirectoryGameContainer Container, IReadOnlyList<DetectedGame> Games)? DetectInDirectory(string path, int maxDepth = 2)
    {
        var container = new DirectoryGameContainer(path);
        var games = Detect(container);
        if (games.Count > 0)
        {
            return (container, games);
        }

        if (maxDepth <= 0)
        {
            return null;
        }

        foreach (var subfolder in Directory.EnumerateDirectories(path).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (DetectInDirectory(subfolder, maxDepth - 1) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// An engine for a detected game, from the provider its <see cref="DetectedGame.EngineId"/>
    /// names. Throws <see cref="InvalidOperationException"/> when no such engine is registered.
    /// </summary>
    public Engine Create(IHost host, IGameContainer game, DetectedGame detected)
    {
        var provider = _providers.FirstOrDefault(p => p.Id == detected.EngineId)
            ?? throw new InvalidOperationException($"No engine registered with id '{detected.EngineId}'.");
        return provider.Create(host, game, detected);
    }
}
