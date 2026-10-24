using Daventry.Core.Storage;
using Maniacal.Scumm.Game;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Scripts;

namespace Maniacal.Scumm.Tests;

public class GameWorldTests
{
    private const string V1Folder = "maniac_mansion_original_DOS.com";
    private const string V2Folder = "maniac_mansion_enhanced_DOS.com";
    private const string C64Folder = "maniac_mansion_c64";

    private static GameWorld? Open(string folder)
        => GameFolders.Find(folder) is { } path ? new GameWorld(ResourceLoader.Open(new DirectoryGameContainer(path))) : null;

    /// <summary>
    /// The boot script loads room 45, the title screen; its entry script turns the lights to 11.
    /// The room is the screen's width, so the camera's limits are both 160.
    /// </summary>
    [Theory]
    [InlineData(V1Folder)]
    [InlineData(V2Folder)]
    public void Booting_shows_the_title_room(string folder)
    {
        if (Open(folder) is not { } world)
        {
            return;
        }

        world.Boot();
        for (int i = 0; i < 60; i++)
        {
            world.Step();
        }

        Assert.Equal(45, world.CurrentRoom?.Number);
        Assert.Equal(45, world.Engine.Variables[ScriptEngine.VarRoom]);
        Assert.Equal(11, world.Engine.Variables[GameWorld.VarCurrentLights]);
        Assert.Equal((160, 160), (world.Engine.Variables[GameWorld.VarCameraMinX], world.Engine.Variables[GameWorld.VarCameraMaxX]));
        Assert.Equal(160, world.Camera.X);
    }

    /// <summary>
    /// The C64 boots to the same title room: its boot script sets the keypad mode for the kid
    /// selection and prints the copyright on the message line. Version 0 keeps the lights in the
    /// engine, not a variable, and every instruction on the way is one it handles.
    /// </summary>
    [Fact]
    public void The_c64_boots_to_the_kid_selection()
    {
        if (Open(C64Folder) is not { } world)
        {
            return;
        }

        world.Boot();
        for (int i = 0; i < 60; i++)
        {
            world.Step();
        }

        Assert.Equal(0, world.Version);
        Assert.Equal(45, world.CurrentRoom?.Number);
        Assert.Equal(45, world.Engine.Variables[ScriptEngine.VarRoom]);
        Assert.Equal(11, world.CurrentLights);
        Assert.Equal(GameWorld.ModeKeypad, world.CurrentMode);
        Assert.True(world.CursorShown);
        Assert.Contains("Lucasfilm Ltd. All rights reserved", world.Messages.Plain());
        Assert.Empty(world.Unhandled);

        // the C64 has no variables for the mouse in the room: 30 and 31 are the scripts' own
        Assert.Equal((0, 0), (world.Engine.Variables[30], world.Engine.Variables[31]));
        // 4 ticks a step, of 4 quarter-frames at 240 a second: 60 steps are 4 seconds, and 4 ticks
        // more for the C64's drawing of the title room (3.6, rounded; DelayCalculateDelta)
        Assert.Equal((60 * 4 + 4) * 4 / 240.0, world.Time.TotalSeconds, 3);
    }

    /// <summary>
    /// The C64's kid selection, by clicking: the keypad mode makes each click a Push, which the
    /// portraits answer with their What is script, version 0's catch-all. A kid's portrait tells
    /// who the kid is and picks them; START, with two picked, starts the intro.
    /// </summary>
    [Fact]
    public void The_c64_kids_are_picked_by_clicking()
    {
        if (Open(C64Folder) is not { } world)
        {
            return;
        }

        const int VarPicked = 90, VarStarted = 91, VarKidCount = 92, VarKids = 97, Intro = 115;
        void Steps(int count)
        {
            for (int i = 0; i < count; i++)
            {
                world.Step();
            }
        }

        world.Boot();
        Steps(100);

        // Syd's portrait, at (56, 88) in the room, which starts 9 lines down the screen
        world.Click(76, 121);
        Steps(10);
        Assert.Equal(1, world.Engine.Variables[VarPicked]);
        Assert.StartsWith("Syd - an aspiring musician", world.Messages.Plain());

        // Razor's, then START
        world.Click(236, 121);
        Steps(50);
        world.Click(290, 69);
        Steps(20);

        Assert.Equal(1, world.Engine.Variables[VarStarted]);
        Assert.Equal(3, world.Engine.Variables[VarKidCount]);
        Assert.Equal([3, 1, 2], world.Engine.Variables[VarKids..(VarKids + 3)]);
        Assert.True(world.Engine.IsScriptRunning(Intro));
        Assert.Empty(world.Unhandled);
    }

