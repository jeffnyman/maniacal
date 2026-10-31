namespace Maniacal.Scumm.Tests;

/// <summary>The front door, as a consumer of the package uses it: open a folder, see what it holds, play or look inside.</summary>
public class ManiacMansionTests
{
    [Theory]
    [InlineData(WalkthroughTests.V2, "Maniac Mansion (V2, English, DOS)", 2, false)]
    [InlineData(WalkthroughTests.V1, "Maniac Mansion (V1, English, DOS)", 1, false)]
    [InlineData(WalkthroughTests.C64, "Maniac Mansion (C64, English, Commodore 64)", 0, false)]
    [InlineData("maniac_mansion_demo_ega_dos", "Maniac Mansion (V1 Demo, English, DOS)", 1, true)]
    [InlineData("maniac_mansion_demo_vga_dos", "Maniac Mansion (V2 Demo, English, DOS)", 2, true)]
    public void A_folder_opens_as_its_release(string folder, string description, int version, bool demo)
    {
        if (GameFolders.Find(folder) is not { } path)
        {
            return;
        }

        var game = ManiacMansion.Open(path);

        Assert.Equal(description, game.Description);
        Assert.Equal(description, game.ToString());
        Assert.Equal(version, game.Version);
        Assert.Equal(demo, game.IsDemo);
        Assert.Equal("maniac", game.Detected.GameId);
    }

    [Fact]
    public void A_release_plays_and_is_looked_inside()
    {
        if (GameFolders.Find(WalkthroughTests.V2) is not { } path)
        {
            return;
        }

        var game = ManiacMansion.Open(path);
        var world = game.Play();
        world.Boot();
        for (int i = 0; i < 60; i++)
        {
            world.Step();
        }

        Assert.Equal(45, world.CurrentRoom?.Number);
        Assert.Equal(53, game.Inspect().Rooms.Count);
    }

    /// <summary>A release a folder below the one given is found, as the player finds one.</summary>
    [Fact]
    public void A_release_a_folder_below_is_found()
    {
        if (GameFolders.Find(WalkthroughTests.V2) is not { } path)
        {
            return;
        }

        string above = Directory.CreateTempSubdirectory("maniacal-").FullName;
        try
        {
            string below = Directory.CreateDirectory(Path.Combine(above, "MM")).FullName;
            foreach (var file in Directory.GetFiles(path))
            {
                File.Copy(file, Path.Combine(below, Path.GetFileName(file)));
            }

            Assert.Equal("Maniac Mansion (V2, English, DOS)", ManiacMansion.Open(above).Description);
        }
        finally
        {
            Directory.Delete(above, recursive: true);
        }
    }

    [Fact]
    public void A_folder_that_is_not_there_says_so()
    {
        string missing = Path.Combine(Path.GetTempPath(), "maniacal-no-such-folder-" + Guid.NewGuid());

        Assert.Throws<DirectoryNotFoundException>(() => ManiacMansion.Open(missing));
    }

    [Fact]
    public void A_folder_without_a_release_says_so()
    {
        string empty = Directory.CreateTempSubdirectory("maniacal-").FullName;
        try
        {
            var ex = Assert.Throws<FileNotFoundException>(() => ManiacMansion.Open(empty));
            Assert.Contains("no Maniac Mansion release", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(empty);
        }
    }
}
