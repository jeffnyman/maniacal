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

    [Fact]
    public void Md5_table_holds_every_scummvm_row()
    {
        Assert.Equal(40, ScummMd5Table.Entries.Length);
        Assert.All(ScummMd5Table.Entries, e => Assert.Equal("maniac", e.GameId));
    }
}

internal static class GameFolders
{
    public static string? Find(string folder)
    {
        var fromEnv = Environment.GetEnvironmentVariable("MANIACAL_GAMES");
        if (!string.IsNullOrEmpty(fromEnv) && Directory.Exists(Path.Combine(fromEnv, folder)))
        {
            return Path.Combine(fromEnv, folder);
        }

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, folder);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
