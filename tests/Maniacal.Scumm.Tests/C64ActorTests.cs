using Maniacal.Scumm.Costumes;
using Maniacal.Scumm.Game;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Rooms;
using Maniacal.Scumm.Storage;

namespace Maniacal.Scumm.Tests;

public class C64ActorTests
{
    private const string C64Folder = "maniac_mansion_c64";

    /// <summary>A room of version 0 boxes and no costumes, for actors that only walk.</summary>
    private sealed class Boxes(params WalkBox[] boxes) : IActorWorld
    {
        public int CurrentRoom => 1;

        public int Version => 0;

        public int BoxCount => boxes.Length;

        public BoxCorners BoxCorners(int box) => Rooms.BoxCorners.Of(boxes[box]);

        public int BoxFlags(int box) => 0;

        public int NextBox(int from, int to) => -1;

        public bool IsPlayer(int actor) => false;

        public Costume? LoadCostume(int costume) => null;

        public int BoxMask(int box) => box >= 0 && box < boxes.Length ? boxes[box].Mask : 0;

        public IReadOnlyList<byte> BoxNeighbours(int box) => [];

        public bool AreBoxesNeighbours(int box1, int box2) => false;

        public V0Costume? LoadCostumeV0(int costume) => null;
    }

    // version 0 corner boxes from column 10 to 30 and line 20 to 41, the left side slanting (0x88) or the right (0x8C)
    private static WalkBox Corner(byte mask) => WalkBox.ReadV0([10, 30, 20, 41, mask]);

    /// <summary>
    /// A point beside a slanted box comes onto its diagonal, which narrows by the C64's table a line
    /// from the top rather than straight: 8 columns 10 lines down. One already inside stays. This
    /// follows the C64 (<c>clamp_to_diagonal_edge</c>), where ScummVM's <c>adjustPosInBorderWalkbox</c>
    /// sends the point on the left diagonal to column 0.
    /// </summary>
    [Fact]
    public void Points_are_kept_inside_a_slanted_walkbox()
    {
        var left = new ActorV0(1, new Boxes(Corner(0x88)));
        Assert.Equal((new Point(30, 20), 0), left.AdjustToBox(12, 20));
        Assert.Equal((new Point(22, 30), 0), left.AdjustToBox(15, 30));
        Assert.Equal((new Point(25, 30), 0), left.AdjustToBox(25, 30));

        var right = new ActorV0(1, new Boxes(Corner(0x8C)));
        Assert.Equal((new Point(18, 30), 0), right.AdjustToBox(28, 30));
        Assert.Equal((new Point(14, 30), 0), right.AdjustToBox(14, 30));
    }

    /// <summary>
    /// A box's distance counts columns double: a box 3 columns beside a point is further than one
    /// 16 lines (4 in distance) below it, which it would not be in versions 1 and 2.
    /// </summary>
    [Fact]
    public void The_nearest_box_counts_columns_double()
    {
        var beside = WalkBox.ReadV0([20, 30, 10, 30, 0]);
        var below = WalkBox.ReadV0([0, 30, 36, 40, 0]);
        var actor = new ActorV0(1, new Boxes(beside, below));

        Assert.Equal((new Point(17, 36), 1), actor.AdjustToBox(17, 20));
        Assert.Equal((new Point(25, 20), 0), actor.AdjustToBox(25, 20));
    }

    /// <summary>
    /// ScummVM's <c>areBoxesNeighbors</c> reads the neighbour lists from 4 bytes in, so it finds a
    /// box's neighbours where the lists 4 bytes on say; <c>getNextBox</c> reads them as they are.
    /// </summary>
    [Fact]
    public void Neighbours_are_tested_as_ScummVM_reads_them()
    {
        // three boxes; box 0 joins 1, box 1 joins 0 and 2, box 2 joins 1
        byte[] data = [0, 1, 0, 1, 0, 2, 3, 0, 1, 0, 4, 5, 0, 1, 0, 0xFF, 1, 0xFF, 0, 2, 0xFF, 1, 0xFF];
        var boxes = WalkBoxes.ParseV0(data);

        Assert.Equal(1, boxes.NextBox(0, 1));
        Assert.Equal(-1, boxes.NextBox(0, 2));

        // from 4 bytes in, box 0's list is empty (the end of box 1's), box 1's is box 2's, and box 2's is past the end
        Assert.False(boxes.AreNeighboursV0(0, 1));
        Assert.False(boxes.AreNeighboursV0(1, 0));
        Assert.True(boxes.AreNeighboursV0(1, 1));
        Assert.False(boxes.AreNeighboursV0(2, 1));
    }

