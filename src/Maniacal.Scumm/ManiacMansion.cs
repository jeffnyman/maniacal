using Maniacal.Scumm.Detection;
using Maniacal.Scumm.Game;
using Maniacal.Scumm.Hosting;
using Maniacal.Scumm.Inspection;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Storage;

namespace Maniacal.Scumm;

/// <summary>
/// A Maniac Mansion release, opened from its files: what it is, and the ways to play it or look
/// inside it.
/// <code>
/// var game = ManiacMansion.Open(folder);   // "Maniac Mansion (V2, English, DOS)"
/// var world = game.Play();                 // headless: Boot, then Step
/// var inspector = game.Inspect();          // rooms, costumes, scripts
/// </code>
/// It is the front door; <see cref="ResourceLoader.Open"/> with <see cref="GameWorld"/> or
/// <see cref="GameInspector"/> is the same thing taken step by step.
/// <para>
/// It does not change once opened, so it may be shared between threads; each world and
/// inspector it gives opens resources of its own, and so may go to a thread of its own.
/// </para>
/// </summary>
public sealed class ManiacMansion
{
    private readonly ScummGame _found;

    private ManiacMansion(IGameContainer files, ScummGame found)
    {
        Files = files;
        _found = found;
        Detected = ScummEngineProvider.Describe(found);
    }

    /// <summary>The release's files.</summary>
    public IGameContainer Files { get; }

    /// <summary>The release as detection names it, as an <see cref="EngineRegistry"/> would.</summary>
    public DetectedGame Detected { get; }

    /// <summary>The release in words: <c>Maniac Mansion (V2, English, DOS)</c>.</summary>
    public string Description => _found.Description;

    /// <summary>The release's SCUMM version: 0 for the Commodore 64, 1 for the 1987 DOS release, 2 for the 1988 one.</summary>
    public int Version => _found.Version;

    /// <summary>Whether the release is one of the DOS demos, which run themselves.</summary>
    public bool IsDemo => _found.IsDemo;

    /// <summary>
    /// Opens the release in a folder, or in a folder up to two levels below it. Throws
    /// <see cref="DirectoryNotFoundException"/> when there is no such folder,
    /// <see cref="FileNotFoundException"/> when it holds no Maniac Mansion release, and
    /// <see cref="NotSupportedException"/> for a release Maniacal does not play (another language
    /// or platform; the message says which).
    /// </summary>
    public static ManiacMansion Open(string folder)
    {
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException($"There is no folder {folder}.");
        }

        var registry = new EngineRegistry().Add(new ScummEngineProvider());
        if (registry.DetectInDirectory(folder) is not var (container, _))
        {
            throw new FileNotFoundException($"There is no Maniac Mansion release in {folder} or the folders below it.");
        }

        return Open(container);
    }

    /// <summary>
    /// Opens the release whose files a container holds: a folder's, or files kept in memory.
    /// Throws as <see cref="Open(string)"/> does, but for the folder.
    /// </summary>
    public static ManiacMansion Open(IGameContainer files)
    {
        var found = ScummDetector.Identify(files)
            ?? throw new FileNotFoundException($"There is no Maniac Mansion release in {files.Name}.");

        // opening the resources refuses a release Maniacal does not play
        ResourceLoader.Open(files);
        return new ManiacMansion(files, found);
    }

    /// <summary>
    /// A new game, headless: <see cref="GameWorld.Boot"/> it (or <see cref="GameWorld.Load"/> a save)
    /// and <see cref="GameWorld.Step"/> it. Each world has its own resources.
    /// </summary>
    /// <param name="seed">The seed of the scripts' random numbers.</param>
    /// <param name="sampleRate">The rate the sound is made at.</param>
    /// <param name="pcjr">Whether the DOS sound is the PCjr's rather than the PC speaker's.</param>
    public GameWorld Play(int seed = 1, int sampleRate = GameWorld.DefaultSampleRate, bool pcjr = true)
        => new(ResourceLoader.Open(Files), seed, sampleRate, pcjr);

    /// <summary>A look inside the release: its rooms, costumes and scripts.</summary>
    public GameInspector Inspect() => new(ResourceLoader.Open(Files));

    /// <summary>The engine that plays the release on a host, a window's or a custom one's.</summary>
    public ScummEngine CreateEngine(IHost host) => new(host, Files, Detected);

    /// <summary>The release in words, as <see cref="Description"/>.</summary>
    public override string ToString() => Description;
}
