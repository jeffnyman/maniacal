using Daventry.Core.Storage;
using Maniacal.Scumm.Game;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Rooms;

namespace Maniacal.Scumm.Tests;

public class BoxGeometryTests
{
    // a box with straight sides from (10, 20) to (30, 40), and one whose left side slants
    private static readonly BoxCorners Square = new(new Point(10, 20), new Point(30, 20), new Point(30, 40), new Point(10, 40));
    private static readonly BoxCorners Slanted = new(new Point(20, 20), new Point(30, 20), new Point(30, 40), new Point(10, 40));

    [Fact]
    public void Points_inside_and_outside()
    {
        Assert.True(BoxGeometry.Contains(Square, 20, 30));
        Assert.True(BoxGeometry.Contains(Square, 10, 20));
        Assert.False(BoxGeometry.Contains(Square, 31, 30));
        Assert.False(BoxGeometry.Contains(Square, 20, 41));

        // left of the slanted edge at the top, inside it at the bottom
        Assert.False(BoxGeometry.Contains(Slanted, 12, 22));
        Assert.True(BoxGeometry.Contains(Slanted, 12, 38));
    }

    [Fact]
    public void The_nearest_point_of_a_box()
    {
        Assert.Equal((0, new Point(20, 30)), BoxGeometry.NearestInBox(Square, 20, 30));

        // above the box: straight down onto its top; beside it: across onto its side
        Assert.Equal(new Point(20, 20), BoxGeometry.NearestInBox(Square, 20, 5).Point);
        Assert.Equal(new Point(30, 30), BoxGeometry.NearestInBox(Square, 50, 30).Point);
        Assert.True(BoxGeometry.NearestInBox(Square, 50, 30).Distance > 0);
    }

    [Fact]
    public void The_nearest_point_on_a_line_stays_on_the_segment()
    {
        var start = new Point(0, 0);
        var end = new Point(10, 0);

        Assert.Equal(new Point(5, 0), BoxGeometry.ClosestPointOnLine(start, end, new Point(5, 7)));
        Assert.Equal(end, BoxGeometry.ClosestPointOnLine(start, end, new Point(20, 3)));
        Assert.Equal(start, BoxGeometry.ClosestPointOnLine(start, end, new Point(-4, 3)));
    }

    [Fact]
    public void The_nearest_point_on_a_box_edge()
    {
        var (distance, point) = BoxGeometry.ClosestPointOnBox(Square, 20, 10);

        Assert.Equal(new Point(20, 20), point);
        Assert.Equal(100, distance);
    }
}

public class WalkingTests
{
    private const string V1Folder = "maniac_mansion_original_DOS.com";
    private const string V2Folder = "maniac_mansion_enhanced_DOS.com";

    private const int Dave = 3;

    /// <summary>Dave in room 1, the front of the mansion, standing at a point.</summary>
    private static (GameWorld World, Actor Dave)? InRoom1(string folder, int x, int y)
    {
        if (GameFolders.Find(folder) is not { } path)
        {
            return null;
        }

        var world = new GameWorld(ResourceLoader.Open(new DirectoryGameContainer(path)));
        world.StartScene(1);
        var dave = world.Actors[Dave];
        dave.SetCostume(Dave);
        dave.Put(x, y, 1);
        return (world, dave);
    }

    private static int WalkUntilStill(Actor actor, int limit = 1000)
    {
        int steps = 0;
        while (actor.Moving != 0 && steps < limit)
        {
            actor.Walk();
            actor.AnimateCostume();
            steps++;
        }

        return steps;
    }

    [Fact]
    public void Putting_an_actor_puts_it_in_a_walkbox()
    {
        if (InRoom1(V2Folder, 20, 30) is not var (_, dave))
        {
            return;
        }

        // (20, 30) is above the path in front of the house: the nearest box is below
        Assert.NotEqual(BoxGeometry.NoBox, dave.Walkbox);
        Assert.True(dave.Y > 30);
    }

    /// <summary>A walk along one box: there, standing, facing the way it went.</summary>
    [Theory]
    [InlineData(V1Folder)]
    [InlineData(V2Folder)]
    public void Walking_along_a_box_arrives(string folder)
    {
        if (InRoom1(folder, 10, 60) is not var (_, dave))
        {
            return;
        }

        int start = dave.X;
        dave.StartWalk(dave.X + 12, dave.Y, -1);
        int steps = WalkUntilStill(dave);

        Assert.Equal(start + 12, dave.X);
        Assert.Equal(0, dave.Moving);
        Assert.Equal(90, dave.Facing);
        Assert.InRange(steps, 12, 40);
    }

