using System.Text;
using Daventry.Core.Hosting;
using Daventry.Core.Input;
using Daventry.Core.Storage;
using Maniacal.Scumm.Game;
using Maniacal.Scumm.Graphics;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Scripts;

namespace Maniacal.Scumm.Tests;

public class VerbAreaTests
{
    private const int Top = 144;

    private static VerbArea Area(int userState = VerbArea.ShowSentence | VerbArea.ShowInventory | VerbArea.ShowVerbs)
        => new(Font.English(2), Top, () => userState);

    private static byte[] Text(string text) => Encoding.Latin1.GetBytes(text);

    /// <summary>Whether a character cell of the area, counted in screen lines, has a pixel of a colour.</summary>
    private static bool Drawn(VerbArea area, int x, int y, int color)
    {
        for (int row = y - Top; row < y - Top + Font.Height; row++)
        {
            if (area.Pixels.Row(row).Slice(x, Font.CharWidth).Contains((byte)color))
            {
                return true;
            }
        }

        return false;
    }

    [Fact]
    public void A_verb_shows_where_the_script_puts_it_and_its_fillers_can_be_clicked()
    {
        var area = Area();

        area.SetVerb(1, 9, 0, 19, 0, Text("Push@@@"), version: 2);

        Assert.True(Drawn(area, 0, 152, 2));
        Assert.False(Drawn(area, 32, 152, 2));
        Assert.Equal(1, area.FindAt(50, 155));
        Assert.Equal(0, area.FindAt(56, 155));
        Assert.Equal(0, area.FindAt(10, 160));
        Assert.Equal(1, area.SlotOf(9));
    }

    [Fact]
    public void Version_1_puts_verbs_a_line_lower()
    {
        var area = new VerbArea(Font.English(1), 152, () => VerbArea.ShowVerbs);

        area.SetVerb(1, 9, 0, 19, 0, Text("Push"), version: 1);

        Assert.Equal(160, area.Slots[1].Y);
        Assert.Equal(1, area.FindAt(5, 163));
    }

    [Fact]
    public void Verbs_dim_hide_and_go()
    {
        var area = Area();
        area.SetVerb(1, 9, 0, 19, 0, Text("Push"), version: 2);

        area.SetMode(9, 2);
        Assert.True(Drawn(area, 0, 152, 8));
        Assert.Equal(0, area.FindAt(5, 155));

        area.SetMode(9, 0);
        Assert.False(Drawn(area, 0, 152, 8));

        area.SetMode(9, 1);
        area.Kill(1);
        Assert.False(Drawn(area, 0, 152, 2));
        Assert.Equal(0, area.SlotOf(9));
    }

    [Fact]
    public void The_verb_under_the_mouse_is_highlighted_while_the_verbs_show()
    {
        var area = Area();
        area.SetVerb(1, 9, 0, 19, 0, Text("Push"), version: 2);

        area.MouseOver(1);
        Assert.True(Drawn(area, 0, 152, 14));

        area.MouseOver(0);
        Assert.True(Drawn(area, 0, 152, 2));

        var hidden = Area(userState: 0);
        hidden.SetVerb(1, 9, 0, 19, 0, Text("Push"), version: 2);
        hidden.MouseOver(1);
        Assert.False(Drawn(hidden, 0, 152, 14));
    }

    [Fact]
    public void The_sentence_line_fills_the_width_and_only_shows_while_on()
    {
        var area = Area();

        // fillers take no room: the 40 letters after them reach the right edge
        area.DrawSentence(Text("@@" + new string('A', 45)));

        Assert.True(Drawn(area, 0, Top, 13));
        Assert.True(Drawn(area, 39 * 8, Top, 13));

        var off = Area(userState: VerbArea.ShowVerbs);
        off.DrawSentence(Text("Walk to"));
        Assert.False(Drawn(off, 0, Top, 13));
    }

