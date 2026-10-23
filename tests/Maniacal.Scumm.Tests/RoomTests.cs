using Daventry.Core.Storage;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Rooms;

namespace Maniacal.Scumm.Tests;

public class RoomTests
{
    private const string V1Folder = "maniac_mansion_original_DOS.com";
    private const string V2Folder = "maniac_mansion_enhanced_DOS.com";

    // object numbers in room 1, the front of the mansion; the same in both DOS releases
    private const int FrontDoor = 61;
    private const int Key = 55;
    private const int DoorMat = 54;

    // verb numbers
    private const byte VerbOpen = 1;
    private const byte VerbClose = 2;
    private const byte VerbUnlock = 8;
    private const byte VerbPickUp = 14;

    [Theory]
    [InlineData(V1Folder, 1280, 136)]
    [InlineData(V2Folder, 960, 128)]
    public void Front_of_the_mansion(string folder, int width, int height)
    {
        if (Open(folder) is not { } loader)
        {
            return;
        }

        var room = loader.LoadRoom(1)!;

        Assert.Equal(width, room.Width);
        Assert.Equal(height, room.Height);
        Assert.Equal(19, room.Objects.Count);
        Assert.Equal([39], room.Sounds);
        Assert.Equal([57], room.Scripts);
        Assert.False(room.EntryScript.IsEmpty);
        Assert.False(room.ExitScript.IsEmpty);

        var door = room.FindObject(FrontDoor)!;
        Assert.Equal("front door", door.Name);
        Assert.Equal(39, door.FindVerb(VerbOpen));
        Assert.Equal(54, door.FindVerb(VerbClose));
        Assert.Equal(60, door.FindVerb(VerbUnlock));
        Assert.Null(door.FindVerb(VerbPickUp));

        // the key under the door mat: visible once the mat (its parent) is in state 8
        var key = room.FindObject(Key)!;
        Assert.Equal("key", key.Name);
        Assert.Equal(DoorMat, room.Objects[key.Parent - 1].Number);
        Assert.Equal(8, key.ParentState);
        Assert.Equal("door mat", room.FindObject(DoorMat)!.Name);
    }

    [Theory]
    [InlineData(V1Folder)]
    [InlineData(V2Folder)]
    public void Front_of_the_mansion_walkboxes(string folder)
    {
        if (Open(folder) is not { } loader)
        {
            return;
        }

        var boxes = loader.LoadRoom(1)!.WalkBoxes;

        Assert.Equal(10, boxes.Count);
        Assert.All(boxes.Boxes, box =>
        {
            Assert.True(box.UpperY <= box.LowerY);
            Assert.True(box.UpperLeftX <= box.UpperRightX);
            Assert.True(box.LowerLeftX <= box.LowerRightX);
        });

        // the boxes form a chain along the front of the house: the way from either end runs through its neighbour
        Assert.Equal(1, boxes.NextBox(0, 9));
        Assert.Equal(7, boxes.NextBox(9, 0));
        for (int box = 0; box < boxes.Count; box++)
        {
            Assert.Equal(box, boxes.NextBox(box, box));
        }
    }

    [Theory]
    [InlineData(V1Folder, 52)]
    [InlineData(V2Folder, 53)]
    public void Every_room_parses(string folder, int lastRoom)
    {
        if (Open(folder) is not { } loader)
        {
            return;
        }

        int objects = 0;
        for (int number = 1; number <= lastRoom; number++)
        {
            var room = loader.LoadRoom(number);
            Assert.NotNull(room);
            Assert.True(room.Width >= 320, $"room {number} is {room.Width} wide");
            Assert.InRange(room.Height, 1, 144);

            foreach (var obj in room.Objects)
            {
                Assert.InRange(obj.Parent, 0, room.Objects.Count);
                Assert.InRange(obj.ImageOffset, 0, room.Data.Length);
                Assert.All(obj.Verbs, verb => Assert.InRange(verb.Offset, 15, obj.Code.Length - 1));
            }

            var boxes = room.WalkBoxes;
            for (int from = 0; from < boxes.Count; from++)
            {
                for (int to = 0; to < boxes.Count; to++)
                {
                    Assert.InRange(boxes.NextBox(from, to), -1, boxes.Count - 1);
                }
            }

            objects += room.Objects.Count;
        }

        Assert.True(objects > 400, $"only {objects} objects");
    }

    private static ResourceLoader? Open(string folder)
        => GameFolders.Find(folder) is { } path ? ResourceLoader.Open(new DirectoryGameContainer(path)) : null;
}
