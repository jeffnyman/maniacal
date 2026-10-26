using Maniacal.Scumm.Game;
using Maniacal.Scumm.Graphics;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Scripts;
using Maniacal.Scumm.Storage;
using Maniacal.Scumm.View;

namespace Maniacal.Scumm.Tests;

public class FlashlightTests
{
    [Fact]
    public void The_light_centres_on_the_mouse_snapped_to_columns_and_even_lines()
    {
        // 6 strips by 4 rows: 48 by 32, centred on (103, 61) snapped to (96, 60), the camera 80 pixels in
        var area = Flashlight.Area(103 + 80, 61, 80, 6, 4, 320, 128);

        Assert.Equal(new Rect(96 - 24, 60 - 16, 48, 32), area);
    }

    [Fact]
    public void The_light_stays_inside_the_view()
    {
        Assert.Equal(new Rect(0, 0, 48, 32), Flashlight.Area(3, 2, 0, 6, 4, 320, 128));
        Assert.Equal(new Rect(320 - 48, 128 - 32, 48, 32), Flashlight.Area(319, 127, 0, 6, 4, 320, 128));
    }

    [Fact]
    public void Darkness_leaves_the_rounded_light_and_nothing_else()
    {
        var view = new Surface8(320, 128);
        view.Fill(5);
        var light = new Rect(100, 40, 48, 32);

        Flashlight.Darken(view, 2, light);

        Assert.Equal(0, view[10, 10]);
        Assert.Equal(0, view[99, 50]);
        Assert.Equal(5, view[124, 56]);

        // the corners are cut, the edges between them are not
        Assert.Equal(0, view[100, 40]);
        Assert.Equal(0, view[147, 40]);
        Assert.Equal(0, view[100, 71]);
        Assert.Equal(0, view[147, 71]);
        Assert.Equal(5, view[124, 40]);
        Assert.Equal(5, view[100, 56]);
    }

    [Fact]
    public void Without_a_light_the_room_is_black()
    {
        var view = new Surface8(320, 128);
        view.Fill(5);

        Flashlight.Darken(view, 2, null);

        Assert.DoesNotContain((byte)5, view.Pixels);
    }
}

/// <summary>What the scripts do to the objects of a room, and dark rooms.</summary>
public class ObjectsAndLightsTests
{
    private const string V1Folder = "maniac_mansion_original_DOS.com";
    private const string V2Folder = "maniac_mansion_enhanced_DOS.com";

    // in room 44, the driveway
    private const int Sign = 278;

    private static GameWorld? World()
        => GameFolders.Find(V2Folder) is { } path ? new GameWorld(ResourceLoader.Open(new DirectoryGameContainer(path))) : null;

    /// <summary>Runs hand-made code as a room script, ended by stopObjectCode.</summary>
    private static void Run(GameWorld world, params byte[] code) => world.Engine.RunRoomScript(ScriptEngine.EntryScriptNumber, (byte[])[.. code, 0x00]);

    [Fact]
    public void Drawing_an_object_at_a_place_moves_it_and_its_walk_to_point_until_the_room_changes()
    {
        if (World() is not { } world)
        {
            return;
        }

        world.StartScene(44);
        var sign = world.CurrentRoom!.FindObject(Sign)!;
        Assert.Equal(Sign, world.FindObject(sign.X + 1, sign.Y + 1));

        // drawObject(278, column 10, row 5)
        Run(world, 0x05, Sign & 0xFF, Sign >> 8, 10, 5);

        Assert.Equal((80, 40), world.PositionOf(sign));
        Assert.Equal(Sign, world.FindObject(81, 41));
        Assert.NotEqual(Sign, world.FindObject(sign.X + 1, sign.Y + 1));
        Assert.Equal(((sign.WalkX + 80 - sign.X) / 8, (sign.WalkY + 40 - sign.Y) / 2), world.ObjectPosition(Sign) is { } p ? (p.X, p.Y) : default);
        Assert.NotEqual(0, world.Engine.GetState(Sign) & ScriptEngine.StateIntrinsic);

        world.StartScene(1);
        world.StartScene(44);
        Assert.Equal((sign.X, sign.Y), world.PositionOf(sign));
    }

    [Fact]
    public void A_new_name_replaces_an_objects_own_name_in_every_room()
    {
        if (World() is not { } world)
        {
            return;
        }

        world.StartScene(44);
        Assert.Equal("sign", world.NameOf(Sign));

        // setObjectName(278, "plaque")
        Run(world, [0x54, Sign & 0xFF, Sign >> 8, .. "plaque"u8, 0]);

        Assert.Equal("plaque", world.NameOf(Sign));
        world.StartScene(1);
        world.StartScene(44);
        Assert.Equal("plaque", world.NameOf(Sign));
    }

