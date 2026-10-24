using Daventry.Core.Storage;
using Maniacal.Scumm.Costumes;
using Maniacal.Scumm.Game;
using Maniacal.Scumm.Resources;

namespace Maniacal.Scumm.Tests;

public class ActorTests
{
    private const string V1Folder = "maniac_mansion_original_DOS.com";
    private const string V2Folder = "maniac_mansion_enhanced_DOS.com";

    private const int Dave = 3;

    private static GameWorld? Open(string folder)
        => GameFolders.Find(folder) is { } path ? new GameWorld(ResourceLoader.Open(new DirectoryGameContainer(path))) : null;

    /// <summary>Dave in room 1 with his own costume.</summary>
    private static Actor? DaveInRoom1(GameWorld world)
    {
        world.StartScene(1);
        var dave = world.Actors[Dave];
        dave.SetCostume(Dave);
        dave.Put(40, 60, 1);
        return dave;
    }

    [Fact]
    public void Directions_convert_both_ways()
    {
        Assert.Equal([270, 90, 180, 0], Enumerable.Range(0, 4).Select(Actor.OldDirToNewDir));
        Assert.Equal([3, 1, 2, 0], new[] { 0, 90, 180, 270 }.Select(Actor.NewDirToOldDir));
    }

    [Theory]
    [InlineData(V1Folder)]
    [InlineData(V2Folder)]
    public void An_actor_shows_in_the_current_room_and_hides_outside_it(string folder)
    {
        if (Open(folder) is not { } world || DaveInRoom1(world) is not { } dave)
        {
            return;
        }

        Assert.True(dave.Visible);

        dave.Put(40, 60, 2);
        Assert.False(dave.Visible);

        dave.Put(40, 60, 1);
        Assert.True(dave.Visible);

        world.StartScene(45);
        Assert.False(dave.Visible);
    }

    /// <summary>Showing an actor starts the stand, init and talk-stop frames, which is what the figure then shows.</summary>
    [Theory]
    [InlineData(V1Folder)]
    [InlineData(V2Folder)]
    public void Showing_starts_stand_init_and_talk_stop(string folder)
    {
        if (Open(folder) is not { } world || DaveInRoom1(world) is not { } dave)
        {
            return;
        }

        var costume = dave.Costume!;
        var palette = CostumeRenderer.Palette(costume, Dave, dave.Palette);
        var shown = CostumeRenderer.DrawLimbs(costume, dave.State, dave.OldDirection, palette);
        var expected = CostumeRenderer.DrawFrame(costume, dave.OldDirection, [CostumeRenderer.StandFrame, CostumeRenderer.InitFrame, CostumeRenderer.TalkStopFrame], Dave);

        Assert.Equal(CostumeRenderer.TalkStopFrame, dave.Frame);
        Assert.Equal(expected.Pixels.Pixels, shown.Pixels.Pixels);
    }

    /// <summary>Turning starts the limbs' animations again for the new direction: facing left looks like facing right, mirrored.</summary>
    [Theory]
    [InlineData(V1Folder)]
    [InlineData(V2Folder)]
    public void Turning_redraws_the_limbs_for_the_new_direction(string folder)
    {
        if (Open(folder) is not { } world || DaveInRoom1(world) is not { } dave)
        {
            return;
        }

        var costume = dave.Costume!;
        var palette = CostumeRenderer.Palette(costume, Dave, dave.Palette);

        dave.SetDirection(90);
        var right = CostumeRenderer.DrawLimbs(costume, dave.State, dave.OldDirection, palette).Pixels;
        dave.SetDirection(270);
        var left = CostumeRenderer.DrawLimbs(costume, dave.State, dave.OldDirection, palette).Pixels;

        Assert.Equal(CostumeRenderer.Left, dave.OldDirection);
        Assert.Equal(right.Width, left.Width);
        for (int y = 0; y < left.Height; y++)
        {
            for (int x = 0; x < left.Width; x++)
            {
                Assert.Equal(right[right.Width - 1 - x, y], left[x, y]);
            }
        }
    }

    /// <summary>animateActor's special frames: 0x3E faces a direction at once; 0x3F goes back to standing.</summary>
    [Theory]
    [InlineData(V1Folder)]
    [InlineData(V2Folder)]
    public void Special_animations_face_and_stand(string folder)
    {
        if (Open(folder) is not { } world || DaveInRoom1(world) is not { } dave)
        {
            return;
        }

        dave.Animate((0x3E << 2) | CostumeRenderer.Back);
        Assert.Equal(0, dave.Facing);

        dave.Animate((0x3E << 2) | CostumeRenderer.Right);
        Assert.Equal(90, dave.Facing);

        dave.Animate(CostumeRenderer.WalkFrame << 2);
        Assert.Equal(CostumeRenderer.WalkFrame, dave.Frame);

        dave.Animate(0x3F << 2);
        Assert.Equal(CostumeRenderer.StandFrame, dave.Frame);
    }

