using Maniacal.Scumm.Game;

namespace Maniacal.Scumm.Tests;

/// <summary>Playing the C64 release by clicking, as a player would.</summary>
public class C64PlayTests
{
    private const int Open = 1, WalkTo = 13;

    // the hall's doors and stairs, and the doors upstairs
    private const int HallKitchenDoor = 35, HallStairs = 209, LandingDoor = 149, HallwayDoor = 162;

    /// <summary>Gets a kid into the hall: the intro, the front door's key from under the mat, and in.</summary>
    internal static GameWorld? InTheHall()
    {
        if (GameWorldTests.AfterTheC64Intro() is not { } world)
        {
            return null;
        }

        world.Click(4, 100);
        Steps(world, 250);
        for (int i = 0; i < 5; i++)
        {
            world.Click(10, 120);
            Steps(world, 40);
        }

        const int Pull = 10, PickUp = 14, Unlock = 8, DoorMat = 21, Key = 22, FrontDoor = 28;
        Sentence(world, Pull, DoorMat, 80);
        Sentence(world, PickUp, Key, 80);
        Verb(world, Unlock);
        Steps(world, 3);
        ClickObject(world, FrontDoor);
        Steps(world, 3);
        world.Click(10, 182);
        Steps(world, 3);
        world.Click(10, 182);
        Steps(world, 100);
        Sentence(world, WalkTo, FrontDoor, 60);
        return world;
    }

    /// <summary>
    /// Up the hall's stairs to the landing (room 11), through its door to the upstairs hallway (13),
    /// and through one of its doors to the office (22); every instruction on the way handled.
    /// </summary>
    [Fact]
    public void The_kid_goes_upstairs()
    {
        if (InTheHall() is not { } world)
        {
            return;
        }

        Assert.Equal(10, world.CurrentRoom?.Number);
        Sentence(world, WalkTo, HallStairs, 200);
        Assert.Equal(11, world.CurrentRoom?.Number);
        Sentence(world, Open, LandingDoor, 60);
        Sentence(world, WalkTo, LandingDoor, 200);
        Assert.Equal(13, world.CurrentRoom?.Number);
        Sentence(world, Open, HallwayDoor, 60);
        Sentence(world, WalkTo, HallwayDoor, 200);
        Assert.Equal(22, world.CurrentRoom?.Number);
        Assert.Equal(GameWorld.ModeNormal, world.CurrentMode);
        Assert.Empty(world.Unhandled);
    }

    /// <summary>
    /// In the kitchen (room 7) Nurse Edna catches the kid going for the far door, and a cutscene
    /// takes him to the dungeon (room 4): "I got you!", then play goes on there.
    /// </summary>
    [Fact]
    public void Edna_catches_the_kid_in_the_kitchen()
    {
        if (InTheHall() is not { } world)
        {
            return;
        }

        Sentence(world, Open, HallKitchenDoor, 60);
        Sentence(world, WalkTo, HallKitchenDoor, 200);
        Assert.Equal(7, world.CurrentRoom?.Number);

        var dave = world.Actors[3];
        bool caught = false;
        for (int round = 0; round < 4; round++)
        {
            world.Click(315, dave.Y * 2 + 9 - 2);
            for (int i = 0; i < 30; i++)
            {
                world.Step();
                caught |= world.Messages.Plain().Contains("I got you!", StringComparison.Ordinal);
            }
        }

        for (int i = 0; i < 3000 && !(world.CurrentRoom?.Number == 4 && world.CurrentMode == GameWorld.ModeNormal); i++)
        {
            world.Step();
            caught |= world.Messages.Plain().Contains("I got you!", StringComparison.Ordinal);
        }

        Assert.True(caught);
        Assert.Equal(4, world.CurrentRoom?.Number);
        Assert.Equal(4, dave.Room);
        Assert.Equal(GameWorld.ModeNormal, world.CurrentMode);
        Assert.Empty(world.Unhandled);
    }

    /// <summary>
    /// The kid put into each room with walkboxes, as the scripts put actors (in its first box, the
    /// camera following him there), from the hall: every room's entry plays on for 60 steps without
    /// an unhandled instruction, in its own room. The same rooms, saved so and loaded in ScummVM
    /// 2.7.0, show the same screens.
    /// </summary>
    [Fact]
    public void The_kid_can_be_put_into_every_room()
    {
        if (InTheHall() is not { } hall || GameFolders.Find("maniac_mansion_c64") is not { } path)
        {
            return;
        }

        var save = hall.Save("hall", DateTime.Now);
        var loader = Resources.ResourceLoader.Open(new Maniacal.Scumm.Storage.DirectoryGameContainer(path));
        var entered = new List<int>();
        for (int room = 1; room < 55; room++)
        {
            // the kid's own room, the title and the save screen are left out
            if (room is 10 or 45 or 50 || !loader.Index.Locate(Resources.ResourceType.Room, room).IsPresent
                || loader.LoadRoom(room) is not { WalkBoxes.Count: > 0 } target)
            {
                continue;
            }

            var world = new GameWorld(Resources.ResourceLoader.Open(new Maniacal.Scumm.Storage.DirectoryGameContainer(path)));
            world.Load(save);
            world.Step();
            var box = target.WalkBoxes.Boxes[0];
            var dave = world.Actors[3];
            dave.Put((box.LowerLeftX + box.LowerRightX) / 2, box.LowerY, room);
            world.FollowActor(dave);
            Steps(world, 60);

            Assert.True(world.CurrentRoom?.Number == room, $"room {room}: in room {world.CurrentRoom?.Number}");
            Assert.True(world.Unhandled.Count == 0, $"room {room}: {string.Join(", ", world.Unhandled.Distinct())}");
            entered.Add(room);
        }

        Assert.Equal(43, entered.Count);
    }

    internal static void Steps(GameWorld world, int count)
    {
        for (int i = 0; i < count; i++)
        {
            world.Step();
        }
    }

    internal static void Verb(GameWorld world, int id)
    {
        var verb = world.Verbs.Slots[world.Verbs.SlotOf(id)];
        world.Click(verb.X + 4, verb.Y + 4);
    }

    /// <summary>
    /// Clicks an object of the room at its middle, walking the kid along his line first while it is
    /// off screen; only Walk to walks to a spot on the floor, so the chosen verb is put back after.
    /// </summary>
    internal static bool ClickObject(GameWorld world, int obj)
    {
        for (int tries = 0; tries < 15; tries++)
        {
            var o = world.CurrentRoom?.FindObject(obj);
            if (o is null)
            {
                return false;
            }

            int x = o.X + o.Width / 2 - world.Camera.Left;
            if (x is >= 4 and < 316)
            {
                world.Click(x, Math.Min(o.Y + o.Height / 2, 130) + 9);
                return true;
            }

            int verb = world.ActiveVerb;
            var kid = world.Actors[world.Engine.Variables[0]];
            Verb(world, WalkTo);
            Steps(world, 2);
            world.Click(x < 0 ? 4 : 315, kid.Y * 2 + 9 - 2);
            Steps(world, 30);
            if (verb != WalkTo)
            {
                Verb(world, verb);
                Steps(world, 2);
            }
        }

        return false;
    }

    /// <summary>A verb and an object, the object clicked twice to carry the sentence out (once for Walk to).</summary>
    internal static void Sentence(GameWorld world, int verb, int obj, int steps)
    {
        Verb(world, verb);
        Steps(world, 3);
        Assert.True(ClickObject(world, obj), $"object {obj} could not be clicked");
        Steps(world, 3);
        if (verb != WalkTo)
        {
            ClickObject(world, obj);
        }

        Steps(world, steps);
    }
}
