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
        Hallway = 13, TentacleLanding = 12, TopHallway = 38, FredsRoom = 21, TentaclesRoom = 20, TedsRoom = 25, PlantRoom = 27, TelescopeRoom = 28,
        EdnasRoom = 19, EdnasAttic = 9, Dungeon = 4, EdsRoom = 26, EdnasAtticCloseUp = 34,
        UnderTheHouse = 29, UnderThePool = 2, Garage = 16,
        WiresAttic = 15, Arcade = 18, HighScore = 39, MarkEteersOffice = 41;

    // the doorbell's ring, the same sound in every release
    private const int Doorbell = 39;

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
    /// Ed's package: the doorbell's five minutes run from the first climb to the top floor, and it
    /// rings once Weird Ed is in his room and nobody looks on at the front of the house. The third
    /// kid, left in the driveway, goes round to the mailbox, while Syd waits in Green Tentacle's
    /// room and Dave in the living room, off Ed's way down; when Ed hears the bell ("Oh, the
    /// doorbell!") the third kid picks the package up, and Ed, finding nothing, sends him off and
    /// goes back up, locking the front door behind him.
    /// </summary>
    private static void EdsPackage(Player p)
    {
        string third = p.World.Actors.Skip(1).First(a => a.Room == Driveway).Name;
        p.NewKid(third);
        p.Leave(left: true);
        Assert.Equal(FrontOfTheHouse, p.Room);
        p.Do("Walk to", "flag");
        p.NewKid(Second(p));
        Assert.Equal(TentaclesRoom, p.Room);
        p.WaitToHear("Oh, the doorbell!", TimeSpan.FromMinutes(10));
        p.NewKid(third);
        p.Do("Pick up", "package", until: () => p.Carries("package"));
        p.Wait(TimeSpan.FromMinutes(3));
        p.NewKid(Second(p));
    }

    /// <summary>
    /// Edna's room and her attic: Dave, who carries the old rusty key, walks into Edna's room to be
    /// caught and taken to the dungeon; while she takes him, Syd comes out of the plant room into
    /// hers, takes the small key from the night stand and goes up the ladder to the dark attic,
    /// where he turns the light on and opens the painting on the wall safe. Dave lets himself out
    /// of the dungeon with the old rusty key and goes up to the hall.
    /// </summary>
    private static void EdnasRoomAndAttic(Player p)
    {
        p.NewKid("Dave");
        p.Go("door[1]");
        Assert.Equal(Hall, p.Room);
        p.Go("stairs");
        Assert.Equal(Landing, p.Room);
        p.Go(MiddleDoor(p));
        Assert.Equal(Hallway, p.Room);
        p.Go("stairs");
        Assert.Equal(TentacleLanding, p.Room);
        p.Go("stairs[1]");
        Assert.Equal(TopHallway, p.Room);
        Assert.Equal(EdnasRoom, p.World.Actors.First(a => a.Name == "Nurse Edna").Room);
        p.Do("Open", "door[2]");
        p.Go("door[2]");
        Assert.True(p.Until(() => p.Kid.Room == Dungeon && p.Playable), "Edna never took Dave to the dungeon");

        p.NewKid(Second(p));
        p.Go("door[1]");
        Assert.Equal(TopHallway, p.Room);
        p.Go("door[2]");
        Assert.Equal(EdnasRoom, p.Room);
        p.Do("Pick up", "small key", until: () => p.Carries("small key"));
        p.Go("ladder");
        Assert.Equal(EdnasAttic, p.Room);
        p.Do("Turn on", "light");
        p.Do("Open", "painting");

        p.NewKid("Dave");
        p.Do("Unlock", "door", "old rusty key");
        p.Do("Open", "door");
        p.Go("door");
        Assert.Equal(Cellar, p.Room);
        p.Go("stairs");
        Assert.Equal(Hall, p.Room);
    }

    /// <summary>
    /// Ed's room: Ed, his package not come, no longer answers the doorbell, so a kid walks in to be
    /// caught. Dave lets the third kid in by the front door, who goes up to hide in Ted's room;
    /// Dave walks into Ed's room and is taken to the dungeon, and while Ed takes him the third kid
    /// comes out, takes the hamster and the card key under it, breaks the piggy bank open for two of
    /// its dimes, and hides again before Ed is back. Dave lets himself out again.
    /// </summary>
    private static void TheCardKeyAndTheDimes(Player p, string third)
    {
        p.NewKid("Dave");
        p.Do("Open", "front door");
        p.NewKid(third);
        p.Go("front door");
        Assert.Equal(Hall, p.Room);
        UpToTheTopHallway(p);
        p.Go("door[4]");
        Assert.Equal(TedsRoom, p.Room);

        p.NewKid("Dave");
        UpToTheTopHallway(p);
        p.Do("Open", "door[3]");
        p.Go("door[3]");
        Assert.True(p.Until(() => p.Kid.Room == Dungeon && p.Playable), "Ed never took Dave to the dungeon");

        p.NewKid(third);
        p.Hurried = true;
        p.Go("door[1]");
        Assert.Equal(TopHallway, p.Room);
        p.Go("door[3]");
        Assert.Equal(EdsRoom, p.Room);
        p.Do("Pick up", "hamster", until: () => p.Carries("hamster"));
        p.Do("Pick up", "card key", until: () => p.Carries("card key"));

        // the dimes are picked up by the script that breaks the bank, once it has said so
        p.Do("Open", "piggy bank");
        p.Quiet();
        for (int dime = 0; dime < 2; dime++)
        {
            p.Do("Pick up", "dime[1]", until: () => p.CarriedCount("dime") > dime);
        }

        p.Go("door");
        Assert.Equal(TopHallway, p.Room);
        p.Go("door[4]");
        Assert.Equal(TedsRoom, p.Room);
        p.Hurried = false;

        // Ed locked the dungeon behind him
        p.NewKid("Dave");
        p.Do("Unlock", "door", "old rusty key");
        p.Do("Open", "door");
        p.Go("door");
        Assert.Equal(Cellar, p.Room);
    }

    /// <summary>
    /// The telescope: the third kid climbs the plant to the telescope room, and with a dime in the
    /// coin slot each time turns the telescope right twice, onto Edna's attic, whose light and open
    /// painting will show the number scrawled under the safe: its combination, close up.
    /// </summary>
    private static void TheTelescope(Player p, string third)
    {
        p.NewKid(third);
        p.Go("door[1]");
        Assert.Equal(TopHallway, p.Room);
        p.Go("door[5]");
        Assert.Equal(PlantRoom, p.Room);
        p.Go(p.World.Version == 0 ? "Man-eating plant" : "Hatch");
        Assert.Equal(TelescopeRoom, p.Room);
        for (int turn = 0; turn < 2; turn++)
        {
            int dimes = p.CarriedCount("dime");
            p.Do("Use", "dime", "coin slot", until: () => p.CarriedCount("dime") < dimes);
            p.Do("Push", "right button");
        }
    }

    /// <summary>
    /// The safe: Syd, still in Edna's attic, opens the wall safe on the combination the telescope
    /// showed, and takes the sealed envelope from it.
    /// </summary>
    private static void TheSafe(Player p)
    {
        p.NewKid(Second(p));
        Assert.Equal(EdnasAttic, p.Room);
        p.Keypad("Open", "wall safe", Code(p, dos: 55, c64: 36));
        p.Do("Pick up", "sealed envelope", until: () => p.Carries("sealed envelope"));
    }

    // a code the game chose, as the player read it: four digits from a variable on (another on the C64)
    private static string Code(Player p, int dos, int c64)
    {
        int first = p.World.Version == 0 ? c64 : dos;
        return string.Concat(Enumerable.Range(first, 4).Select(v => p.World.Engine.Variables[v]));
    }

    /// <summary>
    /// The quarter: Syd comes down from the attic into Edna's room and is caught, and Dave, in the
    /// cellar, lets him out of the dungeon with the old rusty key. In the kitchen the sealed envelope
    /// goes into the microwave with a jar of tap water, and steamed open it gives up a quarter
    /// without being torn.
    /// </summary>
    private static void TheQuarter(Player p)
    {
        p.Go("hatch");
        Assert.True(p.Until(() => p.Kid.Room == Dungeon && p.Playable), "Edna never took Syd to the dungeon");
        p.NewKid("Dave");
        Assert.Equal(Cellar, p.Room);
        p.Do("Unlock", "door", "old rusty key");
        p.Do("Open", "door");

        p.NewKid(Second(p));
        p.Go("door");
        Assert.Equal(Cellar, p.Room);
        p.Go("stairs");
        Assert.Equal(Hall, p.Room);
        p.Do("Open", "door[1]");
        p.Go("door[1]");
        Assert.Equal(Kitchen, p.Room);
        p.Do("Open", "microwave oven");
        p.Do("Use", "sealed envelope", "microwave oven", until: () => !p.Carries("sealed envelope"));
        p.Do("Turn on", "faucet");
        p.Do("Use", "glass jar", "faucet", until: () => p.Carries("jar of water"));
        p.Do("Turn off", "faucet");
        p.Do("Use", "jar of water", "microwave oven", until: () => !p.Carries("jar of water"));
        p.Do("Close", "microwave oven");
        p.Do("Turn on", "microwave oven");

        // opened before it is done, the oven stops cooking
        p.Wait(TimeSpan.FromSeconds(10));
        p.Do("Open", "microwave oven");
        p.Do("Pick up", "envelope", until: () => p.Carries("envelope"));
        p.Do("Open", "envelope", until: () => p.Carries("quarter"));
    }

    /// <summary>
    /// The glowing key: Syd, strong from the Hunk-O-Matic, takes the bushes away from the grating
    /// outside and opens it, and goes round to the pool. Dave goes in under the house to the water
    /// valve and drains the pool, its water cooling the reactor, so only for a while: Syd climbs down
    /// for the glowing key and the radio and back up, and Dave lets the water back in. The radio
    /// holds fresh batteries, which go into the flashlight.
    /// </summary>
    private static void TheGlowingKey(Player p)
    {
        p.Go("door[1]");
        Assert.Equal(Hall, p.Room);
        p.Go("front door");
        Assert.Equal(FrontOfTheHouse, p.Room);
        p.Do("Pick up", "bushes[1]");
        p.Do("Open", "grating");

        p.NewKid("Dave");
        p.Go("stairs");
        Assert.Equal(Hall, p.Room);
        p.Go("front door");
        Assert.Equal(FrontOfTheHouse, p.Room);
        p.Go("grating");
        Assert.Equal(UnderTheHouse, p.Room);

        p.NewKid(Second(p));
        p.Go("front door");
        Assert.Equal(Hall, p.Room);
        p.Go("door[1]");
        Assert.Equal(Kitchen, p.Room);
        p.Do("Open", "door[2]");
        p.Go("door[2]");
        Assert.Equal(DiningRoom, p.Room);
        p.Do("Open", "door[2]");
        p.Go("door[2]");
        Assert.Equal(StorageRoom, p.Room);
        p.Do("Open", "door[2]");
        p.Go("door[2]");
        Assert.Equal(Pool, p.Room);

        // the pool drained, Dr. Fred finds his reactor overheating (the cutscene skipped) and sends
        // Purple Tentacle down to the cellar; a kid on the ladder when a cutscene breaks in stops
        // there and cannot walk on (the first point of every way down being the one he was already
        // going to), so Syd goes down after both, with two minutes from the alarm to the meltdown
        p.NewKid("Dave");
        p.Do("Turn on", "water valve");
        p.NewKid(Second(p));
        p.Hear("meltdown", TimeSpan.FromMinutes(1));
        p.Skip();
        p.WaitToHear("no one here", TimeSpan.FromMinutes(1));
        p.Hurried = true;
        p.Go("ladder");
        Assert.Equal(UnderThePool, p.Room);
        p.Do("Pick up", "glowing key", until: () => p.Carries("glowing key"));
        p.Do("Pick up", "radio", until: () => p.Carries("radio"));
        p.Go("ladder");
        Assert.Equal(Pool, p.Room);
        p.NewKid("Dave");
        p.Do("Turn off", "water valve");
        p.Hurried = false;

        p.NewKid(Second(p));
        p.Do("Open", "radio", until: () => p.Carries("batteries"));
        p.Do("Use", "batteries", "flashlight", until: () => !p.Carries("batteries"));
    }

    /// <summary>
    /// The tools: Syd goes through the pool's gate round to the garage, strong enough now to lift
    /// its door, and the yellow key opens the trunk of the car inside, where the tools are.
    /// </summary>
    private static void TheTools(Player p)
    {
        p.Do("Open", "gate");
        p.Go("gate");
        Assert.Equal(Garage, p.Room);
        p.Do("Open", "garage door");
        p.Do("Unlock", "trunk", "yellow key");
        p.Do("Open", "trunk");
        p.Do("Pick up", "tools", until: () => p.Carries("tools"));
    }

    /// <summary>
    /// The wires: Syd takes the tools up to the attic behind the plant room, whose wires are
    /// broken. The third kid comes down to the hall to hold the knobless door open on the gargoyle
    /// for Dave, who goes down to the cellar and turns the house's power off at the circuit
    /// breakers; by the flashlight's light Syd fixes the wires, and Dave turns the power back on
    /// and goes back up before Purple Tentacle comes to see why.
    /// </summary>
    private static void TheWires(Player p, string third)
    {
        p.Go("gate");
        Assert.Equal(Pool, p.Room);
        p.Go("door");
        Assert.Equal(StorageRoom, p.Room);
        p.Go("door[1]");
        Assert.Equal(DiningRoom, p.Room);
        p.Go("door[1]");
        Assert.Equal(Kitchen, p.Room);
        p.Go("door[1]");
        Assert.Equal(Hall, p.Room);
        UpToTheTopHallway(p);
        p.Go("door[5]");
        Assert.Equal(PlantRoom, p.Room);
        p.Do("Open", "door[2]");
        p.Go("door[2]");
        Assert.Equal(WiresAttic, p.Room);

        p.NewKid(third);
        p.Go("hole");
        Assert.Equal(PlantRoom, p.Room);
        p.Go("door[1]");
        Assert.Equal(TopHallway, p.Room);
        DownToTheHall(p);
        p.Do("Push", "gargoyle[2]");

        p.NewKid("Dave");
        p.Go("grate");
        Assert.Equal(FrontOfTheHouse, p.Room);
        p.Go("front door");
        Assert.Equal(Hall, p.Room);
        p.Go("door[2]");
        Assert.Equal(Cellar, p.Room);
        p.Do("Open", "fuse box");
        p.Do("Turn off", "circuit breakers");

        p.NewKid(Second(p));
        p.Do("Turn on", "flashlight");
        p.Do("Use", "tools", "wires[1]", until: () => p.Heard("that should do it"));

        p.NewKid("Dave");
        p.Hurried = true;
        p.Do("Turn on", "circuit breakers");
        p.Go("stairs");
        Assert.Equal(Hall, p.Room);
        p.Hurried = false;
    }

    /// <summary>
    /// The arcade: its games working again, Dr. Fred comes to play Meteor Mess, three minutes on
    /// and once nobody is in the arcade, the hallway before it, nor Edna's or Ed's room; his high
    /// score is the code of the lab's inner door. Syd comes down to the arcade, where the quarter
    /// in Meteor Mess will show the high score.
    /// </summary>
    private static void TheArcade(Player p)
    {
        p.NewKid(Second(p));
        p.WaitToSee(Arcade, TimeSpan.FromMinutes(6));
        p.Go("hatch");
        Assert.Equal(PlantRoom, p.Room);
        p.Go("door[1]");
        Assert.Equal(TopHallway, p.Room);
        p.Go("stairs");
        Assert.Equal(TentacleLanding, p.Room);
        p.Go("stairs[2]");
        Assert.Equal(Hallway, p.Room);
        p.Do("Open", ArcadeDoor(p));
        p.Go(ArcadeDoor(p));
        Assert.Equal(Arcade, p.Room);
    }

    // the arcade's door, the hallway's second but where the middle door's far side is a plain door
    private static string ArcadeDoor(Player p) => p.World.Version == 2 ? "door[2]" : "door[3]";

    /// <summary>
    /// The lab's doors: Syd goes up from the arcade into Edna's room to be caught and taken to the
    /// dungeon (walking in from the cellar, the door's script closes the door behind the kid by a
    /// sentence that stops the script itself, before it takes him through: so in ScummVM too).
    /// There he unlocks both padlocks with the glowing key, opens the outer door, and opens the
    /// inner door on Dr. Fred's high score. Purple Tentacle guards the lab beyond.
    /// </summary>
    private static void TheLabDoors(Player p)
    {
        p.Ready();
        p.Go("door");
        Assert.Equal(Hallway, p.Room);
        p.Go("stairs");
        Assert.Equal(TentacleLanding, p.Room);
        p.Go("stairs[1]");
        Assert.Equal(TopHallway, p.Room);
        p.Do("Open", "door[2]");
        p.Go("door[2]");
        Assert.True(p.Until(() => p.Kid.Room == Dungeon && p.Playable), "Edna never took Syd to the dungeon");
        p.Do("Unlock", "top padlock", "glowing key");
        p.Do("Unlock", "bottom padlock", "glowing key");
        p.Do("Open", "outer door");
        p.Keypad("Open", "inner door", Code(p, dos: 59, c64: 40));
    }

    /// <summary>
    /// The stamps: Dave unlocks the dungeon from the cellar with the old rusty key and Syd comes
    /// out and up to the hall, where the third kid gives him Ed's package and the card key; the
    /// package's uncancelled stamps come off as he opens it.
    /// </summary>
    private static void TheStamps(Player p, string third)
    {
        p.NewKid("Dave");
        p.Go("door[2]");
        Assert.Equal(Cellar, p.Room);
        p.Do("Unlock", "door", "old rusty key");

        p.NewKid(Second(p));
        p.Go("door");
        Assert.Equal(Cellar, p.Room);
        p.Go("stairs");
        Assert.Equal(Hall, p.Room);

        p.NewKid(third);
        p.Do("Give", "package", Second(p), until: () => p.Carries("package", Second(p)));
        p.Do("Give", "card key", Second(p), until: () => p.Carries("card key", Second(p)));

        p.NewKid(Second(p));
        p.Do("Open", "package", until: () => p.Carries("stamps"));
    }

    /// <summary>
    /// The demo tape: Syd takes the blank tape back from the living room's cassette player, puts
    /// it in the music room's cassette recorder and records himself on the piano, then takes it
    /// up to Green Tentacle, who likes the tune and gives up his own demo tape.
    /// </summary>
    private static void TheDemoTape(Player p)
    {
        p.Go("door[3]");
        Assert.Equal(LivingRoom, p.Room);
        p.Do("Pick up", "cassette tape", until: () => p.Carries("cassette tape"));
        p.Go("door[1]");
        Assert.Equal(Hall, p.Room);
        p.Go("stairs");
        Assert.Equal(Landing, p.Room);
        p.Go(MusicRoomDoor(p));
        Assert.Equal(MusicRoom, p.Room);
        p.Do("Use", "cassette tape", "cassette recorder", until: () => !p.Carries("cassette tape"));
        p.Do("Turn on", "cassette recorder");
        p.Do("Use", "piano");
        p.Do("Turn off", "cassette recorder");
        p.Do("Pick up", "cassette tape", until: () => p.Carries("cassette tape"));

        p.Go("door");
        Assert.Equal(Landing, p.Room);
        p.Go(MiddleDoor(p));
        Assert.Equal(Hallway, p.Room);
        p.Go("stairs");
        Assert.Equal(TentacleLanding, p.Room);
        p.Go("stairs[1]");
        Assert.Equal(TopHallway, p.Room);
        p.Go("door[1]");
        Assert.Equal(FredsRoom, p.Room);
        p.Go("ladder");
        Assert.Equal(TentaclesRoom, p.Room);
        p.Do("Give", "cassette tape", "Green Tentacle", until: () => !p.Carries("cassette tape"));
        p.Do("Pick up", "demo tape", until: () => p.Carries("demo tape"));
    }

    /// <summary>
    /// The contract: the demo tape and the stamps go in the envelope, which the typewriter in the
    /// plant room addresses to Mark Eteer, whose ad Syd watched. Into the mailbox with it, the
    /// mailbox closed and its flag up; with nobody looking on at the front of the house the mailman
    /// takes it, Mark Eteer hears it (as a cutscene shows) and some minutes on a contract comes
    /// back, with a ring of the doorbell. Green Tentacle, given the contract, will look after Syd.
    /// </summary>
    private static void TheContract(Player p)
    {
        // the envelope is named for what it holds
        p.Do("Use", "demo tape", "envelope", until: () => !p.Carries("demo tape"));
        p.Do("Use", "stamps", "tape in envelope", until: () => !p.Carries("stamps"));
        p.Go("hatch");
        Assert.Equal(FredsRoom, p.Room);
        p.Go("door");
        Assert.Equal(TopHallway, p.Room);
        p.Go("door[5]");
        Assert.Equal(PlantRoom, p.Room);
        p.Do("Use", "tape in stamped envelope", "typewriter", until: () => p.Heard("publisher's address"));

        p.Go("door[1]");
        Assert.Equal(TopHallway, p.Room);
        DownToTheHall(p);
        p.Go("front door");
        Assert.Equal(FrontOfTheHouse, p.Room);
        p.Do("Open", "mailbox[1]");
        p.Do("Use", "tape in stamped envelope", "mailbox[1]", until: () => !p.Carries("tape in stamped envelope"));
        p.Do("Close", "mailbox[1]");
        p.Do("Pull", "flag");
        p.Go("front door");
        Assert.Equal(Hall, p.Room);

        p.WaitToSee(MarkEteersOffice, TimeSpan.FromMinutes(8));
        p.WaitToHearSound(Doorbell, TimeSpan.FromMinutes(8));
        p.Go("front door");
        Assert.Equal(FrontOfTheHouse, p.Room);
        p.Do("Open", "mailbox[1]");
        p.Do("Pick up", "contract", until: () => p.Carries("contract"));

        p.Go("front door");
        Assert.Equal(Hall, p.Room);
        UpToTheTopHallway(p);
        p.Go("door[1]");
        Assert.Equal(FredsRoom, p.Room);
        p.Go("ladder");
        Assert.Equal(TentaclesRoom, p.Room);
        p.Do("Give", "contract", "Green Tentacle", until: () => !p.Carries("contract"));
        p.WaitToHear("This is GREAT!", TimeSpan.FromSeconds(30));
    }

    /// <summary>From the top hallway down the stairs twice, through the middle door and down to the hall.</summary>
    private static void DownToTheHall(Player p)
    {
        p.Go("stairs");
        Assert.Equal(TentacleLanding, p.Room);
        p.Go("stairs[2]");
        Assert.Equal(Hallway, p.Room);
        p.Go(p.World.Version == 2 ? "steel security door" : "door[1]");
        Assert.Equal(Landing, p.Room);
        p.Go("stairs");
        Assert.Equal(Hall, p.Room);
    }

    /// <summary>From the hall up the stairs, through the middle door and up twice more to the top hallway.</summary>
    private static void UpToTheTopHallway(Player p)
    {
        p.Go("stairs");
        Assert.Equal(Landing, p.Room);
        p.Go(MiddleDoor(p));
        Assert.Equal(Hallway, p.Room);
        p.Go("stairs");
        Assert.Equal(TentacleLanding, p.Room);
        p.Go("stairs[1]");
        Assert.Equal(TopHallway, p.Room);
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
        EdsPackage(p);
        TheOldRustyKey(p);
        TedsRoomAndThePlant(p);
        Checkpoint(p, folder, 34);
        EdnasRoomAndAttic(p);
        Assert.True(p.Carries("package", p.World.Version == 0 ? "Razor" : "Bernard"), "nobody took Ed's package");
        Checkpoint(p, folder, 35);
        TheCardKeyAndTheDimes(p, p.World.Version == 0 ? "Razor" : "Bernard");
        Assert.True(p.Carries("card key", p.World.Version == 0 ? "Razor" : "Bernard"));
        Checkpoint(p, folder, 36);
        TheTelescope(p, p.World.Version == 0 ? "Razor" : "Bernard");
        p.Begin("Use", "really powerful telescope");
        Assert.True(p.Until(() => p.Room == EdnasAtticCloseUp), "the telescope never showed the safe");
        Checkpoint(p, folder, 37);
        TheSafe(p);
        Assert.True(p.Carries("sealed envelope", "Syd"));
        Checkpoint(p, folder, 38);
        TheQuarter(p);
        Assert.True(p.Carries("quarter", "Syd"));
        Checkpoint(p, folder, 39);
        TheGlowingKey(p);
        Assert.True(p.Carries("glowing key", "Syd"));
        Checkpoint(p, folder, 40);
        TheTools(p);
        Assert.True(p.Carries("tools", "Syd"));
        Checkpoint(p, folder, 41);
        TheWires(p, p.World.Version == 0 ? "Razor" : "Bernard");
        Checkpoint(p, folder, 42);
        TheArcade(p);
        p.Begin("Use", "quarter", "Meteor Mess game");
        Assert.True(p.Until(() => p.Room == HighScore), "Meteor Mess never showed its high score");
        Checkpoint(p, folder, 43);
        TheLabDoors(p);
        Assert.True(p.IsOpen("inner door"), "the lab's inner door stays shut");
        Checkpoint(p, folder, 44);
        TheStamps(p, p.World.Version == 0 ? "Razor" : "Bernard");
        Checkpoint(p, folder, 45);
        TheDemoTape(p);
        Checkpoint(p, folder, 46);
        TheContract(p);
        Checkpoint(p, folder, 47);
        Assert.Empty(p.Unhandled);
    }
}