    /// <summary>
    /// Every costume of the C64 release parses, and every cel of every clip's sequences decodes:
    /// 20 clips each, but the meteor's 12.
    /// </summary>
    [Fact]
    public void Every_c64_costume_and_its_clips_decode()
    {
        if (GameFolders.Find(C64Folder) is not { } path)
        {
            return;
        }

        var loader = ResourceLoader.Open(new DirectoryGameContainer(path));
        int cels = 0;
        for (int number = 1; number < 25; number++)
        {
            var costume = loader.LoadCostumeV0(number)!;
            Assert.Equal(number == 16 ? 12 : 20, costume.ClipCount);
            for (int clip = 0; clip < costume.ClipCount; clip++)
            {
                for (int limb = 0; limb < V0Costume.Limbs; limb++)
                {
                    byte sequence = costume.Clip(clip, limb);
                    if (sequence == V0Costume.End)
                    {
                        continue;
                    }

                    int start = costume.Sequence(costume.Sequence(limb) + (sequence & 0x7F));
                    for (int i = start; costume.Sequence(i) != V0Costume.End; i++)
                    {
                        costume.Cel(costume.Sequence(i));
                        cels++;
                    }
                }
            }
        }

        Assert.Equal(3083, cels);
    }

    /// <summary>
    /// A kid's clips: standing faces a way and holds, the mouth's clip changes only the head, and
    /// facing left mirrors the limbs of facing right.
    /// </summary>
    [Fact]
    public void Clips_set_the_limbs()
    {
        if (GameFolders.Find(C64Folder) is not { } path)
        {
            return;
        }

        var dave = ResourceLoader.Open(new DirectoryGameContainer(path)).LoadCostumeV0(3)!;
        const int FaceLeft = 4, FaceRight = 5, MouthShutRight = 0x11;

        var state = new V0CostumeState { NewClip = FaceRight, Repeat = -1 };
        state.Decode(dave);
        state.Increase(dave);
        Assert.All(Enumerable.Range(0, V0Costume.Limbs), limb => Assert.False(state.Flipped(limb)));
        int[] frames = [.. Enumerable.Range(0, V0Costume.Limbs).Select(state.Frame)];

        state.NewClip = MouthShutRight;
        state.Decode(dave);
        int changed = Enumerable.Range(0, V0Costume.Limbs).Count(limb => state.Frame(limb) != frames[limb]);
        Assert.InRange(changed, 0, 1);

        var left = new V0CostumeState { NewClip = FaceLeft, Repeat = -1 };
        left.Decode(dave);
        left.Increase(dave);
        var right = new V0CostumeState { NewClip = FaceRight, Repeat = -1 };
        right.Decode(dave);
        right.Increase(dave);
        var palette = V0CostumeRenderer.Palette(3, actorColors: true, flashlight: false);
        var leftFrame = V0CostumeRenderer.DrawLimbs(dave, left, palette);
        var rightFrame = V0CostumeRenderer.DrawLimbs(dave, right, palette);
        Assert.Equal((rightFrame.Pixels.Width, rightFrame.Pixels.Height), (leftFrame.Pixels.Width, leftFrame.Pixels.Height));
        for (int y = 0; y < leftFrame.Pixels.Height; y++)
        {
            for (int x = 0; x < leftFrame.Pixels.Width; x++)
            {
                Assert.Equal(rightFrame.Pixels[rightFrame.Pixels.Width - 1 - x, y], leftFrame.Pixels[x, y]);
            }
        }
    }
}
