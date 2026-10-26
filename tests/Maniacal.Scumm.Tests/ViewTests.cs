using Maniacal.Scumm.Costumes;
using Maniacal.Scumm.Graphics;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Storage;
using Maniacal.Scumm.View;

namespace Maniacal.Scumm.Tests;

public class CameraTests
{
    private static Camera InRoom(int width)
    {
        var camera = new Camera();
        camera.SetRoom(width);
        return camera;
    }

    [Fact]
    public void The_view_stays_inside_the_room()
    {
        var camera = InRoom(960);

        camera.SetAtEx(0);
        camera.Move();
        Assert.Equal((160, 0), (camera.X, camera.Left));

        camera.SetAtEx(2000);
        camera.Move();
        Assert.Equal((800, 640), (camera.X, camera.Left));
    }

    [Fact]
    public void The_view_starts_on_a_strip()
    {
        var camera = InRoom(960);

        camera.SetAtEx(403);
        camera.Move();

        Assert.Equal(400, camera.X);
        Assert.Equal(30, camera.StartStrip);
        Assert.Equal(69, camera.EndStrip);
        Assert.Equal(240, camera.Left);
    }

    [Fact]
    public void Panning_moves_8_pixels_a_step()
    {
        var camera = InRoom(960);
        camera.SetAtEx(160);

        camera.PanTo(480);
        camera.Move();
        Assert.Equal(168, camera.X);

        for (int i = 0; i < 50; i++)
        {
            camera.Move();
        }

        Assert.Equal(480, camera.X);
        Assert.Equal(CameraMode.Panning, camera.Mode);
    }

    [Fact]
    public void A_followed_actor_moves_the_camera_once_past_a_trigger()
    {
        var camera = InRoom(1280);
        camera.SetAtEx(160);
        camera.Move();
        int actorX = 200;

        // column 25 of the view: between the triggers, so the camera stays
        camera.Follow(3, actorX);
        camera.Move(() => actorX);
        Assert.Equal(160, camera.X);

        // column 35: past the right trigger, so the camera scrolls 8 pixels a step until centred on the actor
        actorX = 280;
        camera.Move(() => actorX);
        Assert.True(camera.MovingToActor);
        Assert.Equal(168, camera.X);

        for (int i = 0; i < 20; i++)
        {
            camera.Move(() => actorX);
        }

        Assert.Equal(280, camera.X);
        Assert.False(camera.MovingToActor);
    }

    [Fact]
    public void A_far_actor_makes_the_camera_jump()
    {
        var camera = InRoom(1280);
        camera.SetAtEx(160);
        camera.Move();

        camera.Follow(3, 1000);
        camera.Move(() => 1000);

        Assert.Equal(1000, camera.X);
    }

    [Fact]
    public void Script_limits_pull_the_camera_back_8_pixels_a_step()
    {
        var camera = InRoom(1280);
        camera.SetAtEx(600);
        camera.Move();

        camera.MaxX = 400;
        camera.Move();

        Assert.Equal(592, camera.X);
    }
}

public class RoomViewTests
{
    private const string V1Folder = "maniac_mansion_original_DOS.com";
    private const string V2Folder = "maniac_mansion_enhanced_DOS.com";

    /// <summary>A one-pixel figure of colour 5 whose point is the pixel itself.</summary>
    private static CostumeFrame Dot()
    {
        var pixels = new Surface8(1, 1);
        pixels[0, 0] = 5;
        return new CostumeFrame(pixels, 0, 0);
    }

    private static MaskedImage Blank() => new(new Surface8(320, 136), new Surface8(320, 136));

    private static Camera AtLeftEdge()
    {
        var camera = new Camera();
        camera.SetRoom(320);
        return camera;
    }

    [Theory]
    [InlineData(2, false, 88, 40)]
    [InlineData(2, true, 96, 40)]
    [InlineData(1, false, 80, 41)]
    [InlineData(1, true, 96, 41)]
    public void Actors_are_placed_as_prepareDrawActorCostume_does(int version, bool facingLeft, int x, int y)
    {
        var view = Blank();

        RoomView.DrawActor(view, version, AtLeftEdge(), new ActorDrawing(Dot(), 10, 20, facingLeft, BehindMask: false));

        Assert.Equal(5, view.Pixels[x, y]);
        Assert.Equal(1, view.Pixels.Pixels.Count(p => p != 0));
    }

    [Fact]
    public void The_mask_covers_actors_only_when_they_are_behind_it()
    {
        var view = Blank();
        view.Mask[88, 40] = 1;

        RoomView.DrawActor(view, 2, AtLeftEdge(), new ActorDrawing(Dot(), 10, 20, false, BehindMask: true));
        Assert.Equal(0, view.Pixels[88, 40]);

        RoomView.DrawActor(view, 2, AtLeftEdge(), new ActorDrawing(Dot(), 10, 20, false, BehindMask: false));
        Assert.Equal(5, view.Pixels[88, 40]);
    }

    [Theory]
    [InlineData(1, 16, 152)]
    [InlineData(2, 16, 144)]
    public void The_room_sits_between_the_message_lines_and_the_verbs(int version, int top, int bottom)
    {
        var layout = ScreenLayout.ForVersion(version);
        var view = new MaskedImage(new Surface8(320, layout.RoomHeight), new Surface8(320, layout.RoomHeight));
        view.Pixels.Fill(9);

        var screen = RoomView.Screen(view, layout);

        Assert.Equal((top, bottom), (layout.RoomTop, layout.RoomBottom));
        Assert.Equal(0, screen[0, top - 1]);
        Assert.Equal(9, screen[0, top]);
        Assert.Equal(9, screen[319, bottom - 1]);
        Assert.Equal(0, screen[0, bottom]);
    }

    /// <summary>
    /// Dave behind the iron fence at the right of the front yard: the walkbox there has a mask, so
    /// the fence bars are drawn over him and fewer of his pixels show than on open ground.
    /// </summary>
    [Theory]
    [InlineData(V1Folder, 1120, 142, 60)]
    [InlineData(V2Folder, 800, 112, 57)]
    public void The_fence_covers_a_kid_behind_it(string folder, int cameraX, int x, int y)
    {
        if (GameFolders.Find(folder) is not { } path)
        {
            return;
        }

        var loader = ResourceLoader.Open(new DirectoryGameContainer(path));
        var room = loader.LoadRoom(1)!;
        var camera = new Camera();
        camera.SetRoom(room.Width);
        camera.SetAtEx(cameraX);
        camera.Move();

        int box = room.WalkBoxes.FindBox(x, y);
        Assert.NotEqual(-1, box);
        Assert.NotEqual(0, room.WalkBoxes.Boxes[box].Mask);

        var frame = CostumeRenderer.DrawFrame(loader.LoadCostume(3)!, CostumeRenderer.Front, [CostumeRenderer.InitFrame, CostumeRenderer.StandFrame], 3);
        int Drawn(bool behind)
        {
            var background = RoomView.Window(RoomRenderer.DrawBackground(room), camera);
            var view = RoomView.Window(RoomRenderer.DrawBackground(room), camera);
            RoomView.DrawActor(view, room.Version, camera, new ActorDrawing(frame, x, y, false, behind));
            return view.Pixels.Pixels.Zip(background.Pixels.Pixels).Count(p => p.First != p.Second);
        }

        Assert.True(Drawn(behind: true) < Drawn(behind: false));
    }
}
