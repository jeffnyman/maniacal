using System.Text;
using Maniacal.Scumm.Game;
using Maniacal.Scumm.Graphics;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Scripts;
using Maniacal.Scumm.Storage;

namespace Maniacal.Scumm.Tests;

/// <summary>
/// The NES's interface: its verbs, sentence line, inventory and cursor in its verb area, played
/// by clicks as a player plays it. The tests pass without checking when the cartridge's folder is
/// absent.
/// </summary>
public class NesInterfaceTests
{
    private const int Dave = 3, FrontOfTheHouse = 1, Hall = 10, Driveway = 44, Key = 55;

    // the first inventory box, its top left in the verb area (initNESMouseOver)
    private const int FirstBoxX = 16, FirstBoxY = 48;

    /// <summary>The game started with Razor and Bernard beside Dave, and Dave walked to the front of the house.</summary>
    private static GameWorld? AtTheFrontOfTheHouse()
    {
        if (GameFolders.Find(WalkthroughTests.Nes) is not { } path || NesRom.Open(new DirectoryGameContainer(path)) is not { } rom)
        {
            return null;
        }

        var world = new GameWorld(new ResourceLoader(rom, ResourceIndex.Parse(LflFile.Read(rom, 0))));
        world.Boot();
        Steps(world, 600);
        foreach (int obj in new[] { 261, 263, 403 })
        {
            world.ClickObject(obj);
            Steps(world, 100);
        }

        Until(world, () => world.CurrentRoom?.Number == Driveway && world.CursorShown, 3000);
        world.ClickObject(world.ObjectsHere().First(o => o.X == 0 && o.Touchable).Number);
        Until(world, () => world.CurrentRoom?.Number == FrontOfTheHouse, 2000);
        Settle(world);
        return world;
    }

    private static void Steps(GameWorld world, int count)
    {
        for (int i = 0; i < count; i++)
        {
            world.Step();
        }
    }

    private static void Until(GameWorld world, Func<bool> done, int most)
    {
        for (int i = 0; i < most && !done(); i++)
        {
            world.Step();
        }
    }

    // the kid stands and can be played again
    private static void Settle(GameWorld world)
    {
        Until(world, () => world.Actors[Dave].Moving == 0 && world.CursorShown, 800);
        Steps(world, 30);
    }

    private static void Verb(GameWorld world, string name)
    {
        var slot = world.Verbs.Slots.First(v => v.Id != 0 && Encoding.Latin1.GetString(v.Name).Replace("@", "").Equals(name, StringComparison.OrdinalIgnoreCase));
        world.Click(slot.X + 4, slot.Y + 4);
        Steps(world, 3);
    }

    private static void Object(GameWorld world, string name)
    {
        Assert.True(world.ClickObject(world.ObjectsHere().First(o => o.Name == name).Number));
        Steps(world, 3);
    }

    /// <summary>
    /// The NES's twelve verbs, in its font, in four columns from a column in; the sentence line
    /// above them; the inventory under them with each kid's CD player; on the NES's black.
    /// </summary>
    [Fact]
    public void The_verb_area_shows_the_verbs_the_sentence_and_the_inventory()
    {
        if (AtTheFrontOfTheHouse() is not { } world)
        {
            return;
        }

        var verbs = world.Verbs;

        Assert.Equal(160, verbs.Top);
        Assert.Equal(256, verbs.Pixels.Width);
        Assert.Equal(80, verbs.Pixels.Height);
        Assert.Equal(["Push", "Open", "Get", "New Kid", "Pull", "Close", "Use", "Turn On", "Give", "Go To", "Read", "Turn Off"],
            verbs.Slots.Where(v => v.Id != 0).Select(v => Encoding.Latin1.GetString(v.Name).Replace("@", "")));
        Assert.All(verbs.Slots.Where(v => v.Id != 0), v => Assert.Contains(v.X, new[] { 16, 72, 128, 176 }));
        Assert.Equal(NesBitmap.Black, verbs.Pixels[0, 0]);
        Assert.Contains(verbs.Pixels.Row(4).ToArray(), p => p != NesBitmap.Black);
        Assert.Contains(verbs.Pixels.Row(FirstBoxY + 4).ToArray(), p => p != NesBitmap.Black);

        // the screen shows the verb area under the room
        var screen = world.Screen();
        Assert.Equal(verbs.Pixels.Row(20).ToArray(), screen.Row(160 + 20).ToArray());
    }

    /// <summary>
    /// Into the house as the NES plays it: Pull the door mat, Get the key, Use the key (picked from
    /// the inventory, where the NES lists it first) with the front door, Open it, Go To it.
    /// Use waits for its second object because the scripts ask the key's preposition, the object
    /// under the mouse becoming the sentence's as the mouse passes (script 7).
    /// </summary>
    [Fact]
    public void Dave_unlocks_the_front_door_and_goes_in()
    {
        if (AtTheFrontOfTheHouse() is not { } world)
        {
            return;
        }

        // to the porch, the door mat in view
        world.Click(40, 32 + 110);
        Settle(world);

        Verb(world, "Pull");
        Object(world, "door mat");
        Settle(world);
        Verb(world, "Get");
        Object(world, "key");
        Settle(world);
        Assert.Contains(Key, world.Inventory);
        Assert.Equal(Key, world.FindInventory(Dave, world.InventoryCount(Dave)));

        Verb(world, "Use");
        world.Click(FirstBoxX + 8, world.Verbs.Top + FirstBoxY + 4);
        Steps(world, 4);
        Assert.Equal(Key, world.Variables[27]);
        Assert.NotEqual(0, world.Variables[29]);
        Object(world, "front door");
        Settle(world);
        Verb(world, "Open");
        Object(world, "front door");
        Settle(world);
        Verb(world, "Go To");
        Object(world, "front door");
        Until(world, () => world.CurrentRoom?.Number == Hall, 800);

        Assert.Equal(Hall, world.CurrentRoom?.Number);
        Assert.DoesNotContain(world.Unhandled, u => !u.Contains("resourceRoutines", StringComparison.Ordinal));
    }

    /// <summary>The NES's cursor is a tile of the costume set, 8 by 8 from the mouse, its corner colour left out.</summary>
    [Fact]
    public void The_cursor_is_the_costume_sets_arrow()
    {
        if (AtTheFrontOfTheHouse() is not { } world)
        {
            return;
        }

        world.MoveMouse(100, 100);
        var screen = new Surface8(256, 240);
        screen.Fill(NesBitmap.Black);
        world.DrawCursor(screen);

        Assert.Contains(Enumerable.Range(0, 64), i => screen[100 + i % 8, 100 + i / 8] != NesBitmap.Black);
        Assert.All(Enumerable.Range(0, 256 * 240).Where(i => i % 256 is < 100 or >= 108 || i / 256 is < 100 or >= 108), i => Assert.Equal(NesBitmap.Black, screen.Pixels[i]));
    }
}
