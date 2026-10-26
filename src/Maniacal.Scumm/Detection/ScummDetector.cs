using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Storage;

namespace Maniacal.Scumm.Detection;

/// <summary>A Maniac Mansion release identified in a game folder.</summary>
/// <param name="Entry">The matching row of ScummVM's MD5 table.</param>
/// <param name="Version">The SCUMM version: 0 for C64 and Apple II, 1 for DOS V1 and NES, 2 for V2.</param>
/// <param name="IsDemo">True for the demo releases.</param>
public sealed record ScummGame(Md5Entry Entry, int Version, bool IsDemo)
{
    public string Variant => Entry.Variant;

    public string? Language => Entry.Language;

    public string? Platform => Entry.Platform;
}

/// <summary>
/// Identifies Maniac Mansion releases the way ScummVM's <c>detection_internal.h</c> does: by the
/// MD5 of the first megabyte of the room-0 index file: a loose 00.LFL (or 00.MAN for the V1 demo),
/// or for the C64 the 00.LFL that <see cref="C64DiskImage"/> builds from the disks, as ScummVM's
/// <c>openDiskImage</c> does. The NES ROM needs its own reader first.
/// </summary>
public static class ScummDetector
{
    /// <summary>ScummVM hashes at most this much of a detection file (<c>kMD5FileSizeLimit</c>).</summary>
    private const int Md5FileSizeLimit = 1024 * 1024;

    private static readonly string[] IndexFiles = ["00.LFL", "00.MAN"];

    /// <summary>
    /// The SCUMM version and demo flag of each Maniac Mansion row of <c>detection_tables.h</c>.
    /// </summary>
    private static readonly Dictionary<string, (int Version, bool Demo)> Variants = new()
    {
        ["C64"] = (0, false),
        ["C64 Demo"] = (0, true),
        ["Apple II"] = (0, false),
        ["V1"] = (1, false),
        ["V1 Demo"] = (1, true),
        ["NES"] = (1, false),
        ["V2"] = (2, false),
        ["V2 Demo"] = (2, true),
        ["Remastered"] = (2, false),
    };

    /// <summary>The MD5 of the index file ScummVM detects a release by, as the bytes ScummVM keeps in its saves (<c>_gameMD5</c>).</summary>
    public static byte[] IndexMd5(IGameContainer container)
    {
        container = C64DiskImage.Open(container) ?? container;
        foreach (var name in IndexFiles)
        {
            if (container.Exists(name))
            {
                using var stream = container.Open(name);
                var buffer = new byte[Math.Min(stream.Length, Md5FileSizeLimit)];
                stream.ReadExactly(buffer);
                return System.Security.Cryptography.MD5.HashData(buffer);
            }
        }

        return new byte[16];
    }

    public static ScummGame? Identify(IGameContainer container)
    {
        container = C64DiskImage.Open(container) ?? container;
        foreach (var name in IndexFiles)
        {
            if (!container.Exists(name))
            {
                continue;
            }

            using var stream = container.Open(name);
            var buffer = new byte[Math.Min(stream.Length, Md5FileSizeLimit)];
            stream.ReadExactly(buffer);

            if (ScummMd5Table.Find(Md5.ComputeHex(buffer)) is { } entry && Variants.TryGetValue(entry.Variant, out var variant))
            {
                return new ScummGame(entry, variant.Version, variant.Demo);
            }
        }

        return null;
    }
}