    [Fact]
    public void The_inventory_shows_four_names_and_scrolls_by_two()
    {
        var area = Area();
        string[] names = ["one", "two", "three", "four", "five", "six"];

        area.DrawInventory(names);

        // two names a row, either side of the arrows; only the down arrow, as nothing is above
        Assert.True(Drawn(area, 0, Top + 32, 13));
        Assert.True(Drawn(area, 176, Top + 32, 13));
        Assert.True(Drawn(area, 0, Top + 40, 13));
        Assert.True(Drawn(area, 152, Top + 40, 1));
        Assert.False(Drawn(area, 152, Top + 32, 1));

        Assert.Equal(-2, area.ClickInventory(160, Top + 44, names.Length));
        Assert.Equal(2, area.InventoryOffset);
        area.DrawInventory(names);
        Assert.True(Drawn(area, 152, Top + 32, 1));
        Assert.False(Drawn(area, 152, Top + 40, 1));

        // the places count from the top left, along the rows
        Assert.Equal(3, area.ClickInventory(200, Top + 44, names.Length));
        Assert.Equal(-1, area.ClickInventory(200, Top + 20, names.Length));
    }

    [Fact]
    public void The_inventory_box_under_the_mouse_is_highlighted()
    {
        var area = Area();
        area.DrawInventory(["one", "two"]);

        area.CheckMouseOver(10, Top + 34, cursorShown: true);
        Assert.Equal(0, area.MouseOverBox);
        Assert.True(Drawn(area, 0, Top + 32, 14));

        area.CheckMouseOver(10, Top + 10, cursorShown: true);
        Assert.Equal(-1, area.MouseOverBox);
        Assert.True(Drawn(area, 0, Top + 32, 13));
    }
}

/// <summary>The game played by clicking: the kid selection, then the verbs, walking and the sentence line.</summary>
public class PlayingTests
{
    private const string V1Folder = "maniac_mansion_original_DOS.com";
    private const string V2Folder = "maniac_mansion_enhanced_DOS.com";

    // the second kid's portrait, the fifth kid's, and START, on the kid selection screen
    private static readonly (int X, int Y)[] Selection = [(82, 118), (200, 118), (290, 75)];

    private static void Steps(GameWorld world, int count)
    {
        for (int i = 0; i < count; i++)
        {
            world.Step();
        }
    }

    /// <summary>
    /// A verb, then the object twice: the first click puts it in the sentence, the second carries it
    /// out. With a second object, that one is clicked in the inventory instead, twice. Walk to goes
    /// on the first click.
    /// </summary>
    internal static void Use(GameWorld world, int verb, int obj, int inventoryObj = 0)
    {
        var slot = world.Verbs.Slots[world.Verbs.SlotOf(verb)];
        world.Click(slot.X + 4, slot.Y + 4);
        Steps(world, 2);
        var point = PointOn(world, obj);
        world.Click(point.X, point.Y);
        Steps(world, 2);

        // Walk to needs no second click: the sentence goes at once
        const int WalkTo = 13;
        Assert.Equal(verb == WalkTo ? 0 : obj, world.Engine.Variables[GameWorld.VarSentenceObject1]);
        if (inventoryObj != 0)
        {
            // the inventory's first place, top left
            int place = world.Inventory.ToList().IndexOf(inventoryObj);
            Assert.Equal(0, place);
            point = (8, world.Verbs.Top + VerbArea.InventoryTop + 4);
            world.Click(point.X, point.Y);
            Steps(world, 2);
            Assert.Equal(inventoryObj, world.Engine.Variables[GameWorld.VarSentenceObject2]);
        }

        world.Click(point.X, point.Y);
        var ego = world.Actors[world.Engine.Variables[ScriptEngine.VarEgo]];
        for (int step = 0; step < 600 && (world.Sentences.Count > 0 || ego.Moving != 0 || world.Engine.IsScriptRunning(ScriptEngine.SentenceScript)); step++)
        {
            world.Step();
        }

        Steps(world, 20);
    }

    /// <summary>A point on screen where the mouse finds the object: its middle, or the nearest place in its box that is not another object's.</summary>
    private static (int X, int Y) PointOn(GameWorld world, int obj)
    {
        var target = world.CurrentRoom!.FindObject(obj)!;
        var (x, y) = world.PositionOf(target);
        var points = from dy in Enumerable.Range(0, target.Height)
                     from dx in Enumerable.Range(0, target.Width)
                     let px = x + dx
                     let py = y + dy
                     orderby Math.Abs(dx - target.Width / 2) + Math.Abs(dy - target.Height / 2)
                     where px >= world.Camera.Left && px < world.Camera.Left + 320 && world.FindObject(px, py) == obj
                     select (px - world.Camera.Left, py + 16);
        return points.First();
    }

