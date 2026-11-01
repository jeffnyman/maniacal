using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Storage;

namespace Maniacal.Scumm.Detection;

/// <summary>A Maniac Mansion release identified in a game folder.</summary>
/// <param name="Entry">The matching row of ScummVM's MD5 table.</param>
/// <param name="Variant">The release, as <c>detection_tables.h</c> names it: "V1", "V2 Demo", "C64" and so on.</param>
/// <param name="Version">The SCUMM version: 0 for C64 and Apple II, 1 for DOS V1 and NES, 2 for V2.</param>
/// <param name="IsDemo">True for the demo releases.</param>
internal sealed record ScummGame(Md5Entry Entry, string Variant, int Version, bool IsDemo)
{
    public string? Language => Entry.Language;

    public string? Platform => Entry.Platform;

    /// <summary>
    /// Whether Maniacal plays the release: the English releases for the Commodore 64 and DOS and
    /// their demos, the ones it is tested on. Not other languages or platforms.
    /// </summary>
    public bool IsSupported => Unsupported is null;

    /// <summary>Why Maniacal does not play the release, or null when it does.</summary>
    public string? Unsupported
        => Language != "en" ? "it plays only the English releases"
        : Platform == "nes" ? "the NES release is not supported yet"
        : !(Platform == "dos" && Version is 1 or 2) && !(Platform == "c64" && Version == 0) ? "it plays only the Commodore 64 and DOS releases"
        : null;

    /// <summary>The release in words: <c>Maniac Mansion (V2, German, DOS)</c>.</summary>
    public string Description => $"Maniac Mansion ({Variant}, {LanguageName(Language)}, {PlatformName(Platform)})";

    private static string LanguageName(string? code) => code switch
    {
        "en" => "English",
        "de" => "German",
        "fr" => "French",
        "es" => "Spanish",
        "it" => "Italian",
        "ru" => "Russian",
        "sv" => "Swedish",
        "ca" => "Catalan",
        "he" => "Hebrew",
        "pt" => "Portuguese",
        "nl" => "Dutch",
        "ja" => "Japanese",
        null => "language unknown",
        _ => code,
    };

    private static string PlatformName(string? code) => code switch
    {
        "dos" => "DOS",
        "c64" => "Commodore 64",
        "amiga" => "Amiga",
        "atari" => "Atari ST",
        "nes" => "NES",
        "apple2gs" => "Apple IIgs",
        null => "platform unknown",
        _ => code,
    };
}

/// <summary>
/// Identifies Maniac Mansion releases the way ScummVM's <c>detection_internal.h</c> does: by the
/// MD5 of the first megabyte of the room-0 index file: a loose 00.LFL (or 00.MAN for the V1 demo),
/// or for the C64 and the NES the 00.LFL that <see cref="C64DiskImage"/> builds from the disks and
/// <see cref="NesRom"/> from the cartridge, as ScummVM's <c>openDiskImage</c> does.
/// </summary>
internal static class ScummDetector
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
        container = GameImages.Unpack(container);
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

    /// <summary>The release an index MD5 names, as a save keeps it, or null for one ScummVM's table does not know.</summary>
    public static ScummGame? FromMd5(ReadOnlySpan<byte> md5)
        => ScummMd5Table.Find(Convert.ToHexStringLower(md5)) is { } entry && Variants.TryGetValue(entry.Variant, out var variant)
            ? new ScummGame(entry, entry.Variant, variant.Version, variant.Demo)
            : null;

    public static ScummGame? Identify(IGameContainer container)
    {
        container = GameImages.Unpack(container);
        foreach (var name in IndexFiles)
        {
            if (!container.Exists(name))
            {
                continue;
            }

            using var stream = container.Open(name);
            var buffer = new byte[Math.Min(stream.Length, Md5FileSizeLimit)];
            stream.ReadExactly(buffer);

            // ScummVM tells two demos from their releases by the files' names, not the MD5:
            // the V1 demo's 00.MAN is the full release's 00.LFL (detection_tables.h's "%02d.MAN",
            // "V1 Demo"), and the C64 demo's table row is "C64" with the extra "Demo", its disk
            // being maniacdemo.d64 ("C64 Demo")
            if (ScummMd5Table.Find(Md5.ComputeHex(buffer)) is { } entry
                && (name == "00.MAN" && entry.Variant == "V1" ? "V1 Demo"
                    : container is C64DiskImage { IsDemo: true } ? "C64 Demo"
                    : entry.Variant) is var variantName
                && Variants.TryGetValue(variantName, out var variant))
            {
                return new ScummGame(entry, variantName, variant.Version, variant.Demo);
            }
        }

        return null;
    }
}
