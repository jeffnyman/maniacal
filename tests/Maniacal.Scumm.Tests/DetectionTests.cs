using Maniacal.Scumm;
using Maniacal.Scumm.Detection;
using Maniacal.Scumm.Storage;

namespace Maniacal.Scumm.Tests;

/// <summary>
/// Identifies the game data folders next to the repository. A test passes without checking when its
/// folder is absent. The folders are found from the MANIACAL_GAMES environment variable, or by
/// walking up from the test binary to the directory that holds them.
/// </summary>
public class DetectionTests
{
    [Theory]
    [InlineData("maniac_mansion_original_DOS.com", "V1", 1)]
    [InlineData("maniac_mansion_enhanced_DOS.com", "V2", 2)]
    public void Identifies_dos_releases(string folder, string variant, int version)
    {
        if (GameFolders.Find(folder) is not { } path)
        {
            return;
        }

        var game = ScummDetector.Identify(new DirectoryGameContainer(path));

        Assert.NotNull(game);
        Assert.Equal(variant, game.Variant);
        Assert.Equal(version, game.Version);
        Assert.Equal("en", game.Language);
        Assert.Equal("dos", game.Platform);
        Assert.False(game.IsDemo);
    }

    /// <summary>The C64 release is identified by the index built from its disks, as ScummVM does.</summary>
    [Fact]
    public void Identifies_the_c64_release_from_its_disks()
    {
        if (GameFolders.Find("maniac_mansion_c64") is not { } path)
        {
            return;
        }

        var game = ScummDetector.Identify(new DirectoryGameContainer(path));

        Assert.NotNull(game);
        Assert.Equal("C64", game.Variant);
        Assert.Equal(0, game.Version);
        Assert.Equal("c64", game.Platform);
        Assert.Equal(1188, game.Entry.Size);
        Assert.Equal("eea4d9ac2fb6f145945a308e8866915b", game.Entry.Md5);
        Assert.False(game.IsDemo);
    }

    [Fact]
    public void Provider_reports_maniac_mansion()
    {
        if (GameFolders.Find("maniac_mansion_enhanced_DOS.com") is not { } path)
        {
            return;
        }

        var detected = Assert.Single(new ScummEngineProvider().Detect(new DirectoryGameContainer(path)));

        Assert.Equal("scumm", detected.EngineId);
        Assert.Equal("maniac", detected.GameId);
        Assert.Equal("Maniac Mansion (V2) [scumm:maniac]", detected.ToString());
    }

    /// <summary>
    /// The 1987 demo's 00.MAN is the full release's 00.LFL, byte for byte: as ScummVM does, the
    /// file's name makes it the demo.
    /// </summary>
    [Theory]
    [InlineData(WalkthroughTests.V1Demo, "V1 Demo", 1)]
    [InlineData(WalkthroughTests.V2Demo, "V2 Demo", 2)]
    [InlineData(WalkthroughTests.C64Demo, "C64 Demo", 0)]
    public void Identifies_the_demos(string folder, string variant, int version)
    {
        if (GameFolders.Find(folder) is not { } path)
        {
            return;
        }

        var game = ScummDetector.Identify(new DirectoryGameContainer(path));

        Assert.NotNull(game);
        Assert.Equal(variant, game.Variant);
        Assert.Equal(version, game.Version);
        Assert.True(game.IsDemo);
    }

    /// <summary>Maniacal plays the English releases for the Commodore 64 and DOS and their demos, and the American NES cartridge, and refuses the rest by name.</summary>
    [Theory]
    [InlineData("b250d0f9cc83f80ced56fe11a4fb057c", "V2", null)]
    [InlineData("624cdb93654667c869d204a64af7e57f", "V2", null)]
    [InlineData("7f45ddd6dbfbf8f80c0c0efea4c295bc", "V1", null)]
    [InlineData("eea4d9ac2fb6f145945a308e8866915b", "C64", null)]
    [InlineData("183d7464902d40d00800e8ee1f04117c", "V2", "This is Maniac Mansion (V2, German, DOS), which Maniacal does not play: it plays only the English releases.")]
    [InlineData("0c331637580950aea2346e012ef2a868", "V2", "This is Maniac Mansion (V2, English, Atari ST), which Maniacal does not play: it plays only the Commodore 64, DOS and NES releases.")]
    [InlineData("91d5db93187fab54d823f73bd6441cb6", "NES", null)]
    [InlineData("17f7296f63c78642724f057fd8e736a7", "NES", "This is Maniac Mansion (NES, English, NES), which Maniacal does not play: of the NES releases it plays only the American cartridge.")]
    [InlineData("40564ec47da48a67787d1f9bd043902a", "V2 Demo", null)]
    [InlineData("2cb46375dd5cdfd023e2f07e0a21b530", "C64 Demo", null)]
    public void Plays_the_english_c64_dos_and_nes_releases(string md5, string variant, string? refusal)
    {
        var entry = ScummMd5Table.Find(md5)!;
        var game = new ScummGame(entry, variant, variant.StartsWith("V2", StringComparison.Ordinal) ? 2 : variant.StartsWith("C64", StringComparison.Ordinal) ? 0 : 1, variant.EndsWith("Demo", StringComparison.Ordinal));

        Assert.Equal(refusal is null, game.IsSupported);
        if (refusal is not null)
        {
            Assert.Equal(refusal, $"This is {game.Description}, which Maniacal does not play: {game.Unsupported}.");
        }
    }


    [Fact]
    public void Md5_table_holds_every_scummvm_row()
    {
        Assert.Equal(40, ScummMd5Table.Entries.Length);
        Assert.All(ScummMd5Table.Entries, e => Assert.Equal("maniac", e.GameId));
    }
}

internal static class GameFolders
{
    /// <summary>
    /// The game folder of that name, its case aside (maniac_mansion_C64 is maniac_mansion_c64 on
    /// any system), in MANIACAL_GAMES or a folder above the test binary; null when there is none.
    /// </summary>
    public static string? Find(string folder)
    {
        var fromEnv = Environment.GetEnvironmentVariable("MANIACAL_GAMES");
        if (!string.IsNullOrEmpty(fromEnv) && In(fromEnv, folder) is { } found)
        {
            return found;
        }

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (In(dir.FullName, folder) is { } candidate)
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? In(string parent, string folder)
        => Directory.Exists(parent)
            ? Directory.EnumerateDirectories(parent).FirstOrDefault(d => string.Equals(Path.GetFileName(d), folder, StringComparison.OrdinalIgnoreCase))
            : null;
}
