using Maniacal.Scumm.Game;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Scripts;
using Maniacal.Scumm.Storage;
using Maniacal.Scumm.View;

namespace Maniacal.Scumm.Tests;

/// <summary>
/// The NES release played headless: its scripts on the version 1 interpreter with the NES's
/// differences, before its rooms, costumes and interface are drawn. Maniacal refuses the release
/// for now, so the world is made from the cartridge directly. The tests pass without checking when
/// the cartridge's folder is absent.
/// </summary>
public class NesPlayTests
{
    // the rooms of the title, the kid selection and the driveway
    private const int TitleRoom = 33, LogoRoom = 49, SelectionRoom = 45, Driveway = 44, FrontOfTheHouse = 1;

    // the kids' portraits on the selection screen, the start button, and Dave
    private const int Razor = 261, Bernard = 263, Start = 403, Dave = 3;

    private static GameWorld? Open()
    {
        if (GameFolders.Find(WalkthroughTests.Nes) is not { } path || NesRom.Open(new DirectoryGameContainer(path)) is not { } rom)
        {
            return null;
        }

        return new GameWorld(new ResourceLoader(rom, ResourceIndex.Parse(LflFile.Read(rom, 0))));
    }

    // steps until a room is on screen, and the rooms on the way
    private static List<int> StepsTo(GameWorld world, int room, int most)
    {
        var rooms = new List<int>();
        for (int step = 0; step < most && world.CurrentRoom?.Number != room; step++)
        {
            world.Step();
            if (world.CurrentRoom?.Number is { } now && (rooms.Count == 0 || rooms[^1] != now))
            {
                rooms.Add(now);
            }
        }

        return rooms;
    }

    /// <summary>
    /// The title plays through, "Twenty years ago today..." over the meteor, then the logo, whose
    /// scroll waits for a camera as wide as the NES's screen (with ScummVM's patch for one 256
    /// pixels wide), to the kid selection.
    /// </summary>
    [Fact]
    public void The_title_plays_to_the_kid_selection()
    {
        if (Open() is not { } world)
        {
            return;
        }

        world.Boot();
        var rooms = StepsTo(world, SelectionRoom, 2000);

        Assert.Equal(Camera.NesScreenWidth, world.Camera.ScreenWidth);
        Assert.Equal([TitleRoom, LogoRoom, SelectionRoom], rooms.Where(r => r != 0));
        Assert.Equal(8, world.ObjectsHere().Count(o => o.Touchable));
        Assert.DoesNotContain(world.Unhandled, u => !u.Contains("resourceRoutines", StringComparison.Ordinal));
    }

    /// <summary>
    /// Two kids picked beside Dave, the game starts in the driveway, where each kid is given a CD
    /// player of his own (script 131), the NES's twelve verbs show, and Dave walks out by the left.
    /// </summary>
    [Fact]
    public void Kids_are_picked_and_the_game_starts()
    {
        if (Open() is not { } world)
        {
            return;
        }

        world.Boot();
        StepsTo(world, SelectionRoom, 2000);
        for (int step = 0; step < 100; step++)
        {
            world.Step();
        }

        foreach (int obj in new[] { Razor, Bernard, Start })
        {
            Assert.True(world.ClickObject(obj));
            for (int step = 0; step < 100; step++)
            {
                world.Step();
            }
        }

        StepsTo(world, Driveway, 3000);
        for (int step = 0; step < 3000 && !world.CursorShown; step++)
        {
            world.Step();
        }

        Assert.Equal(Driveway, world.CurrentRoom?.Number);
        Assert.Equal(["Bernard", "Dave", "Razor"], world.ActorsHere().Select(a => a.Name).Order());
        Assert.Equal(3, world.Inventory.Count(o => o != 0 && world.NameOf(o) == "CD Player"));
        Assert.Equal(12, world.Verbs.Slots.Count(v => v.Id != 0));
        Assert.Equal(Dave, world.Variables[ScriptEngine.VarEgo]);

        // the kids stand drawn in their costumes, from costume set 0, the kids' own
        var screen = world.Screen();
        var room = Graphics.RoomRenderer.Render(world.CurrentRoom!, world.StateOf);
        foreach (var kid in world.Actors.OfType<ActorNes>().Where(a => a.InCurrentRoom && a.Visible))
        {
            Assert.NotNull(kid.CostumeNes);
            Assert.NotEqual(Costumes.CostumeState.None, kid.State.Position(0));
            int x = kid.X * 8 - world.Camera.Left + world.RoomScreenLeft, y = kid.Y * 2 + world.Layout.RoomTop;
            Assert.Contains(Enumerable.Range(-8, 16).SelectMany(dx => Enumerable.Range(-40, 40).Select(dy => (dx, dy))),
                d => screen[x + d.dx, y + d.dy] != room.Pixels[x + d.dx - world.RoomScreenLeft + world.Camera.Left, y + d.dy - world.Layout.RoomTop]);
        }

        Assert.Equal(0, world.NesCostumeSetNumber);

        var exit = world.ObjectsHere().First(o => o.X == 0 && o.Touchable);
        Assert.True(world.ClickObject(exit.Number));
        StepsTo(world, FrontOfTheHouse, 2000);

        Assert.Equal(FrontOfTheHouse, world.CurrentRoom?.Number);
        Assert.DoesNotContain(world.Unhandled, u => !u.Contains("resourceRoutines", StringComparison.Ordinal));
    }
}
