using static Maniacal.Scumm.Tests.WalkthroughTests;

namespace Maniacal.Scumm.Tests;

/// <summary>
/// Michael's way to win, in each release: the walkthrough's route as far as the lab's doors with
/// Wendy and Michael picked, Wendy playing Syd's part and Michael the third kid's, then Ed's plans,
/// photographed, developed for him.
/// </summary>
public class MichaelsWayTests
{
    private const int TedsBathroom = 24, Darkroom = 23;

    /// <summary>
    /// The developer and the sponge: Michael tries to pick up the bottle of developer in the
    /// storage room, which falls and breaks, its developer running down under the house; up in
    /// Ted's bathroom he takes the sponge.
    /// </summary>
    private static void TheDeveloperAndTheSponge(Player p)
    {
        p.NewKid("Michael");
        Assert.Equal(Hall, p.Room);
        p.Do("Open", "door[1]");
        p.Go("door[1]");
        Assert.Equal(Kitchen, p.Room);
        p.Do("Open", "door[2]");
        p.Go("door[2]");
        Assert.Equal(DiningRoom, p.Room);
        p.Do("Open", "door[2]");
        p.Go("door[2]");
        Assert.Equal(StorageRoom, p.Room);
        p.Do("Pick up", "bottle of developer", until: () => p.Heard("Whoops!"));
        p.Go("door[1]");
        Assert.Equal(DiningRoom, p.Room);
        p.Go("door[1]");
        Assert.Equal(Kitchen, p.Room);
        p.Go("door[1]");
        Assert.Equal(Hall, p.Room);

        UpToTheTopHallway(p);
        p.Go("door[4]");
        Assert.Equal(TedsRoom, p.Room);
        p.Do("Open", "door[2]");
        p.Go("door[2]");
        Assert.Equal(TedsBathroom, p.Room);
        p.Do("Pick up", "sponge", until: () => p.Carries("sponge"));
        p.Go("door");
        Assert.Equal(TedsRoom, p.Room);
        p.Go("door[1]");
        Assert.Equal(TopHallway, p.Room);
    }

    /// <summary>
    /// Ed's package: Michael walks into Ed's room and, as Ed comes to take him, gives him his
    /// package; Ed, thankful, no longer minds him, and his plans, lost, lie on film by the bushes
    /// outside. Michael picks the film up and, under the house, soaks the sponge in the developer.
    /// </summary>
    private static void TheFilm(Player p)
    {
        p.Do("Open", "door[3]");
        p.Go("door[3]");
        Assert.Equal(EdsRoom, p.Room);
        p.Do("Give", "package", "Weird Ed", until: () => p.Heard("commando package"));
        p.Go("door");
        Assert.Equal(TopHallway, p.Room);

        DownToTheHall(p);
        p.Go("front door");
        Assert.Equal(FrontOfTheHouse, p.Room);
        p.Do("Pick up", "undeveloped film", until: () => p.Carries("undeveloped film"));
        p.Go("grating");
        Assert.Equal(UnderTheHouse, p.Room);
        p.Do("Use", "sponge", "puddle of developer", until: () => p.Carries("sponge with developer"));
        p.Go("grate");
        Assert.Equal(FrontOfTheHouse, p.Room);
    }