    /// <summary>Boots a release and picks Syd and Bernard by clicking, then plays on to the driveway.</summary>
    internal static GameWorld? SelectedKids(string folder)
    {
        if (GameFolders.Find(folder) is not { } path)
        {
            return null;
        }

        var world = new GameWorld(ResourceLoader.Open(new DirectoryGameContainer(path)));
        world.Boot();
        Steps(world, 300);
        foreach (var (x, y) in Selection)
        {
            world.Click(x, y);
            Steps(world, 100);
        }

        for (int step = 0; step < 3000 && !(world.CurrentRoom?.Number == 44 && world.CursorShown); step++)
        {
            world.Step();
        }

        return world;
    }

    [Theory]
    [InlineData(V1Folder)]
    [InlineData(V2Folder)]
    public void Kids_are_picked_by_clicking_and_the_game_starts_with_the_verbs(string folder)
    {
        if (SelectedKids(folder) is not { } world)
        {
            return;
        }

        Assert.Equal(44, world.CurrentRoom?.Number);
        Assert.Equal([1, 3, 5], world.Actors.Where(a => a.InCurrentRoom && a.Visible).Select(a => a.Number));
        Assert.Equal(3, world.Engine.Variables[ScriptEngine.VarEgo]);

        // script 164 made the verbs; the sentence line says the default verb
        Assert.NotEqual(0, world.Verbs.SlotOf(13));
        Assert.Equal(13, world.Engine.Variables[GameWorld.VarSentenceVerb]);
        Assert.Contains((byte)13, world.Verbs.Pixels.Row(3).ToArray());
    }

    [Theory]
    [InlineData(V1Folder)]
    [InlineData(V2Folder)]
    public void Clicking_the_room_walks_the_kid_there(string folder)
    {
        if (SelectedKids(folder) is not { } world)
        {
            return;
        }

        var dave = world.Actors[3];
        world.Click(60, 125);
        world.Step();
        Assert.NotEqual(0, dave.Moving);

        for (int step = 0; step < 500 && dave.Moving != 0; step++)
        {
            world.Step();
        }

        Assert.Equal(7, dave.X);
        Assert.Equal(270, dave.Facing);
    }

    /// <summary>With What is, the sentence line names whatever object the mouse is over.</summary>
    [Fact]
    public void What_is_names_the_object_under_the_mouse()
    {
        if (SelectedKids(V2Folder) is not { } world)
        {
            return;
        }

        var whatIs = world.Verbs.Slots[world.Verbs.SlotOf(15)];
        world.Click(whatIs.X + 4, whatIs.Y + 4);
        Steps(world, 2);
        Assert.Equal(15, world.Engine.Variables[GameWorld.VarSentenceVerb]);

        var room = world.CurrentRoom!;
        var target = room.Objects.First(o => o.Name.Length > 0 && world.FindObject(o.X + o.Width / 2, o.Y + o.Height / 2) == o.Number);
        world.MoveMouse(target.X + target.Width / 2 - world.Camera.Left, target.Y + target.Height / 2 + 16);
        Steps(world, 3);

        Assert.Equal(target.Number, world.Engine.Variables[GameWorld.VarSentenceObject1]);
    }

    /// <summary>
    /// At the front door: Pull door mat shows the key under it, and Pick up key puts it in the
    /// inventory, where its name shows.
    /// </summary>
    [Fact]
    public void The_key_under_the_door_mat_can_be_picked_up()
    {
        if (SelectedKids(V2Folder) is not { } world)
        {
            return;
        }

        const int Pull = 10, PickUp = 14, DoorMat = 54, Key = 55;
        var dave = world.Actors[3];

        // out of the driveway by its exit, to the front of the house
        world.Click(4, 100);
        for (int step = 0; step < 1000 && (world.CurrentRoom?.Number != 1 || dave.Moving != 0); step++)
        {
            world.Step();
        }

        Assert.Equal(1, world.CurrentRoom?.Number);

        // walk left until the door mat is on screen
        var mat = world.CurrentRoom!.FindObject(DoorMat)!;
        for (int tries = 0; tries < 20 && (mat.X < world.Camera.Left + 8 || mat.X + mat.Width > world.Camera.Left + 312); tries++)
        {
            world.Click(10, 120);
            Steps(world, 40);
        }

        Use(world, Pull, DoorMat);
        Assert.NotEqual(0, world.Engine.GetState(DoorMat) & ScriptEngine.StateIntrinsic);
        var key = world.CurrentRoom!.FindObject(Key)!;
        Assert.Equal(Key, world.FindObject(key.X + 4, key.Y + 4));

        Use(world, PickUp, Key);
        Assert.Equal([Key], world.Inventory);
        Assert.Equal(1, world.InventoryCount(3));
        Assert.Equal("key", world.NameOf(Key));
        Assert.Contains((byte)13, world.Verbs.Pixels.Row(VerbArea.InventoryTop + 2).ToArray());
    }

