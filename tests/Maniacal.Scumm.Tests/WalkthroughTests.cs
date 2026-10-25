namespace Maniacal.Scumm.Tests;

/// <summary>
/// A winning route through the game, played by names, in each release: the route of
/// <c>docs/game/walkthrough.pdf</c>, a section at a time, each section played on from the last.
/// </summary>
public class WalkthroughTests
{
    private const string C64 = "maniac_mansion_c64", V1 = "maniac_mansion_original_DOS.com", V2 = "maniac_mansion_enhanced_DOS.com";

    // the rooms on the way
    private const int Driveway = 44, FrontOfTheHouse = 1, Hall = 10, Cellar = 8, LivingRoom = 3, Library = 5,
        Kitchen = 7, DiningRoom = 37, StorageRoom = 36, Pool = 6;

    /// <summary>The kids in the driveway, the game started: Dave, Syd and Razor on the C64, Syd, Dave and Bernard on DOS.</summary>
    private static Player? Start(string folder)
    {
        var world = folder == C64 ? GameWorldTests.AfterTheC64Intro() : PlayingTests.SelectedKids(folder);
        return world is null ? null : new Player(world);
    }

    /// <summary>
    /// Where a section ends, the game is saved into the folder <c>MANIACAL_CHECKPOINTS</c> names, if
    /// any, as ScummVM's slot file for the release's target (<c>maniac64</c>, <c>maniac1</c>,
    /// <c>maniac2</c>), for loading in ScummVM to compare.
    /// </summary>
    private static void Checkpoint(Player p, string folder, int slot)
    {
        if (Environment.GetEnvironmentVariable("MANIACAL_CHECKPOINTS") is not { Length: > 0 } checkpoints)
        {
            return;
        }

        string target = folder switch { C64 => "maniac64", V1 => "maniac1", _ => "maniac2" };
        File.WriteAllBytes(Path.Combine(checkpoints, $"{target}.s{slot:D2}"), p.World.Save($"Walkthrough {slot}", DateTime.Now));
    }

    /// <summary>The kid that is not Dave and comes first: Syd, in every release.</summary>
    private static string Second(Player p) => p.World.Actors[1].Name;

    /// <summary>
    /// Entering the house: the key from under the door mat unlocks the front door, and Dave goes in;
    /// the second kid follows him into the hall.
    /// </summary>
    private static void EnterTheHouse(Player p)
    {
        Assert.Equal(Driveway, p.Room);
        p.Leave(left: true);
        Assert.Equal(FrontOfTheHouse, p.Room);
        p.Do("Pull", "door mat");
        p.Do("Pick up", "key", until: () => p.Carries("key"));
        p.Do("Unlock", "front door", "key");
        p.Do("Open", "front door");
        p.Go("front door");
        Assert.Equal(Hall, p.Room);

        p.NewKid(Second(p));
        p.Leave(left: true);
        p.Go("front door");
        Assert.Equal(Hall, p.Room);
    }

    /// <summary>
    /// Getting the silver key: Dave pushes the right gargoyle, which holds the knobless door open
    /// while he stands, and the second kid goes through to the cellar, turns its light on, takes the
    /// key by the fuse box and goes back up to the hall.
    /// </summary>
    private static void GetTheSilverKey(Player p)
    {
        p.NewKid("Dave");
        p.Do("Push", "gargoyle[2]");
        p.NewKid(Second(p));
        p.Go("door[2]");
        Assert.Equal(Cellar, p.Room);
        p.Do("Turn on", "light switch");
        p.Do("Pick up", "silver key", until: () => p.Carries("silver key"));
        p.Go("stairs");
        Assert.Equal(Hall, p.Room);
    }

    /// <summary>
    /// The living room and the library: Dave leaves the gargoyle for the far door of the hall, opens
    /// the living room's cabinet on its cassette player, and in the dark library turns a lamp on,
    /// opens the loose panel and takes the blank cassette tape from behind it.
    /// </summary>
    private static void TheLivingRoomAndTheLibrary(Player p)
    {
        p.NewKid("Dave");
        p.Do("Open", "door[3]");
        p.Go("door[3]");
        Assert.Equal(LivingRoom, p.Room);
        p.Do("Open", "cabinet door");
        p.Do("Open", "door[2]");
        p.Go("door[2]");
        Assert.Equal(Library, p.Room);
        p.Do("Turn on", "lamp");
        p.Do("Open", "loose panel");
        p.Do("Pick up", "cassette tape", until: () => p.Carries("cassette tape"));
        p.Go("door");
        Assert.Equal(LivingRoom, p.Room);
    }

    /// <summary>
    /// The kitchen, the storage room and the pool: Syd waits in the living room until Nurse Edna
    /// has left the kitchen (five minutes of her timer from the front door, which stands still
    /// through cutscenes: between eight and ten minutes into the game here), then takes the
    /// flashlight and, from the fridge,
    /// the can of Pepsi; through the dining room to the storage room for the glass jar and the
    /// fruit drinks; the silver key opens its other door on the pool, whose water fills the jar.
    /// </summary>
    private static void TheKitchenAndThePool(Player p)
    {
        p.NewKid(Second(p));
        p.Go("door[3]");
        Assert.Equal(LivingRoom, p.Room);
        p.Wait(TimeSpan.FromMinutes(11));
        p.Go("door[1]");
        Assert.Equal(Hall, p.Room);
        p.Do("Open", "door[1]");
        p.Go("door[1]");
        Assert.Equal(Kitchen, p.Room);
        p.Do("Pick up", "flashlight", until: () => p.Carries("flashlight"));
        p.Do("Open", "refrigerator");
        p.Do("Pick up", "can of Pepsi", until: () => p.Carries("can of Pepsi"));
        p.Do("Open", "door[2]");
        p.Go("door[2]");
        Assert.Equal(DiningRoom, p.Room);
        p.Do("Open", "door[2]");
        p.Go("door[2]");
        Assert.Equal(StorageRoom, p.Room);
        p.Do("Pick up", "glass jar", until: () => p.Carries("glass jar"));
        p.Do("Pick up", "fruit drinks", until: () => p.Carries("fruit drinks"));
        p.Do("Unlock", "door[2]", "silver key");
        p.Do("Open", "door[2]");
        p.Go("door[2]");
        Assert.Equal(Pool, p.Room);
        p.Do("Use", "glass jar", "swimming pool");
    }

    /// <summary>
    /// The route so far, every instruction on the way handled, saved where each section ends. Timed
    /// events break in on the way and are waited out: the cutscene of Dr. Fred and Sandy in the lab,
    /// a minute in, and Nurse Edna's leaving the kitchen. Each save loaded in ScummVM 2.7.0 shows
    /// the same screen there, but for the cursor and the DOS sentence line, which ScummVM 2.7.0
    /// leaves blank after a load.
    /// </summary>
    [Theory]
    [InlineData(C64)]
    [InlineData(V1)]
    [InlineData(V2)]
    public void The_route_plays(string folder)
    {
        if (Start(folder) is not { } p)
        {
            return;
        }

        EnterTheHouse(p);
        GetTheSilverKey(p);
        Checkpoint(p, folder, 31);
        TheLivingRoomAndTheLibrary(p);
        TheKitchenAndThePool(p);
        Assert.True(p.Carries("jar of water"));
        Checkpoint(p, folder, 32);
        Assert.Empty(p.Unhandled);
    }
}