    /// <summary>A walk cycle loops: after as many steps as it has pictures, the figure is back to its first.</summary>
    [Theory]
    [InlineData(V1Folder)]
    [InlineData(V2Folder)]
    public void The_walk_cycle_loops(string folder)
    {
        if (Open(folder) is not { } world || DaveInRoom1(world) is not { } dave)
        {
            return;
        }

        var costume = dave.Costume!;
        var palette = CostumeRenderer.Palette(costume, Dave, dave.Palette);
        dave.SetDirection(90);
        dave.StartAnim(CostumeRenderer.WalkFrame);
        int length = costume.Animation(CostumeRenderer.WalkFrame * 4 + CostumeRenderer.Right).Where(l => l.Start is not null).Max(l => l.Length + 1);

        var first = CostumeRenderer.DrawLimbs(costume, dave.State, dave.OldDirection, palette).Pixels.Pixels;
        dave.AnimateCostume();
        var second = CostumeRenderer.DrawLimbs(costume, dave.State, dave.OldDirection, palette).Pixels.Pixels;
        for (int i = 1; i < length; i++)
        {
            dave.AnimateCostume();
        }

        var again = CostumeRenderer.DrawLimbs(costume, dave.State, dave.OldDirection, palette).Pixels.Pixels;

        Assert.NotEqual(first, second);
        Assert.Equal(first, again);
    }

    [Theory]
    [InlineData(V1Folder)]
    [InlineData(V2Folder)]
    public void Anim_speed_steps_every_so_many_frames(string folder)
    {
        if (Open(folder) is not { } world || DaveInRoom1(world) is not { } dave)
        {
            return;
        }

        dave.SetDirection(90);
        dave.StartAnim(CostumeRenderer.WalkFrame);
        dave.AnimSpeed = 3;
        var costume = dave.Costume!;
        var palette = CostumeRenderer.Palette(costume, Dave, dave.Palette);
        byte[] Picture() => CostumeRenderer.DrawLimbs(costume, dave.State, dave.OldDirection, palette).Pixels.Pixels;

        var first = Picture();
        dave.AnimateCostume();
        dave.AnimateCostume();
        Assert.Equal(first, Picture());

        dave.AnimateCostume();
        Assert.NotEqual(first, Picture());
    }

    /// <summary>Version 1 actors start in the costume of their own number, with their own talk colours (<c>resetScumm</c>).</summary>
    [Fact]
    public void Version_1_actors_start_in_their_own_costumes()
    {
        if (Open(V1Folder) is not { } world)
        {
            return;
        }

        Assert.Equal(Dave, world.Actors[Dave].CostumeNumber);
        Assert.Equal(14, world.Actors[Dave].TalkColor);
        Assert.Equal(0, Open(V2Folder)?.Actors[Dave].CostumeNumber ?? 0);
    }
}

/// <summary>The start of the game, reached by standing in for the player's choices.</summary>
public class GameStartTests
{
    private const string V1Folder = "maniac_mansion_original_DOS.com";
    private const string V2Folder = "maniac_mansion_enhanced_DOS.com";

    /// <summary>
    /// The kid selection, done through its variables: Syd (1) and Razor (2) join Dave in Var[48]
    /// and Var[49], Var[111] counts them, Var[110] is the START click. The intro is skipped with the
    /// cutscene key; script 1 then puts the three kids on the driveway in room 44.
    /// </summary>
    private static Maniacal.Scumm.Game.GameWorld? StartGame(string folder)
    {
        if (GameFolders.Find(folder) is not { } path)
        {
            return null;
        }

        var world = new Maniacal.Scumm.Game.GameWorld(ResourceLoader.Open(new DirectoryGameContainer(path)));
        world.Boot();
        world.Engine.WriteVar(48, 1);
        world.Engine.WriteVar(49, 2);
        world.Engine.WriteVar(111, 4);
        world.Engine.WriteVar(110, 1);
        for (int step = 0; step < 300; step++)
        {
            if (step == 100)
            {
                world.Engine.AbortCutscene();
            }

            world.Step();
        }

        return world;
    }

    [Theory]
    [InlineData(V1Folder)]
    [InlineData(V2Folder)]
    public void The_kids_stand_on_the_driveway(string folder)
    {
        if (StartGame(folder) is not { } world)
        {
            return;
        }

        Assert.Equal(44, world.CurrentRoom?.Number);
        var kids = world.Actors.Where(a => a.InCurrentRoom).Select(a => (a.Number, a.Name, a.X, a.Y, a.Visible)).ToList();
        Assert.Equal([(1, "Syd", 29, 58, true), (2, "Razor", 34, 61, true), (3, "Dave", 24, 59, true)], kids);
    }

    /// <summary>Pinned after checking the screens by eye: the three kids by the moon (Syd put into his walkbox, a line lower).</summary>
    [Theory]
    [InlineData(V1Folder, "7d33d11a3d85849629c2c8de867031e7")]
    [InlineData(V2Folder, "22d5ab753f41cc9ce770ec06edf86474")]
    public void The_start_screen_is_stable(string folder, string md5)
    {
        if (StartGame(folder) is not { } world)
        {
            return;
        }

        Assert.Equal(md5, Md5.ComputeHex(world.Screen().Pixels));
    }
}
