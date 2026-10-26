using static Maniacal.Scumm.Tests.WalkthroughTests;

namespace Maniacal.Scumm.Tests;

/// <summary>
/// Bernard's way to win, in each release: the walkthrough's route as far as the lab's doors, Bernard
/// the third kid (on the C64 picked for Razor), then the call to the Meteor Police.
/// </summary>
public class BernardsWayTests
{
    /// <summary>
    /// The Meteor Police: Bernard, who alone knows how, takes the tube out of the living room's old
    /// radio and fixes Dr. Fred's radio with it; the wanted poster beside it gives the Meteor
    /// Police's number, and he calls them on the radio. Five minutes on they come down to the
    /// dungeon and, the lab's doors open, go in and take the Meteor away.
    /// </summary>
    private static void TheMeteorPolice(Player p)
    {
        p.NewKid("Bernard");
        Assert.Equal(Hall, p.Room);
        p.Go("door[3]");
        Assert.Equal(LivingRoom, p.Room);
        p.Do("Open", "old fashion radio");
        p.Do("Pick up", "radio tube", until: () => p.Carries("radio tube"));
        p.Go("door[1]");
        Assert.Equal(Hall, p.Room);
        UpToTheTopHallway(p);
        p.Go("door[1]");
        Assert.Equal(FredsRoom, p.Room);
        p.Do("Use", "radio tube", "tube socket", until: () => !p.Carries("radio tube"));
        p.Do("Read", "wanted poster");
        p.Do("Turn on", "radio");
        p.Keypad("Use", "radio", Code(p, dos: 77, c64: 58));
        p.WaitToHear("This is the Meteor Police.", TimeSpan.FromMinutes(1));
        p.WaitToHear("crater-head", TimeSpan.FromMinutes(8));
    }

    /// <summary>
    /// The badge: Bernard lets Edna catch him for the dungeon, where the Meteor Police dropped a
    /// badge, and picks it up.
    /// </summary>
    private static void TheBadge(Player p)
    {
        p.Go("door");
        Assert.Equal(TopHallway, p.Room);
        p.Do("Open", "door[2]");
        p.Go("door[2]");
        Assert.True(p.Until(() => p.Kid.Room == Dungeon && p.Playable), "Edna never took Bernard to the dungeon");
        p.Do("Pick up", "badge", until: () => p.Carries("badge"));
    }

    /// <summary>
    /// The lab, and the game won: Purple Tentacle stops Bernard on his way in, but taking the badge
    /// for the Meteor Police's he runs. Beyond, Dr. Fred starts the self-destruct; Ed's card key
    /// opens the far door, and Bernard turns the switch off.
    /// </summary>
    private static void TheLab(Player p)
    {
        p.Go("inner door");
        Assert.Equal(LabEntrance, p.Room);
        p.Begin("Walk to", "door[2]");
        p.Do("Give", "badge", "Purple Tentacle", until: () => p.Heard("Meteor Police!!"));
        p.Ready();
        p.Do("Open", "door[2]", until: () => p.IsOpen("door[2]"));
        p.Go("door[2]");
        Assert.Equal(Lab, p.Room);

        p.Hurried = true;
        p.Do("Use", "card key", "card slot");
        p.Do("Open", "door[2]", until: () => p.IsOpen("door[2]"));
        p.Go("door[2]");
        Assert.Equal(MeteorsRoom, p.Room);
    }

    /// <summary>
    /// Bernard's way to win, every instruction on the way handled, saved from slot 49 on where each
    /// section of his own ends.
    /// </summary>
    [Theory]
    [InlineData(C64)]
    [InlineData(V1)]
    [InlineData(V2)]
    public void Bernard_calls_the_Meteor_Police(string folder)
    {
        if (Start(folder, third: "Bernard") is not { } p)
        {
            return;
        }

        ToTheLabDoors(p, folder, saved: false);
        TheMeteorPolice(p);
        Checkpoint(p, folder, 49);
        TheBadge(p);
        Checkpoint(p, folder, 50);
        TheLab(p);
        Checkpoint(p, folder, 51);
        p.Hurried = false;
        p.Do("Turn off", "switch");
        p.Hear("You won the game!", TimeSpan.FromMinutes(5));
        Assert.Empty(p.Unhandled);
    }
}