    /// <summary>
    /// Into the house: the key unlocks the front door, which opens on the hall. There, pulling a
    /// gargoyle opens the door beside it for as long as the kid stands still (script 149).
    /// </summary>
    [Fact]
    public void The_key_opens_the_front_door_and_a_gargoyle_holds_a_door_open()
    {
        if (SelectedKids(V2Folder) is not { } world)
        {
            return;
        }

        const int Open = 1, Unlock = 8, Pull = 10, WalkTo = 13, PickUp = 14;
        const int DoorMat = 54, Key = 55, FrontDoor = 61, Gargoyle = 351, HiddenDoor = 69;
        var dave = world.Actors[3];
        world.Click(4, 100);
        for (int step = 0; step < 1000 && (world.CurrentRoom?.Number != 1 || dave.Moving != 0); step++)
        {
            world.Step();
        }

        var mat = world.CurrentRoom!.FindObject(DoorMat)!;
        for (int tries = 0; tries < 20 && (mat.X < world.Camera.Left + 8 || mat.X + mat.Width > world.Camera.Left + 312); tries++)
        {
            world.Click(10, 120);
            Steps(world, 40);
        }

        Use(world, Pull, DoorMat);
        Use(world, PickUp, Key);
        Use(world, Unlock, FrontDoor, Key);
        Assert.Equal(0, world.Engine.GetState(FrontDoor) & ScriptEngine.StateLocked);
        Use(world, Open, FrontDoor);
        Assert.NotEqual(0, world.Engine.GetState(FrontDoor) & ScriptEngine.StateIntrinsic);
        Use(world, WalkTo, FrontDoor);
        Assert.Equal(10, world.CurrentRoom?.Number);

        // along the hall until the gargoyle is on screen, then pull it
        world.Click(300, 120);
        Steps(world, 60);
        Use(world, Pull, Gargoyle);
        Assert.NotEqual(0, world.Engine.GetState(HiddenDoor) & ScriptEngine.StateIntrinsic);
        Steps(world, 100);
        Assert.NotEqual(0, world.Engine.GetState(HiddenDoor) & ScriptEngine.StateIntrinsic);

        // a step away and it shuts
        world.Click(100, 125);
        Steps(world, 30);
        Assert.Equal(0, world.Engine.GetState(HiddenDoor) & ScriptEngine.StateIntrinsic);
        Assert.Equal(10, world.CurrentRoom?.Number);
    }

    /// <summary>The engine on a host: the screen is presented and keys reach the game.</summary>
    [Fact]
    public void The_engine_runs_on_a_host()
    {
        if (GameFolders.Find(V2Folder) is not { } path)
        {
            return;
        }

        var container = new DirectoryGameContainer(path);
        var provider = new ScummEngineProvider();
        var host = new HeadlessHost();
        using var engine = provider.Create(host, container, provider.Detect(container)[0]);
        engine.Initialize();
        host.Run(engine, 10);

        Assert.Equal(10, host.Graphics.PresentCount);
        Assert.Equal((320, 200), (host.Graphics.Width, host.Graphics.Height));
        Assert.Contains("Maniac Mansion", host.Graphics.Title, StringComparison.Ordinal);

        host.Input.PushKey(Key.F1);
        host.Continue(engine, 1);
        Assert.Equal(1, ((ScummEngine)engine).World.Engine.Variables[GameWorld.VarKeypress]);
    }
}
