using Maniacal.Scumm.Inspection;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Storage;

namespace Maniacal.Scumm.Tests;

/// <summary>
/// The inspection API as a consumer of the package uses it, its answers pinned to what the
/// player's <c>--room</c>, <c>--costume</c>, <c>--script</c> and <c>--view</c> printed before they
/// were built on it.
/// </summary>
public class InspectionTests
{
    private static GameInspector? Open(string folder)
        => GameFolders.Find(folder) is { } path ? new GameInspector(ResourceLoader.Open(new DirectoryGameContainer(path))) : null;

    /// <summary>
    /// Every number is asked, as a program that lists a release's rooms, scripts and costumes asks
    /// them, and none throws: the index's empty slots (entries in room 0: number 0 in the 1988
    /// release and on the C64, scripts 176 to 199 in the 1987 release, whose script 0 is a real
    /// one) and the entries pointing at nothing the game loads (the 1988 release's script 134, the
    /// 1987 release's 50, 72 and 162 and its costume 0, which holds no costume; the C64's scripts
    /// 67, 152, 153 and 159) answer null. Found by a program written against the package.
    /// </summary>
    [Theory]
    [InlineData(WalkthroughTests.V2, 53, 169, new[] { 0, 134 })]
    [InlineData(WalkthroughTests.V1, 52, 173, new[] { 50, 72, 162, 176, 199 })]
    [InlineData(WalkthroughTests.C64, 52, 155, new[] { 0, 67, 152, 153, 159 })]
    public void Every_number_is_answered_without_throwing(string folder, int rooms, int scripts, int[] noScripts)
    {
        if (Open(folder) is not { } inspector)
        {
            return;
        }

        Assert.Equal(rooms, Enumerable.Range(0, 100).Count(n => inspector.Room(n) is not null));
        Assert.Equal(scripts, Enumerable.Range(0, 256).Count(n => inspector.Script(n) is not null));

        // the lists say the same as asking every number
        Assert.Equal(Enumerable.Range(0, 100).Where(n => inspector.Room(n) is not null), inspector.Rooms);
        Assert.Equal(Enumerable.Range(0, 256).Where(n => inspector.Script(n) is not null), inspector.Scripts);
        Assert.Equal(Enumerable.Range(0, 100).Where(n => inspector.Costume(n) is not null), inspector.Costumes);
        Assert.All(Enumerable.Range(0, 100), n => inspector.RoomScripts(n));
        Assert.All(Enumerable.Range(0, 100), n => inspector.Costume(n));
        Assert.All(noScripts, n => Assert.Null(inspector.Script(n)));
        Assert.Null(inspector.Room(0));
    }

    [Fact]
    public void A_room_comes_with_its_picture_mask_and_objects()
    {
        if (Open(WalkthroughTests.V2) is not { } inspector)
        {
            return;
        }

        var room = inspector.Room(12)!;

        Assert.Equal((640, 128), (room.Width, room.Height));
        Assert.Equal((640, 128), (room.Pixels.Width, room.Pixels.Height));
        Assert.Equal((640, 128), (room.Mask.Width, room.Mask.Height));
        Assert.Equal(7, room.Objects.Count);
        var plant = room.Objects[0];
        Assert.Equal((386, "lovely plant"), (plant.Number, plant.Name));
        Assert.Equal((104, 24, 32, 88), (plant.X, plant.Y, plant.Width, plant.Height));
        Assert.Equal((112, 112), (plant.WalkX, plant.WalkY));
        Assert.Null(inspector.Room(99));
    }

    [Fact]
    public void The_objects_drawn_depend_on_which_are_asked_for()
    {
        if (Open(WalkthroughTests.V2) is not { } inspector)
        {
            return;
        }

        var none = inspector.Room(12, RoomObjects.None)!.Pixels.Pixels;
        var all = inspector.Room(12, RoomObjects.All)!.Pixels.Pixels;

        Assert.NotEqual(none, all);
    }

    [Theory]
    [InlineData(WalkthroughTests.V1, 0x57, 24)]
    [InlineData(WalkthroughTests.C64, null, 20)]
    public void A_costume_stands_four_ways_and_walks(string folder, int? format, int animations)
    {
        if (Open(folder) is not { } inspector)
        {
            return;
        }

        var costume = inspector.Costume(3)!;

        Assert.Equal(format, costume.Format);
        Assert.Equal(animations, costume.Animations);
        Assert.Equal(4, costume.Standing.Count);
        Assert.Equal(4, costume.Walking.Count);
        Assert.Contains(costume.Standing[0].Pixels.Pixels.ToArray(), p => p != CostumeFigures.Transparent);
        Assert.Null(inspector.Costume(99));
    }

    [Fact]
    public void A_script_is_listed_with_the_bytes_execution_never_reaches()
    {
        if (Open(WalkthroughTests.V2) is not { } inspector)
        {
            return;
        }

        var listing = inspector.Script(59)!;

        Assert.StartsWith("Script 59 (", listing.Title);
        Assert.Null(listing.Error);
        Assert.Contains(listing.Lines, l => l is { Instruction: null, Offset: 0x00F5, Length: 20 });
        Assert.Null(inspector.Script(999));
    }

    [Fact]
    public void A_rooms_scripts_start_with_its_entry()
    {
        if (Open(WalkthroughTests.C64) is not { } inspector)
        {
            return;
        }

        var entry = inspector.RoomScripts(1)![0];

        Assert.Equal("Room 1 entry", entry.Title);
        Assert.Equal(new ScriptLine { Offset = 0, Length = 2, Instruction = "lights(2)" }, entry.Lines[0]);
    }

