using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Rooms;
using Maniacal.Scumm.Storage;

namespace Maniacal.Scumm.Tests;

public class RoomTests
{
    private const string V1Folder = "maniac_mansion_original_DOS.com";
    private const string V2Folder = "maniac_mansion_enhanced_DOS.com";
    private const string C64Folder = "maniac_mansion_c64";

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

    /// <summary>
    /// The C64's front of the mansion: the same room, with the C64's object numbers. Objects that
    /// are part of the room have type 1 in their number's high byte, as the doorbell (130).
    /// </summary>
    [Fact]
    public void C64_front_of_the_mansion()
    {
        if (Open(C64Folder) is not { } loader)
        {
            return;
        }

        const int C64FrontDoor = 28, C64Key = 22, C64DoorMat = 21, Doorbell = 0x100 | 130;
        var room = loader.LoadRoom(1)!;

        Assert.Equal(0, room.Version);
        Assert.Equal(1280, room.Width);
        Assert.Equal(136, room.Height);
        Assert.Equal(19, room.Objects.Count);
        Assert.False(room.EntryScript.IsEmpty);
        Assert.False(room.ExitScript.IsEmpty);

        var door = room.FindObject(C64FrontDoor)!;
        Assert.Equal("front door", door.Name);
        Assert.Equal((432, 8, 88, 80), (door.X, door.Y, door.Width, door.Height));
        Assert.NotNull(door.FindVerb(VerbOpen));
        Assert.NotNull(door.FindVerb(VerbUnlock));
        Assert.Null(door.FindVerb(VerbPickUp));

        var key = room.FindObject(C64Key)!;
        Assert.Equal("key", key.Name);
        Assert.Equal(C64DoorMat, room.Objects[key.Parent - 1].Number);
        Assert.Equal(8, key.ParentState);
        Assert.Equal("doorbell", room.FindObject(Doorbell)!.Name);
    }

    /// <summary>The C64 keeps each box's neighbours: the way to a box is known only when it is next door.</summary>
    [Fact]
    public void C64_walkboxes_know_their_neighbours()
    {
        if (Open(C64Folder) is not { } loader)
        {
            return;
        }

        var boxes = loader.LoadRoom(1)!.WalkBoxes;

        Assert.Equal(10, boxes.Count);
        Assert.Equal([2, 0], boxes.Neighbours![1]);
        Assert.Equal(1, boxes.NextBox(0, 1));
        Assert.Equal(-1, boxes.NextBox(0, 9));
        Assert.Equal(9, boxes.NextBox(7, 9));
    }

    [Theory]
    [InlineData(V1Folder, 52)]
    [InlineData(V2Folder, 53)]
    [InlineData(C64Folder, 52)]
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
                Assert.All(obj.Verbs, verb => Assert.InRange(verb.Offset, room.Version == 0 ? 14 : 15, obj.Code.Length - 1));
            }

            var boxes = room.WalkBoxes;
            for (int from = 0; from < boxes.Count; from++)
            {
                for (int to = 0; to < boxes.Count; to++)
                {
                    Assert.InRange(boxes.NextBox(from, to), -1, boxes.Count - 1);
                    if (boxes.Neighbours is not null)
                    {
                        Assert.Equal(boxes.Neighbours[from].Contains((byte)to) ? to : -1, boxes.NextBox(from, to));
                    }
                }
            }

            objects += room.Objects.Count;
        }

        Assert.True(objects > 400, $"only {objects} objects");
    }

    /// <summary>
    /// Version 0 walkboxes: rectangles of x1, x2, y1, y2 and a mask, up to an 0xFF, then a list of
    /// neighbours for each box. A box with mask bits 0x88 narrows its top to one corner: the left
    /// one with bit 4, else the right one.
    /// </summary>
    [Fact]
    public void Version_0_walkboxes_are_rectangles_corners_and_neighbour_lists()
    {
        byte[] data =
        [
            2, 10, 5, 20, 0x00,
            4, 9, 21, 30, 0x8C,
            4, 9, 31, 40, 0x88,
            0xFF,
            1, 0xFF,
            0, 2, 0xFF,
            1, 0xFF,
        ];

        var boxes = WalkBoxes.ParseV0(data);

        Assert.Equal(3, boxes.Count);
        Assert.Equal(new WalkBox(5, 20, 2, 10, 2, 10, 0x00, 0), boxes.Boxes[0]);
        Assert.Equal(new WalkBox(21, 30, 4, 4, 4, 9, 0x8C, 0), boxes.Boxes[1]);
        Assert.Equal(new WalkBox(31, 40, 9, 9, 4, 9, 0x88, 0), boxes.Boxes[2]);
        Assert.Equal([0, 2], boxes.Neighbours![1]);
        Assert.Equal(2, boxes.NextBox(1, 2));
        Assert.Equal(-1, boxes.NextBox(0, 2));
        Assert.Equal(1, boxes.FindBox(5, 25));
        Assert.Equal(-1, boxes.FindBox(7, 25));
    }

    [Fact]
    public void Version_0_walkboxes_without_their_lists_are_rejected()
    {
        Assert.Throws<InvalidDataException>(() => WalkBoxes.ParseV0([2, 10, 5, 20, 0x00, 0xFF, 1]));
        Assert.Throws<InvalidDataException>(() => WalkBoxes.ParseV0([2, 10, 5]));
    }

    private static ResourceLoader? Open(string folder)
        => GameFolders.Find(folder) is { } path ? ResourceLoader.Open(new DirectoryGameContainer(path)) : null;
}
