using Maniacal.Scumm.Game;
using Maniacal.Scumm.View;

namespace Maniacal.Scumm.Tests;

public class TransitionTests
{
    /// <summary>
    /// A change of room plays the iris: the room on screen closes to black, then the new one opens
    /// from the middle, in the iterations versions before 3 work out from the room's height (9 for
    /// the C64's 136 lines, 8 for version 2's 128), each shown for the version's delay (6
    /// quarter-frames on the C64, 4 for version 2). Version 1 has no delay, so nothing to show. The
    /// step lasts as much longer, and the last frame shows the room the step drew.
    /// </summary>
    [Theory]
    [InlineData("maniac_mansion_c64", 9, 6)]
    [InlineData("maniac_mansion_enhanced_DOS.com", 8, 4)]
    [InlineData("maniac_mansion_original_DOS.com", 0, 0)]
    public void Leaving_a_room_closes_an_iris_and_the_next_opens_one(string folder, int iterations, int quarterFrames)
    {
        var world = folder == "maniac_mansion_c64" ? GameWorldTests.AfterTheC64Intro() : PlayingTests.SelectedKids(folder);
        if (world is null)
        {
            return;
        }

        // off the drive's left edge
        int room = world.CurrentRoom!.Number;
        world.Click(4, 100);
        for (int i = 0; i < 400 && world.CurrentRoom?.Number == room; i++)
        {
            world.Step();
        }

        Assert.NotEqual(room, world.CurrentRoom?.Number);
        var frames = world.Transition;
        Assert.Equal(2 * iterations, frames.Count);
        Assert.All(frames, f => Assert.Equal(quarterFrames, f.QuarterFrames));
        Assert.Equal(world.StepTicks * 4 + 2 * iterations * quarterFrames, world.StepLength.TotalSeconds * world.TimerFrequency, 3);
        if (iterations == 0)
        {
            return;
        }

        // closed: the room black; open: the room the step drew (the message line, which a
        // transition leaves alone, is drawn after it)
        var layout = ScreenLayout.ForVersion(world.Version);
        var closed = frames[iterations - 1].Screen;
        var screen = world.Screen();
        for (int y = layout.RoomTop; y < layout.RoomBottom; y++)
        {
            Assert.All(closed.Row(y).ToArray(), p => Assert.Equal(0, p));
            Assert.Equal(screen.Row(y).ToArray(), frames[^1].Screen.Row(y).ToArray());
        }

        // an opening frame halfway shows the middle of the new room, not yet its edges
        var half = frames[iterations + iterations / 2].Screen;
        Assert.Equal(screen[160, layout.RoomTop + layout.RoomHeight / 2], half[160, layout.RoomTop + layout.RoomHeight / 2]);
        Assert.Equal(0, half[0, layout.RoomTop]);
    }
}