    /// <summary>A walk across the front of the house goes from box to box by the box matrix and arrives.</summary>
    [Fact]
    public void Walking_crosses_boxes()
    {
        if (InRoom1(V2Folder, 10, 60) is not var (world, dave))
        {
            return;
        }

        int startBox = dave.Walkbox;
        dave.StartWalk(110, 57, -1);
        var boxes = new HashSet<int>();
        int steps = 0;
        while (dave.Moving != 0 && steps++ < 2000)
        {
            dave.Walk();
            boxes.Add(dave.Walkbox);
        }

        Assert.Equal((110, 57), (dave.X, dave.Y));
        Assert.Equal(8, dave.Walkbox);
        Assert.NotEqual(startBox, dave.Walkbox);
        Assert.True(boxes.Count >= 4, $"went through boxes {string.Join(", ", boxes)}");
    }

    /// <summary>Turning goes a quarter turn a frame: from front to back by way of a side.</summary>
    [Theory]
    [InlineData(V1Folder)]
    [InlineData(V2Folder)]
    public void Turning_goes_a_quarter_at_a_time(string folder)
    {
        if (InRoom1(folder, 10, 60) is not var (_, dave))
        {
            return;
        }

        dave.SetDirection(180);
        dave.TurnTo(0);

        dave.Walk();
        Assert.True(dave.Facing is 90 or 270, $"facing {dave.Facing}");

        dave.Walk();
        Assert.Equal(0, dave.Facing);

        dave.Walk();
        Assert.Equal(0, dave.Moving);
    }

    /// <summary>A walk to the same place, facing the same way, is no walk.</summary>
    [Fact]
    public void Walking_to_where_the_actor_is_does_nothing()
    {
        if (InRoom1(V2Folder, 10, 60) is not var (_, dave))
        {
            return;
        }

        dave.StartWalk(dave.X, dave.Y, -1);

        Assert.Equal(0, dave.Moving);
    }
}

/// <summary>The intro, played through: it needs walking for the meteor and the camera variable for the logo.</summary>
public class IntroTests
{
    private const string V1Folder = "maniac_mansion_original_DOS.com";
    private const string V2Folder = "maniac_mansion_enhanced_DOS.com";

    [Theory]
    [InlineData(V1Folder)]
    [InlineData(V2Folder)]
    public void The_intro_plays_to_the_start_of_the_game(string folder)
    {
        if (GameFolders.Find(folder) is not { } path)
        {
            return;
        }

        var world = new GameWorld(ResourceLoader.Open(new DirectoryGameContainer(path)));
        world.Boot();
        world.Engine.WriteVar(48, 1);
        world.Engine.WriteVar(49, 2);
        world.Engine.WriteVar(111, 4);
        world.Engine.WriteVar(110, 1);

        var rooms = new List<int>();
        var messages = new HashSet<string>();
        bool meteorLanded = false;
        for (int step = 0; step < 1500 && !(rooms.Count > 0 && rooms[^1] == 44); step++)
        {
            world.Step();
            int room = world.CurrentRoom?.Number ?? 0;
            if (rooms.Count == 0 || rooms[^1] != room)
            {
                rooms.Add(room);
            }

            if (world.Messages.HaveMessage != 0)
            {
                messages.Add(world.Messages.Plain());
            }

            var meteor = world.Actors[22];
            meteorLanded |= meteor.InCurrentRoom && (meteor.X, meteor.Y) == (29, 52);
        }

        // the hill where the meteor lands, the logo, then the driveway
        Assert.True(meteorLanded);
        Assert.Contains(33, rooms);
        Assert.Contains(49, rooms);
        Assert.Equal(44, rooms[^1]);
        Assert.Contains(messages, m => m.Contains("Twenty years ago today", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("Ron Gilbert and David Fox", StringComparison.Ordinal));
        Assert.Equal([1, 2, 3], world.Actors.Where(a => a.InCurrentRoom && a.Visible).Select(a => a.Number));
    }
}