    /// <summary>Boots the C64, picks Syd and Razor with Dave, and plays the intro until the kids stand in front of the mansion.</summary>
    internal static GameWorld? AfterTheC64Intro()
    {
        if (Open(C64Folder) is not { } world)
        {
            return null;
        }

        world.Boot();
        for (int i = 0; i < IntroSteps; i++)
        {
            switch (i)
            {
                case 100:
                    world.Click(76, 121);
                    break;
                case 150:
                    world.Click(236, 121);
                    break;
                case 200:
                    world.Click(290, 69);
                    break;
            }

            world.Step();
        }

        return world;
    }

    private const int IntroSteps = 2200;

    /// <summary>
    /// The C64's intro plays: the kids' costumes animate, they walk to the mansion's drive, and
    /// the game starts with Dave, the verbs showing. Every actor stands (version 0's 2). Pinned
    /// after checking the screen by eye.
    /// </summary>
    [Fact]
    public void The_c64_intro_plays()
    {
        if (AfterTheC64Intro() is not { } world)
        {
            return;
        }

        Assert.Equal(44, world.CurrentRoom?.Number);
        Assert.Equal(GameWorld.ModeNormal, world.CurrentMode);
        Assert.Equal(3, world.Engine.Variables[ScriptEngine.VarEgo]);
        Assert.Equal([1, 2, 3], world.Actors.Where(a => a.InCurrentRoom && a.Visible).Select(a => a.Number));
        Assert.All(world.Actors.Where(a => a.InCurrentRoom), a => Assert.Equal(ActorV0.Standing, a.Moving));
        Assert.Equal(["Syd", "Razor", "Dave"], world.Actors[1..4].Select(a => a.Name));
        Assert.Empty(world.Unhandled);
        Assert.Equal("6de357bb82197929d5f4a21348470826", Md5.ComputeHex(world.Screen().Pixels));
    }

    /// <summary>
    /// After the intro, Dave walks where the room is clicked, a column a step, and stands facing
    /// the way he went; Read and the sign walk him to it, turn him to it, and its script shows its text.
    /// </summary>
    [Fact]
    public void The_c64_kid_walks_and_reads()
    {
        if (AfterTheC64Intro() is not { } world)
        {
            return;
        }

        var dave = world.Actors[3];
        void Steps(int count)
        {
            for (int i = 0; i < count; i++)
            {
                world.Step();
            }
        }

        // (60, 130) on the screen is column 7, line 60 of the room, which the walkboxes move to line 64
        world.Click(60, 130);
        Steps(5);
        Assert.NotEqual(ActorV0.Standing, dave.Moving);
        Steps(45);
        Assert.Equal((7, 64, 270, ActorV0.Standing), (dave.X, dave.Y, dave.Facing, dave.Moving));

        // Read, then the sign twice: the second click carries the sentence out
        world.Click(70, 170);
        Steps(5);
        world.Click(24, 100);
        Steps(5);
        world.Click(24, 100);
        Steps(40);
        Assert.Equal((3, 64, 0), (dave.X, dave.Y, dave.Facing));
        Assert.StartsWith("WARNING!!", world.Messages.Plain());
        Assert.Empty(world.Unhandled);
    }

