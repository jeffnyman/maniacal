using Maniacal.Scumm.Game;
using Maniacal.Scumm.Graphics;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Scripts;
using Maniacal.Scumm.Storage;

namespace Maniacal.Scumm.Tests;

/// <summary>
/// The DOS demos, as ScummVM runs them: the 1987 demo's rooms read from its <c>NN.MAN</c> files,
/// both booted into their demo mode (script 9), every actor in the costume of its number, and the
/// 1988 demo's colours fixed for its script.
/// </summary>
public class DemoTests
{
    private const string V1Demo = WalkthroughTests.V1Demo, V2Demo = WalkthroughTests.V2Demo;

    private static GameWorld? Open(string folder)
        => GameFolders.Find(folder) is { } path ? new GameWorld(ResourceLoader.Open(new DirectoryGameContainer(path))) : null;

    private static void Steps(GameWorld world, int count)
    {
        for (int i = 0; i < count; i++)
        {
            world.Step();
        }
    }

    [Theory]
    [InlineData(V1Demo, 1)]
    [InlineData(V2Demo, 2)]
    public void A_demo_boots_into_its_demo_mode(string folder, int version)
    {
        if (Open(folder) is not { } world)
        {
            return;
        }

        world.Boot();
        world.Step();

        Assert.Equal(version, world.Version);
        Assert.True(world.IsDemo);
        Assert.Contains(world.RunningScripts(), s => s.Kind == ScriptKind.Global && s.Number == GameWorld.DemoBootScript);
        Assert.All(world.Actors.Skip(1), a => Assert.Equal(a.Number, a.CostumeNumber));
    }

    /// <summary>Both demos show the title, the front porch and the lab, every instruction handled.</summary>
    [Theory]
    [InlineData(V1Demo, 1100, 1600)]
    [InlineData(V2Demo, 1400, 2100)]
    public void A_demo_plays_through_the_mansion_to_the_lab(string folder, int inTheLab, int outOfTheLab)
    {
        if (Open(folder) is not { } world)
        {
            return;
        }

        world.Boot();
        var rooms = new List<int>();
        for (int step = 0; step < outOfTheLab; step++)
        {
            world.Step();
            if (world.CurrentRoom?.Number is { } room && (rooms.Count == 0 || rooms[^1] != room))
            {
                rooms.Add(room);
            }

            if (step == inTheLab)
            {
                Assert.Equal(31, world.CurrentRoom?.Number);
            }
        }

        Assert.Equal([33, 1, 10, 31, 10], rooms.Where(r => r != 49));
        Assert.DoesNotContain(world.Unhandled, call => !call.Contains("resourceRoutines", StringComparison.Ordinal));
    }

    /// <summary>
    /// Script 9 starts script 175, whose cutscene freezes script 9, and then, frozen as it is, goes
    /// on to start script 173 and its quotes: in versions 0 to 2 a caller resumes after a nested
    /// script whatever its freeze bit (runScriptNested asks freezeCount, which freezeScripts never
    /// raises there). Checked against ScummVM 2026.4.0, which shows the first quote by the meteor.
    /// </summary>
    [Fact]
    public void The_1988_demo_shows_its_quotes_by_the_meteor()
    {
        if (Open(V2Demo) is not { } world)
        {
            return;
        }

        world.Boot();
        Steps(world, 120);

        Assert.Contains(world.RunningScripts(), s => s.Kind == ScriptKind.Global && s.Number == 173);
        Assert.Contains(world.Verbs.Slots, v => System.Text.Encoding.Latin1.GetString(v.Name).Contains("Game of the Year", StringComparison.Ordinal));
    }

    /// <summary>resetScumm and o2_actorOps give the 1988 demo's script its colours.</summary>
    [Fact]
    public void The_1988_demo_has_its_colours()
    {
        if (Open(V2Demo) is not { } world)
        {
            return;
        }

        Assert.Equal(1, world.Actors[3].Palette[3]);
        Assert.Equal([15, 7, 2, 5, 14], new[] { 9, 10, 11, 13, 23 }.Select(n => world.Actors[n].TalkColor));
    }

    /// <summary>The 1987 demo's font has the 1988 release's apostrophe, as ScummVM's charset gives it.</summary>
    [Fact]
    public void The_1987_demo_has_the_1988_apostrophe()
    {
        Assert.Equal(Font.English(2).Glyph('\'').ToArray(), Font.English(1, demo: true).Glyph('\'').ToArray());
        Assert.NotEqual(Font.English(2).Glyph('\'').ToArray(), Font.English(1).Glyph('\'').ToArray());
    }
}
