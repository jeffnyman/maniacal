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
        Kitchen = 7, DiningRoom = 37, StorageRoom = 36, Pool = 6, Landing = 11, PaintingRoom = 14, MusicRoom = 17,
        Hallway = 13, TentacleLanding = 12, TopHallway = 38, FredsRoom = 21, TentaclesRoom = 20, TedsRoom = 25, PlantRoom = 27, TelescopeRoom = 28;

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
        p.WaitUntil(TimeSpan.FromMinutes(11));
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
    /// Upstairs: Syd waits at the pool while Weird Ed, hungry, comes down through the house to the
    /// kitchen's fridge and goes back up (he would take a kid he met to the dungeon), then goes back
    /// through the house to the hall and up the stairs; takes the bowl of
    /// wax fruit and the paint remover from the painting room, watches the ad on the music room's TV,
    /// and goes through the middle door (DOS's steel security door) and up to Green Tentacle, who lets him by for the
    /// wax fruit and the fruit drinks. On the top floor he takes the dime from Dr. Fred's floor and,
    /// up the ladder in Green Tentacle's room, the record and the yellow key.
    /// </summary>
    private static void Upstairs(Player p)
    {
        p.WaitToHear("heese", TimeSpan.FromMinutes(5));
        p.Wait(TimeSpan.FromMinutes(1));
        p.Go("door");
        Assert.Equal(StorageRoom, p.Room);
        p.Go("door[1]");
        Assert.Equal(DiningRoom, p.Room);
        p.Go("door[1]");
        Assert.Equal(Kitchen, p.Room);

        // Ed shut the kitchen's doors behind him
        p.Do("Open", "door[1]");
        p.Go("door[1]");
        Assert.Equal(Hall, p.Room);
        p.Go("stairs");
        Assert.Equal(Landing, p.Room);

        p.Do("Open", "door[1]");
        p.Go("door[1]");
        Assert.Equal(PaintingRoom, p.Room);
        p.Do("Pick up", "bowl of wax fruit", until: () => p.Carries("bowl of wax fruit"));
        p.Do("Pick up", "paint remover", until: () => p.Carries("paint remover"));
        p.Go("door");
        Assert.Equal(Landing, p.Room);
        p.Do("Open", MusicRoomDoor(p));
        p.Go(MusicRoomDoor(p));
        Assert.Equal(MusicRoom, p.Room);
        p.Do("Turn on", "TV");
        p.Go("door");
        Assert.Equal(Landing, p.Room);

        // the steel security door is open already, and Open would ask for its code
        if (p.World.Version == 0)
        {
            p.Do("Open", MiddleDoor(p));
        }

        p.Go(MiddleDoor(p));
        Assert.Equal(Hallway, p.Room);
        p.Go("stairs");
        Assert.Equal(TentacleLanding, p.Room);
        p.Do("Give", "bowl of wax fruit", "Green Tentacle", until: () => !p.Carries("bowl of wax fruit"));
        p.Do("Give", "fruit drinks", "Green Tentacle", until: () => !p.Carries("fruit drinks"));
        p.Go("stairs[1]");
        Assert.Equal(TopHallway, p.Room);

        p.Do("Open", "door[1]");
        p.Go("door[1]");
        Assert.Equal(FredsRoom, p.Room);
        p.Do("Pick up", "dime", until: () => p.Carries("dime"));
        p.Go("ladder");
        Assert.Equal(TentaclesRoom, p.Room);
        p.Do("Pick up", "record", until: () => p.Carries("record"));
        p.Do("Pick up", "yellow key", until: () => p.Carries("yellow key"));
    }

    // the landing's middle door, and the music room's (the C64's landing has three plain doors; on
    // DOS the middle one is the steel security door, which without the copy protection is always
    // open, and whose far side only version 2 names so)
    private static string MiddleDoor(Player p) => p.World.Version == 0 ? "door[2]" : "steel security door";

    private static string MusicRoomDoor(Player p) => p.World.Version == 0 ? "door[3]" : "door[2]";

    /// <summary>
    /// The old rusty key: Syd brings the record down to the music room and Dave the blank tape up;
    /// the tape in the cassette recorder and the record on the Victrola, the recorder takes the
    /// tentacle mating calls down. In the living room the tape played breaks the windows and brings
    /// the chandelier down, and the old rusty key hung on it can be picked up.
    /// </summary>
    private static void TheOldRustyKey(Player p)
    {
        p.Go("hatch");
        Assert.Equal(FredsRoom, p.Room);
        p.Go("door");
        Assert.Equal(TopHallway, p.Room);
        p.Go("stairs");
        Assert.Equal(TentacleLanding, p.Room);
        p.Go("stairs[2]");
        Assert.Equal(Hallway, p.Room);
        p.Go(p.World.Version == 2 ? "steel security door" : "door[1]");
        Assert.Equal(Landing, p.Room);
        p.Go(MusicRoomDoor(p));
        Assert.Equal(MusicRoom, p.Room);
        p.Do("Use", "record", "Victrola", until: () => !p.Carries("record"));

        p.NewKid("Dave");
        p.Go("door[1]");
        Assert.Equal(Hall, p.Room);
        p.Go("stairs");
        Assert.Equal(Landing, p.Room);
        p.Go(MusicRoomDoor(p));
        Assert.Equal(MusicRoom, p.Room);
        p.Do("Use", "cassette tape", "cassette recorder", until: () => !p.Carries("cassette tape"));
        p.Do("Turn on", "cassette recorder");
        p.Do("Turn on", "Victrola");
        p.Wait(TimeSpan.FromSeconds(10));
        p.Do("Turn off", "cassette recorder");
        p.Do("Pick up", "cassette tape", until: () => p.Carries("cassette tape"));

        p.Go("door");
        Assert.Equal(Landing, p.Room);
        p.Go("stairs");
        Assert.Equal(Hall, p.Room);
        p.Go("door[3]");
        Assert.Equal(LivingRoom, p.Room);
        p.Do("Use", "cassette tape", "cassette player", until: () => !p.Carries("cassette tape"));
        p.Do("Turn on", "cassette player");
        p.Wait(TimeSpan.FromSeconds(10));
        p.Do("Pick up", "old rusty key", until: () => p.Carries("old rusty key"));
    }

    /// <summary>
    /// Ted's room and the plant: Syd pumps iron on the Hunk-O-Matic, strong enough now for the
    /// grating and the garage door; next door, the jar of radioactive pool water makes the
    /// man-eating plant grow to the hatch, the Pepsi settles its stomach, and the paint remover
    /// shows the door under the paint blotch. Up the plant is the telescope room.
    /// </summary>
    private static void TedsRoomAndThePlant(Player p)
    {
        p.NewKid(Second(p));
        p.Go("door");
        Assert.Equal(Landing, p.Room);
        p.Go(MiddleDoor(p));
        Assert.Equal(Hallway, p.Room);
        p.Go("stairs");
        Assert.Equal(TentacleLanding, p.Room);
        p.Go("stairs[1]");
        Assert.Equal(TopHallway, p.Room);
        p.Do("Open", "door[4]");
        p.Go("door[4]");
        Assert.Equal(TedsRoom, p.Room);
        p.Do("Use", "Hunk-O-Matic Machine (TM)");
        p.Go("door[1]");
        Assert.Equal(TopHallway, p.Room);
        p.Do("Open", "door[5]");
        p.Go("door[5]");
        Assert.Equal(PlantRoom, p.Room);
        p.Do("Use", "jar of water", "Man-eating plant", until: () => !p.Carries("jar of water"));
        p.Do("Use", "can of Pepsi", "Man-eating plant", until: () => !p.Carries("can of Pepsi"));
        p.Do("Use", "paint remover", "paint blotch", until: () => !p.Carries("paint remover"));

        // the plant, grown and fed, can be climbed to the telescope room: on the C64 the plant's
        // stem leads up, on DOS the hatch it reaches
        p.Go(p.World.Version == 0 ? "Man-eating plant" : "Hatch");
        Assert.Equal(TelescopeRoom, p.Room);
        p.Go("hole");
        Assert.Equal(PlantRoom, p.Room);
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
        Upstairs(p);
        Checkpoint(p, folder, 33);
        TheOldRustyKey(p);
        TedsRoomAndThePlant(p);
        Checkpoint(p, folder, 34);
        Assert.Empty(p.Unhandled);
    }
}