    /// <summary>
    /// A C64 step lasts 4 ticks and as many more as the C64 took to draw it (<c>DelayCalculateDelta</c>):
    /// with nothing to redraw, 4; while the camera scrolls with a walking kid, 4 more for the room
    /// (3.6) and his limbs (0.3), rounded.
    /// </summary>
    [Fact]
    public void C64_steps_take_longer_to_draw_a_scrolling_room()
    {
        if (AfterTheC64Intro() is not { } world)
        {
            return;
        }

        void Steps(int count)
        {
            for (int i = 0; i < count; i++)
            {
                world.Step();
            }
        }

        // the kids stand still in the street
        Steps(30);
        Assert.Equal(4, world.StepTicks);

        // into room 1, then left along the drive with the camera following
        world.Click(4, 100);
        Steps(250);
        world.Click(10, 120);
        var ticks = new List<int>();
        for (int i = 0; i < 30; i++)
        {
            int cameraX = world.Camera.X;
            world.Step();
            if (world.Camera.X != cameraX)
            {
                ticks.Add(world.StepTicks);
            }
        }

        Assert.NotEmpty(ticks);
        Assert.All(ticks, t => Assert.Equal(8, t));
    }

    /// <summary>
    /// After the intro, Dave goes into the mansion: off the street's left edge to the drive, along
    /// it to the porch, Pull the door mat (the key shows), Pick up the key (Dave owns it and the
    /// inventory names it), Unlock the front door with the key, which opens it, and Walk to the
    /// door, which takes him into the hall, room 10.
    /// </summary>
    [Fact]
    public void The_c64_kid_goes_into_the_house()
    {
        if (AfterTheC64Intro() is not { } world)
        {
            return;
        }

        const int DoorMat = 21, Key = 22, FrontDoor = 28, Dave = 3;
        void Steps(int count)
        {
            for (int i = 0; i < count; i++)
            {
                world.Step();
            }
        }

        // a verb at its column and row, and an object of the room at its middle, twice to carry the sentence out
        void Sentence(int column, int row, int obj, int steps)
        {
            world.Click(column * 8 + 4, 145 + 8 + row * 8 + 4);
            Steps(3);
            var o = world.CurrentRoom!.FindObject(obj)!;
            for (int i = 0; i < 2; i++)
            {
                world.Click(o.X + o.Width / 2 - world.Camera.Left, o.Y + o.Height / 2 + 9);
                Steps(3);
            }

            Steps(steps);
        }

        world.Click(4, 100);
        Steps(250);
        Assert.Equal(1, world.CurrentRoom?.Number);
        for (int i = 0; i < 5; i++)
        {
            world.Click(10, 120);
            Steps(40);
        }

        Sentence(0, 1, DoorMat, 80);
        Assert.Equal(8, world.Engine.GetState(DoorMat));
        Sentence(15, 1, Key, 80);
        Assert.Equal(Dave, world.Engine.ObjectOwners[Key]);
        var verbs = world.Verbs.Pixels;
        Assert.Contains(Enumerable.Range(VerbArea.InventoryTop, verbs.Height - VerbArea.InventoryTop), y => verbs.Row(y).Contains((byte)16));

        // Unlock, the door, then the key in the inventory's first box
        world.Click(24 * 8 + 4, 145 + 16 + 4);
        Steps(3);
        var door = world.CurrentRoom!.FindObject(FrontDoor)!;
        world.Click(door.X + door.Width / 2 - world.Camera.Left, door.Y + door.Height / 2 + 9);
        Steps(3);
        world.Click(10, 182);
        Steps(3);
        world.Click(10, 182);
        Steps(100);
        Assert.Equal(8, world.Engine.GetState(FrontDoor));

        Sentence(15, 0, FrontDoor, 60);
        Assert.Equal(10, world.CurrentRoom?.Number);
        Assert.True(world.Actors[Dave].InCurrentRoom);
        Assert.Empty(world.Unhandled);
    }

