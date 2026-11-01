namespace Maniacal.Scumm.Tests;

/// <summary>The front door, as a consumer of the package uses it: open a folder, see what it holds, play or look inside.</summary>
public class ManiacMansionTests
{
    [Theory]
    [InlineData(WalkthroughTests.V2, "Maniac Mansion (V2, English, DOS)", 2, false)]
    [InlineData(WalkthroughTests.V1, "Maniac Mansion (V1, English, DOS)", 1, false)]
    [InlineData(WalkthroughTests.C64, "Maniac Mansion (C64, English, Commodore 64)", 0, false)]
    [InlineData(WalkthroughTests.V1Demo, "Maniac Mansion (V1 Demo, English, DOS)", 1, true)]
    [InlineData(WalkthroughTests.V2Demo, "Maniac Mansion (V2 Demo, English, DOS)", 2, true)]
    [InlineData(WalkthroughTests.C64Demo, "Maniac Mansion (C64 Demo, English, Commodore 64)", 0, true)]
    [InlineData(WalkthroughTests.Nes, "Maniac Mansion (NES, English, NES)", 1, false)]
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

    /// <summary>
    /// The NES release opened at the front door plays: its title screens first, on its 256 by 240
    /// screen in its 64 colours, then its boot script, to the kid selection.
    /// </summary>
    [Fact]
    public void The_nes_release_plays()
    {
        if (GameFolders.Find(WalkthroughTests.Nes) is not { } path)
        {
            return;
        }

        var world = ManiacMansion.Open(path).Play();
        world.Boot();
        world.Step();

        Assert.Equal((256, 240), (world.Screen().Width, world.Screen().Height));
        Assert.Equal(Graphics.Palettes.Nes.ToArray(), world.Palette.ToArray());
        Assert.Null(world.CurrentRoom);
        for (int i = 0; i < 3000 && world.CurrentRoom?.Number != 45; i++)
        {
            world.Step();
        }

        Assert.Equal(45, world.CurrentRoom?.Number);
    }

    /// <summary>The palette can be kept and used later, in a lambda, where a span could not be.</summary>
    [Theory]
    [InlineData(WalkthroughTests.V2, 16)]
    [InlineData(WalkthroughTests.C64, 17)]
    public void The_palette_can_be_kept(string folder, int colours)
    {
        if (GameFolders.Find(folder) is not { } path)
        {
            return;
        }

        var game = ManiacMansion.Open(path);
        var world = game.Play();
        ReadOnlyMemory<Graphics.Rgb> kept = world.Palette;
        Func<int, Graphics.Rgb> colour = n => kept.Span[n];

        Assert.Equal(colours, kept.Length);
        Assert.Equal(Graphics.Palettes.ForVersion(world.Version).ToArray(), kept.ToArray());
        Assert.Equal(kept.ToArray(), game.Inspect().Palette.ToArray());
        Assert.Equal(Graphics.Palettes.ForVersion(world.Version)[1], colour(1));
    }

    /// <summary>
    /// A save keeps the MD5 of the release it was made with; one made with another release (whose
    /// data it cannot fit) is refused by name, before the world is touched. Found by a program
    /// written against the package, where such a save loaded and the game then failed.
    /// </summary>
    [Theory]
    [InlineData(WalkthroughTests.V2, WalkthroughTests.V1, "This save was made with Maniac Mansion (V2, English, DOS), not this release, Maniac Mansion (V1, English, DOS)")]
    [InlineData(WalkthroughTests.V1, WalkthroughTests.V2, "This save was made with Maniac Mansion (V1, English, DOS), not this release, Maniac Mansion (V2, English, DOS)")]
    [InlineData(WalkthroughTests.C64, WalkthroughTests.V2, "This save was made with Maniac Mansion (C64, English, Commodore 64), not this release, Maniac Mansion (V2, English, DOS)")]
    [InlineData(WalkthroughTests.V2, WalkthroughTests.C64, "This save was made with Maniac Mansion (V2, English, DOS), not this release, Maniac Mansion (C64, English, Commodore 64)")]
    [InlineData(WalkthroughTests.Nes, WalkthroughTests.V1, "This save was made with Maniac Mansion (NES, English, NES), not this release, Maniac Mansion (V1, English, DOS)")]
    [InlineData(WalkthroughTests.V1, WalkthroughTests.Nes, "This save was made with Maniac Mansion (V1, English, DOS), not this release, Maniac Mansion (NES, English, NES)")]
    public void A_save_from_another_release_is_refused(string from, string into, string message)
    {
        if (GameFolders.Find(from) is not { } fromPath || GameFolders.Find(into) is not { } intoPath)
        {
            return;
        }

        var saver = ManiacMansion.Open(fromPath).Play();
        saver.Boot();
        for (int i = 0; i < 60; i++)
        {
            saver.Step();
        }

        var save = saver.Save("title", new DateTime(2026, 11, 1));
        var loader = ManiacMansion.Open(intoPath).Play();
        loader.Boot();
        loader.Step();
        int steps = loader.Steps;

        var ex = Assert.Throws<InvalidDataException>(() => loader.Load(save));
        Assert.StartsWith(message, ex.Message, StringComparison.Ordinal);
        Assert.Equal(steps, loader.Steps);

        // the same save loads into its own release
        var again = ManiacMansion.Open(fromPath).Play();
        Assert.Equal("title", again.Load(save).Name);
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