    [Fact]
    public void The_closest_actor_in_the_range_is_found()
    {
        if (World() is not { } world)
        {
            return;
        }

        world.StartScene(44);
        foreach (var (actor, x) in new[] { (3, 10), (4, 30), (5, 14) })
        {
            world.Actors[actor].SetCostume(actor);
            world.Actors[actor].Put(x, 60, 44);
        }

        world.Engine.Variables[GameWorld.VarActorRangeMin] = 4;
        world.Engine.Variables[GameWorld.VarActorRangeMax] = 6;

        // Var[60] = getClosestObjActor(3)
        Run(world, 0x66, 60, 3, 0);

        Assert.Equal(5, world.Engine.Variables[60]);
    }

    /// <summary>
    /// Without the copy protection (the default, as in ScummVM) the steel security door is always
    /// open; with it, it is as the game leaves it, shut, and drawn so.
    /// </summary>
    [Fact]
    public void The_copy_protection_bypass_keeps_the_security_door_open()
    {
        if (World() is not { } bypassed || World() is not { } protectedWorld)
        {
            return;
        }

        const int Door = 182;
        protectedWorld.Engine.CopyProtection = true;
        Assert.Equal(0, protectedWorld.Engine.GetState(Door) & ScriptEngine.StateIntrinsic);
        Assert.NotEqual(0, bypassed.Engine.GetState(Door) & ScriptEngine.StateIntrinsic);
        Assert.NotEqual(0, bypassed.Engine.GetState(193) & ScriptEngine.StateIntrinsic);

        // in room 11 the open door draws its image, the shut one the room behind it
        bypassed.StartScene(11);
        protectedWorld.StartScene(11);
        var door = bypassed.CurrentRoom!.FindObject(Door)!;
        bypassed.Camera.SetAt(door.X + door.Width / 2);
        protectedWorld.Camera.SetAt(door.X + door.Width / 2);
        var open = bypassed.Screen();
        var shut = protectedWorld.Screen();
        int left = door.X - bypassed.Camera.Left;
        int differing = Enumerable.Range(door.Y + 16, door.Height)
            .Sum(y => Enumerable.Range(left, door.Width).Count(x => open[x, y] != shut[x, y]));
        Assert.True(differing > 100, $"{differing} pixels of the door differ");
    }

    /// <summary>
    /// Room 5 starts dark: on the way in, script 55 turns the lights off, or to the flashlight when its owner is
    /// here with it switched on. Dark, the room is black and the kid grey; the flashlight lights the
    /// room around the mouse and shows the kid in colour.
    /// </summary>
    [Theory]
    [InlineData(V2Folder, false, 0)]
    [InlineData(V2Folder, true, 12)]
    [InlineData(V1Folder, false, 0)]
    [InlineData(V1Folder, true, 4)]
    public void Dark_rooms_are_black_but_for_the_flashlight(string folder, bool flashlight, int lights)
    {
        // the start of the game marks which rooms are dark
        if (PlayingTests.SelectedKids(folder) is not { } world)
        {
            return;
        }

        const int Flashlight = 84, FlashlightSwitchedOn = 156;
        var dave = world.Actors[3];
        dave.SetCostume(3);
        dave.Put(30, 60, 5);
        world.Engine.ObjectOwners[Flashlight] = 3;
        world.Engine.Variables[FlashlightSwitchedOn] = flashlight ? 1 : 0;
        world.FollowActor(dave);
        world.MoveMouse(160, 90);
        world.Step();
        world.Step();

        Assert.Equal(lights, world.Engine.Variables[GameWorld.VarCurrentLights]);

        var screen = world.Screen();
        var light = View.Flashlight.Area(160 + world.Camera.Left, 90 - 16, world.Camera.Left, 6, 4, 320, 128);
        bool Lit(int x, int y) => screen[x, y + 16] != 0;

        // far from the kid and the light, the room is black
        Assert.False(Lit(4, 4));
        Assert.False(Lit(316, 120));

        var inside = Enumerable.Range(light.Top + 8, light.Height - 16)
            .SelectMany(y => Enumerable.Range(light.Left + 8, light.Width - 16).Select(x => (x, y)))
            .Count(p => Lit(p.x, p.y));
        if (flashlight)
        {
            Assert.True(inside > 100, $"{inside} lit pixels in the light");
        }
        else
        {
            Assert.DoesNotContain(Enumerable.Range(0, 128).SelectMany(y => Enumerable.Range(0, 320).Select(x => screen[x, y + 16])), c => c is not (0 or 8 or 7 or 15));
        }
    }
}