    /// <summary>
    /// The C64's engine makes its 15 verbs itself, at columns and rows under the sentence line, in
    /// its colours; Give takes "to", Unlock and Fix "with", and Use the preposition of its object.
    /// </summary>
    [Fact]
    public void The_c64_makes_its_own_verbs()
    {
        if (Open(C64Folder) is not { } world)
        {
            return;
        }

        const int Give = 3, Fix = 6, NewKid = 7, Unlock = 8, Push = 9, Use = 11, WalkTo = 13;
        var verbs = world.Verbs;

        Assert.Equal(15, verbs.Slots.Count(v => v.Id != 0));
        var newKid = verbs.Slots[verbs.SlotOf(NewKid)];
        Assert.Equal("New Kid", System.Text.Encoding.Latin1.GetString(newKid.Name));
        Assert.Equal((24 * 8, 145 + 8), (newKid.X, newKid.Y));
        Assert.Equal((5, 7, 11), (newKid.Color, newKid.HiColor, newKid.DimColor));
        Assert.Equal((15 * 8, 145 + 8), (verbs.Slots[verbs.SlotOf(WalkTo)].X, verbs.Slots[verbs.SlotOf(WalkTo)].Y));
        Assert.Equal(4, verbs.Slots[verbs.SlotOf(Give)].Preposition);
        Assert.Equal((2, 2), (verbs.Slots[verbs.SlotOf(Unlock)].Preposition, verbs.Slots[verbs.SlotOf(Fix)].Preposition));
        Assert.Equal(0xFF, verbs.Slots[verbs.SlotOf(Use)].Preposition);
        Assert.Equal(0, verbs.Slots[verbs.SlotOf(Push)].Preposition);
        Assert.Equal(13, world.ActiveVerb);
    }

    /// <summary>Pinned after checking the screens by eye: the title, START and the seven kids, after 60 steps with seed 1.</summary>
    [Theory]
    [InlineData(V1Folder, "09c1f7ddfc8ee22b22a3126cebd04151")]
    [InlineData(V2Folder, "94c5b94df4c443508a7ad9c8ad5bacbd")]
    [InlineData(C64Folder, "f837caf9ecdc9ae403db05f9df376c2d")]
    public void The_title_screen_is_stable(string folder, string md5)
    {
        if (Open(folder) is not { } world)
        {
            return;
        }

        world.Boot();
        for (int i = 0; i < 60; i++)
        {
            world.Step();
        }

        var screen = world.Screen();
        Assert.Contains(screen.Pixels, pixel => pixel != 0);
        Assert.Equal(md5, Md5.ComputeHex(screen.Pixels));
    }

    [Theory]
    [InlineData(V1Folder)]
    [InlineData(V2Folder)]
    public void Changing_room_kills_its_scripts_and_clamps_the_camera(string folder)
    {
        if (Open(folder) is not { } world)
        {
            return;
        }

        world.StartScene(1);
        world.Camera.SetAtEx(2000);
        world.Step();
        Assert.Equal(world.CurrentRoom!.Width - 160, world.Camera.X);

        // an object script of room 1 is running when the room changes
        var door = world.CurrentRoom.FindObject(61)!;
        world.Engine.RunObjectScript(61, door.Code, door.FindVerb(1)!.Value);
        world.StartScene(45);

        Assert.DoesNotContain(world.Engine.Slots, s => s.Kind == ScriptKind.Room && !s.IsDead);
        Assert.Equal(45, world.Engine.Variables[ScriptEngine.VarRoom]);
        Assert.Equal(160, world.Camera.X);
    }

    [Theory]
    [InlineData(V1Folder)]
    [InlineData(V2Folder)]
    public void Room_0_is_no_room(string folder)
    {
        if (Open(folder) is not { } world)
        {
            return;
        }

        world.StartScene(45);
        world.StartScene(0);

        Assert.Null(world.CurrentRoom);
        Assert.All(world.Screen().Pixels, pixel => Assert.Equal(0, pixel));
    }
}