    [Fact]
    public void A_view_stands_figures_in_the_room_and_says_where()
    {
        if (Open(WalkthroughTests.C64) is not { } inspector)
        {
            return;
        }

        var view = inspector.View(1, 300, [new StandingActor(1, 30, 60, Facing.Left), new StandingActor(3, 60, 62, Facing.Front), new StandingActor(5, 90, 40, Facing.Back)])!;

        Assert.Equal((320, 200), (view.Screen.Width, view.Screen.Height));
        Assert.Equal((296, 136), (view.CameraX, view.ViewLeft));
        Assert.Equal([5, 1, 3], view.Actors.Select(a => a.Costume));
        Assert.Equal(4, view.Actors.Single(a => a.Costume == 3).Box);
        Assert.Throws<KeyNotFoundException>(() => inspector.View(12, null, [new StandingActor(77, 10, 10, Facing.Front)]));
        Assert.Null(inspector.View(99));
    }

    // the verbs by number as the DOS releases, their demos, the Atari ST and the Amiga name them;
    // the C64's and the NES's differ
    private const string DosVerbs = "1 Open, 2 Close, 3 Give, 4 Turn on, 5 Turn off, 6 Fix, 7 New kid, 8 Unlock, 9 Push, 10 Pull, 11 Use, 12 Read, 13 Walk to, 14 Pick up, 15 What is";
    private const string C64Verbs = "1 Open, 2 Close, 3 Give, 4 Turn on, 5 Turn off, 6 Fix, 7 New Kid, 8 Unlock, 9 Push, 10 Pull, 11 Use, 12 Read, 13 Walk to, 14 Pick up, 15 What is";
    private const string NesVerbs = "1 Open, 2 Close, 3 Give, 4 Turn On, 5 Turn Off, 7 New Kid, 9 Push, 10 Pull, 11 Use, 12 Read, 13 Go To, 14 Get";

    private static string Named(GameInspector inspector) => string.Join(", ", inspector.Verbs.Select(v => $"{v} {inspector.VerbName(v)}"));

    /// <summary>
    /// Every release names its verbs, by the numbers objects' verb tables use: the C64's from
    /// ScummVM's table, the others' from the script that sets them up (164), the DOS demos' too,
    /// though they never show them. The NES has no Fix, Unlock or What is, and its own names.
    /// Found by a program written against the package, which could list objects' verbs only as numbers.
    /// </summary>
    [Theory]
    [InlineData(WalkthroughTests.V2, DosVerbs)]
    [InlineData(WalkthroughTests.V1, DosVerbs)]
    [InlineData(WalkthroughTests.AtariSt, DosVerbs)]
    [InlineData(WalkthroughTests.Amiga, DosVerbs)]
    [InlineData(WalkthroughTests.V1Demo, DosVerbs)]
    [InlineData(WalkthroughTests.V2Demo, DosVerbs)]
    [InlineData(WalkthroughTests.C64, C64Verbs)]
    [InlineData(WalkthroughTests.C64Demo, C64Verbs)]
    [InlineData(WalkthroughTests.Nes, NesVerbs)]
    public void Verbs_have_the_names_their_release_gives_them(string folder, string named)
    {
        if (GameFolders.Find(folder) is not { } path)
        {
            return;
        }

        var inspector = ManiacMansion.Open(path).Inspect();
        Assert.Equal(named, Named(inspector));
        Assert.Null(inspector.VerbName(0));
        Assert.Null(inspector.VerbName(255));
    }

    /// <summary>
    /// The names are the ones the game shows once it is played to the driveway, its verbs set up
    /// by its own scripts (or, on the C64, its engine), the fillers that keep them a width aside.
    /// </summary>
    [Theory]
    [InlineData(WalkthroughTests.V2)]
    [InlineData(WalkthroughTests.C64)]
    [InlineData(WalkthroughTests.Nes)]
    public void Verb_names_are_those_the_game_shows(string folder)
    {
        if (WalkthroughTests.Start(folder) is not { } player || GameFolders.Find(folder) is not { } path)
        {
            return;
        }

        var inspector = ManiacMansion.Open(path).Inspect();
        var shown = player.World.Verbs.Slots
            .Where(v => v is not null && v.Id != 0)
            .ToDictionary(v => v.Id, v => System.Text.Encoding.Latin1.GetString(v.Name).TrimEnd('\0').Replace('@', ' ').Trim());
        Assert.Equal(shown.Keys.Order(), inspector.Verbs);
        Assert.All(shown, v => Assert.Equal(v.Value, inspector.VerbName(v.Key)));
    }

    /// <summary>
    /// The numbers in objects' verb tables that have no name are none a player picks: 255 matches
    /// any verb, 253 is code the scripts run on an object, 16 locks a door or padlock, and the
    /// NES's objects still answer the Fix and Unlock it has no verbs for.
    /// </summary>
    [Theory]
    [InlineData(WalkthroughTests.V2, new[] { 16, 253, 255 })]
    [InlineData(WalkthroughTests.V1, new[] { 16, 255 })]
    [InlineData(WalkthroughTests.C64, new[] { 16 })]
    [InlineData(WalkthroughTests.Nes, new[] { 6, 8, 16, 253, 255 })]
    public void The_unnamed_verbs_in_objects_tables_are_none_a_player_picks(string folder, int[] unnamed)
    {
        if (GameFolders.Find(folder) is not { } path)
        {
            return;
        }

        var inspector = ManiacMansion.Open(path).Inspect();
        var answered = inspector.Rooms.SelectMany(r => inspector.Room(r)!.Objects).SelectMany(o => o.Verbs).Select(v => (int)v.Verb).Distinct();
        Assert.Equal(unnamed, answered.Where(v => inspector.VerbName(v) is null).Order());
    }
}
