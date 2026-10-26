using static Maniacal.Scumm.Tests.WalkthroughTests;

namespace Maniacal.Scumm.Tests;

/// <summary>
/// Wendy's way to win, in each release: the walkthrough's route as far as the lab's doors with
/// Wendy and Michael picked, Wendy playing Syd's part and Michael the third kid's, then the
/// Meteor's memoirs, rewritten, sent to Mark Eteer.
/// </summary>
public class WendysWayTests
{
    // the medical lab, behind the hallway's door after the middle door (on DOS the steel security
    // door, which only version 2 names so on this side)
    private const int MedicalLab = 22;

    private static string MedicalLabDoor(Player p) => p.World.Version == 2 ? "door[1]" : "door[2]";

    /// <summary>
    /// The manuscript: Wendy, let out of the dungeon and given the stamps, goes up to the medical
    /// lab, turns its desk lamp on, opens the desk and takes the Meteor's memoirs; in the plant room
    /// she rewrites them on the typewriter, as only she can, and puts them in the steamed envelope
    /// with the stamps.
    /// </summary>
    private static void TheManuscript(Player p)
    {
        p.Go("stairs");
        Assert.Equal(Landing, p.Room);
        p.Go(MiddleDoor(p));
        Assert.Equal(Hallway, p.Room);
        p.Do("Open", MedicalLabDoor(p));
        p.Go(MedicalLabDoor(p));
        Assert.Equal(MedicalLab, p.Room);
        p.Do("Turn on", "desk lamp");
        p.Do("Open", "desk");
        p.Do("Pick up", "manuscript", until: () => p.Carries("manuscript"));
        p.Go("door");
        Assert.Equal(Hallway, p.Room);
        p.Go("stairs");
        Assert.Equal(TentacleLanding, p.Room);
        p.Go("stairs[1]");
        Assert.Equal(TopHallway, p.Room);
        p.Go("door[5]");
        Assert.Equal(PlantRoom, p.Room);
        p.Do("Use", "manuscript", "typewriter", until: () => p.Heard("much better"));

        // the envelope is named for what it holds
        p.Do("Use", "manuscript", "envelope", until: () => !p.Carries("manuscript"));
        p.Do("Use", "stamps", "memoir in envelope", until: () => !p.Carries("stamps"));
    }

    /// <summary>
    /// The lab: Wendy lets Edna catch her for the dungeon and goes in; Purple Tentacle stops her,
    /// but given the contract, a publishing contract for the Meteor, lets her by. Beyond, Dr. Fred
    /// starts the self-destruct; Wendy takes the radiation suit from the locker, opens the far door
    /// with Ed's card key and turns the Meteor's switch off, which frees Dr. Fred.
    /// </summary>
    private static void TheLab(Player p)
    {
        p.Go("front door");
        Assert.Equal(Hall, p.Room);
        UpToTheTopHallway(p);
        p.Do("Open", "door[2]");
        p.Go("door[2]");
        Assert.True(p.Until(() => p.Kid.Room == Dungeon && p.Playable), "Edna never took Wendy to the dungeon");
        p.Go("inner door");
        Assert.Equal(LabEntrance, p.Room);
        p.Begin("Walk to", "door[2]");
        p.Do("Give", "contract", "Purple Tentacle", until: () => p.Heard("Move along"));
        p.Ready();
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

        // Dr. Fred, himself again, cannot stop the self-destruct, and asks for help
        p.WaitToHear("get rid of that meteor", TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// Wendy's way to win, every instruction on the way handled, saved from slot 52 on where each
    /// section of her own ends: given the contract the Meteor gives up crime for writing.
    /// </summary>
    [Theory]
    [InlineData(C64)]
    [InlineData(V1)]
    [InlineData(V2)]
    public void Wendy_has_the_memoirs_published(string folder)
    {
        if (Start(folder, "Wendy", "Michael") is not { } p)
        {
            return;
        }

        ToTheLabDoors(p, folder, saved: false);
        TheStamps(p);
        TheManuscript(p);
        Checkpoint(p, folder, 52);
        MailToMarkEteer(p, "memoir in stamped envelope");
        Checkpoint(p, folder, 53);
        TheLab(p);
        Checkpoint(p, folder, 54);
        p.Hurried = false;
        p.Begin("Give", "contract", "Meteor");
        p.Hear("You won the game!", TimeSpan.FromMinutes(5));
        Assert.Empty(p.Unhandled);
    }
}
