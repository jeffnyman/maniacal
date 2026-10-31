using Maniacal.Scumm.Inspection;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Storage;

namespace Maniacal.Scumm.Tests;

/// <summary>
/// The inspection API as a consumer of the package uses it, its answers pinned to what the
/// player's <c>--room</c>, <c>--costume</c>, <c>--script</c> and <c>--view</c> printed before they
/// were built on it.
/// </summary>
public class InspectionTests
{
    private static GameInspector? Open(string folder)
        => GameFolders.Find(folder) is { } path ? new GameInspector(ResourceLoader.Open(new DirectoryGameContainer(path))) : null;

    [Fact]
    public void A_room_comes_with_its_picture_mask_and_objects()
    {
        if (Open(WalkthroughTests.V2) is not { } inspector)
        {
            return;
        }

        var room = inspector.Room(12)!;

        Assert.Equal((640, 128), (room.Width, room.Height));
        Assert.Equal((640, 128), (room.Pixels.Width, room.Pixels.Height));
        Assert.Equal((640, 128), (room.Mask.Width, room.Mask.Height));
        Assert.Equal(7, room.Objects.Count);
        var plant = room.Objects[0];
        Assert.Equal((386, "lovely plant"), (plant.Number, plant.Name));
        Assert.Equal((104, 24, 32, 88), (plant.X, plant.Y, plant.Width, plant.Height));
        Assert.Equal((112, 112), (plant.WalkX, plant.WalkY));
        Assert.Null(inspector.Room(99));
    }

    [Fact]
    public void The_objects_drawn_depend_on_which_are_asked_for()
    {
        if (Open(WalkthroughTests.V2) is not { } inspector)
        {
            return;
        }

        var none = inspector.Room(12, RoomObjects.None)!.Pixels.Pixels;
        var all = inspector.Room(12, RoomObjects.All)!.Pixels.Pixels;

        Assert.NotEqual(none, all);
    }

    [Theory]
    [InlineData(WalkthroughTests.V1, 0x57, 24)]
    [InlineData(WalkthroughTests.C64, null, 20)]
    public void A_costume_stands_four_ways_and_walks(string folder, int? format, int animations)
    {
        if (Open(folder) is not { } inspector)
        {
            return;
        }

        var costume = inspector.Costume(3)!;

        Assert.Equal(format, costume.Format);
        Assert.Equal(animations, costume.Animations);
        Assert.Equal(4, costume.Standing.Count);
        Assert.Equal(4, costume.Walking.Count);
        Assert.Contains(costume.Standing[0].Pixels.Pixels.ToArray(), p => p != CostumeFigures.Transparent);
        Assert.Null(inspector.Costume(99));
    }

    [Fact]
    public void A_script_is_listed_with_the_bytes_execution_never_reaches()
    {
        if (Open(WalkthroughTests.V2) is not { } inspector)
        {
            return;
        }

        var listing = inspector.Script(59)!;

        Assert.StartsWith("Script 59 (", listing.Title);
        Assert.Null(listing.Error);
        Assert.Contains(listing.Lines, l => l is { Instruction: null, Offset: 0x00F5, Length: 20 });
        Assert.Null(inspector.Script(999));
    }

    [Fact]
    public void A_rooms_scripts_start_with_its_entry()
    {
        if (Open(WalkthroughTests.C64) is not { } inspector)
        {
            return;
        }

        var entry = inspector.RoomScripts(1)![0];

        Assert.Equal("Room 1 entry", entry.Title);
        Assert.Equal(new ScriptLine { Offset = 0, Length = 2, Instruction = "lights(2)" }, entry.Lines[0]);
    }

    [Fact]
    public void A_view_stands_figures_in_the_room_and_says_where()
    {
        if (Open(WalkthroughTests.C64) is not { } inspector)
        {
            return;
        }

        var view = inspector.View(1, 300, [new StandingActor(1, 30, 60, Facing.Left), new StandingActor(3, 60, 62, Facing.Front), new StandingActor(5, 90, 40, Facing.Back)])!;

        Assert.Equal((320, 200), (view.Screen.Width, view.Screen.Height));
        Assert.Equal((296, 136), (view.CameraX, view.ViewLeft));
        Assert.Equal([5, 1, 3], view.Actors.Select(a => a.Costume));
        Assert.Equal(4, view.Actors.Single(a => a.Costume == 3).Box);
        Assert.Throws<KeyNotFoundException>(() => inspector.View(12, null, [new StandingActor(77, 10, 10, Facing.Front)]));
        Assert.Null(inspector.View(99));
    }
}