    /// <summary>
    /// The prints: behind the door on Green Tentacle's landing is a darkroom. By its red light the
    /// sponge fills the developer tray, and Michael, who alone knows how, develops the film into
    /// prints of Ed's plans, which he takes up to Ed: Ed will see to the Meteor in the lab.
    /// </summary>
    private static void ThePrints(Player p)
    {
        p.Go("front door");
        Assert.Equal(Hall, p.Room);
        p.Go("stairs");
        Assert.Equal(Landing, p.Room);
        p.Go(MiddleDoor(p));
        Assert.Equal(Hallway, p.Room);
        p.Go("stairs");
        Assert.Equal(TentacleLanding, p.Room);
        p.Do("Open", "door");
        p.Go("door");
        Assert.Equal(Darkroom, p.Room);
        p.Do("Turn on", "red light");
        p.Do("Use", "sponge with developer", "developer tray", until: () => !p.Carries("sponge with developer"));
        p.Do("Use", "undeveloped film", "tray of developer", until: () => p.Heard("Perfect!"));

        // Dr. Fred comes back to Sandy in the lab 45 minutes after he first did, a cutscene that
        // breaks in wherever the kid is, and a C64 kid it stops on the stairs stays stuck there:
        // Michael waits for it here, unless it came already. He shut the door to develop the film.
        p.WaitToHear("Stop playing with the lab experiments.", TimeSpan.FromMinutes(15));
        p.Do("Open", "door");
        p.Go("door");
        Assert.Equal(TentacleLanding, p.Room);
        p.Go("stairs[1]");
        Assert.Equal(TopHallway, p.Room);
        p.Do("Open", "door[3]");
        p.Go("door[3]");
        Assert.Equal(EdsRoom, p.Room);
        p.Do("Give", "prints", "Weird Ed", until: () => p.Heard("overthrow the meteor"));
    }

    /// <summary>
    /// The lab: Michael lets Edna catch him for the dungeon, where Wendy gives him the yellow key,
    /// and goes in; Purple Tentacle stops him, and Ed comes to chase Purple Tentacle off. Beyond,
    /// Dr. Fred starts the self-destruct; Michael takes the radiation suit from the locker, opens the
    /// far door with Ed's card key, turns the Meteor's switch off and takes the Meteor out to the
    /// garage, into the car's trunk, and shuts the trunk.
    /// </summary>
    private static void TheLab(Player p)
    {
        p.Go("door");
        Assert.Equal(TopHallway, p.Room);
        p.Do("Open", "door[2]");
        p.Go("door[2]");
        Assert.True(p.Until(() => p.Kid.Room == Dungeon && p.Playable), "Edna never took Michael to the dungeon");
        p.NewKid(Second(p));
        p.Do("Give", "yellow key", "Michael", until: () => p.Carries("yellow key", "Michael"));
        p.NewKid("Michael");
        p.Go("inner door");
        Assert.Equal(LabEntrance, p.Room);
        p.Begin("Walk to", "door[2]");
        p.WaitToHear("getting out of here", TimeSpan.FromMinutes(1));
        p.Do("Open", "door[2]", until: () => p.IsOpen("door[2]"));
        p.Go("door[2]");
        Assert.Equal(Lab, p.Room);

        p.Hurried = true;
        p.Do("Open", "locker");
        p.Do("Pick up", "radiation suit");
        p.Do("Use", "card key", "card slot");
        p.Do("Open", "door[2]", until: () => p.IsOpen("door[2]"));
        p.Go("door[2]");
        Assert.Equal(MeteorsRoom, p.Room);
        p.Do("Turn off", "switch");
        p.Do("Pick up", "meteor", until: () => p.Carries("meteor"));
        p.Do("Open", "door[2]", until: () => p.IsOpen("door[2]"));
        p.Go("door[2]");
        Assert.Equal(Garage, p.Room);
        p.Do("Use", "meteor", "trunk", until: () => !p.Carries("meteor"));
        p.Do("Close", "trunk");
    }

    /// <summary>
    /// Michael's way to win, every instruction on the way handled, saved from slot 55 on where each
    /// section of his own ends: the yellow key starts the car, which takes the Meteor away.
    /// </summary>
    [Theory]
    [InlineData(C64)]
    [InlineData(V1)]
    [InlineData(V2)]
    public void Michael_develops_Eds_plans(string folder)
    {
        if (Start(folder, "Wendy", "Michael") is not { } p)
        {
            return;
        }

        ToTheLabDoors(p, folder, saved: false);
        TheDeveloperAndTheSponge(p);
        Checkpoint(p, folder, 55);
        TheFilm(p);
        Checkpoint(p, folder, 56);
        ThePrints(p);
        Checkpoint(p, folder, 57);
        TheLab(p);
        Checkpoint(p, folder, 58);
        p.Hurried = false;
        p.Begin("Use", "yellow key", "weird edsel");
        p.Hear("You won the game!", TimeSpan.FromMinutes(5));
        Assert.Empty(p.Unhandled);
    }
}
