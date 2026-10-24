using Daventry.Core.Storage;
using Maniacal.Scumm.Game;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Saves;

namespace Maniacal.Scumm.Tests;

public class SaveSerializerTests
{
    [Fact]
    public void Fields_outside_the_version_take_no_part()
    {
        var writer = SaveSerializer.ForSaving(100);
        int a = 0x1234, b = 7, c = -2;
        writer.Uint16(ref a);
        writer.Byte(ref b, 101);
        writer.Int16(ref c, 8, 100);
        var bytes = writer.ToArray();

        Assert.Equal([0x34, 0x12, 0xFE, 0xFF], bytes);

        var reader = SaveSerializer.ForLoading(bytes, 100);
        int ra = 0, rb = 99, rc = 0;
        reader.Uint16(ref ra);
        reader.Byte(ref rb, 101);
        reader.Int16(ref rc, 8, 100);
        Assert.Equal((0x1234, 99, -2), (ra, rb, rc));
    }

    [Theory]
    [InlineData(SaveFile.CompatibleVersion)]
    [InlineData(SaveFile.CurrentVersion)]
    public void A_state_reads_back_as_it_was_written(int version)
    {
        var state = new ScummState(780) { CurrentRoom = 44, CameraX = 160, InventoryOffset = 2, FlashlightStrips = 7 };
        state.Variables[0] = 3;
        state.Actors[3].X = 24;
        state.Slots[1].Number = 120;
        state.Resources[(ScummState.TypeVerb, 1)] = new ScummState.Resource([(byte)'P', 0]);
        state.Resources[(ScummState.TypeInventory, 0)] = new ScummState.Resource(new byte[20], 55);
        state.Resources[(ScummState.TypeSound, 58)] = new ScummState.Resource([]);

        var file = SaveFile.Write(state, "test", TimeSpan.FromSeconds(90), new DateTime(2026, 10, 24, 8, 30, 0), version);
        var (info, read) = SaveFile.Read(file, 780);

        Assert.Equal(("test", version, TimeSpan.FromSeconds(90), new DateTime(2026, 10, 24, 8, 30, 0)), (info.Name, info.Version, info.PlayTime, info.Saved));
        Assert.Equal((44, 160, 2, 7), (read.CurrentRoom, read.CameraX, read.InventoryOffset, read.FlashlightStrips));
        Assert.Equal(3, read.Variables[0]);
        Assert.Equal(24, read.Actors[3].X);
        Assert.Equal(120, read.Slots[1].Number);
        Assert.Equal(55, read.Resources[(ScummState.TypeInventory, 0)].Number);
        Assert.Equal([(byte)'P', 0], read.Resources[(ScummState.TypeVerb, 1)].Data);
        Assert.True(read.Resources.ContainsKey((ScummState.TypeSound, 58)));
    }

    [Fact]
    public void Saves_are_gzip_with_a_ScummVM_header()
    {
        var file = SaveFile.Write(new ScummState(780), "x", TimeSpan.Zero, DateTime.Now);

        Assert.Equal([0x1F, 0x8B], file[..2]);
        Assert.Equal(SaveFile.CompatibleVersion, SaveFile.ReadInfo(file).Version);
    }
}

/// <summary>Saving a game and loading it into a fresh world gives the same game.</summary>
public class SaveGameTests
{
    private const string V1Folder = "maniac_mansion_original_DOS.com";
    private const string V2Folder = "maniac_mansion_enhanced_DOS.com";

    private static GameWorld Fresh(string path) => new(ResourceLoader.Open(new DirectoryGameContainer(path)));

    // a fresh world with a save's state, as the save screen restores it, without Load's trip through the save room
    private static (GameWorld World, SaveInfo Info) Restored(string path, byte[] file)
    {
        var world = Fresh(path);
        var (info, state) = SaveFile.Read(file, world.Engine.ObjectOwners.Length);
        world.Restore(state);
        return (world, info);
    }

    [Theory]
    [InlineData(V1Folder, SaveFile.CompatibleVersion)]
    [InlineData(V2Folder, SaveFile.CompatibleVersion)]
    [InlineData(V2Folder, SaveFile.CurrentVersion)]
    public void A_game_loaded_from_its_save_plays_on_as_the_original(string folder, int version)
    {
        if (GameFolders.Find(folder) is not { } path || PlayingTests.SelectedKids(folder) is not { } world)
        {
            return;
        }

        // walk out of the driveway and save on the way
        world.Click(4, 100);
        for (int i = 0; i < 20; i++)
        {
            world.Step();
        }

        var file = world.Save("on the way", DateTime.Now, version);
        var (loaded, info) = Restored(path, file);

        Assert.Equal("on the way", info.Name);
        Assert.Equal(world.CurrentRoom?.Number, loaded.CurrentRoom?.Number);
        Assert.Equal(world.Engine.Variables, loaded.Engine.Variables);
        Assert.Equal(world.Actors.Select(a => (a.Room, a.X, a.Y, a.Facing, a.Moving)), loaded.Actors.Select(a => (a.Room, a.X, a.Y, a.Facing, a.Moving)));

        // the two play on alike: the walk finishes, the room changes, the screens match step for step
        // but for the message and sentence lines, which a load clears as ScummVM's does
        int roomStart = MessageLine.Height * 320, sentenceStart = world.Verbs.Top * 320, sentenceEnd = (world.Verbs.Top + 8) * 320;
        for (int step = 0; step < 400; step++)
        {
            world.Step();
            loaded.Step();
            var a = world.Screen().Pixels;
            var b = loaded.Screen().Pixels;
            Assert.True(a.AsSpan(roomStart, sentenceStart - roomStart).SequenceEqual(b.AsSpan(roomStart, sentenceStart - roomStart))
                && a.AsSpan(sentenceEnd).SequenceEqual(b.AsSpan(sentenceEnd)), $"the screens differ at step {step}");
        }

        Assert.Equal(1, loaded.CurrentRoom?.Number);
    }

    /// <summary>
    /// The game's own save screen (F5): Save this game, Game A, and the save is in the store; in a
    /// fresh game, Load a saved game, Game A, and the kid stands where he was when it was saved.
    /// </summary>
    [Fact]
    public void The_save_screen_saves_and_loads()
    {
        if (PlayingTests.SelectedKids(V2Folder) is not { } world)
        {
            return;
        }

        var store = new Daventry.Core.Storage.MemorySaveStore();
        world.SaveStore = store;
        world.Click(160, 120);
        Steps(world, 60);
        int x = world.Actors[3].X;

        UseSaveScreen(world, 80);
        Assert.Equal(["maniac.s01"], store.List("maniac."));
        Assert.Equal("Game A", SaveFile.ReadInfo(ReadAll(store, "maniac.s01")).Name);

        var fresh = PlayingTests.SelectedKids(V2Folder)!;
        fresh.SaveStore = store;
        Steps(fresh, 60);
        Assert.NotEqual(x, fresh.Actors[3].X);

        UseSaveScreen(fresh, 104);
        Assert.Equal(44, fresh.CurrentRoom?.Number);
        Assert.Equal(x, fresh.Actors[3].X);
    }

    /// <summary>F5, then a button of the save screen (at a screen line), then Game A.</summary>
    private static void UseSaveScreen(GameWorld world, int button)
    {
        world.PressKey(GameWorld.KeyF1 + 4);
        Steps(world, 30);
        Assert.Equal(GameWorld.SaveRoom, world.CurrentRoom?.Number);
        world.Click(228, button);
        Steps(world, 30);
        var gameA = world.Verbs.Slots[world.Verbs.SlotOf(35)];
        world.Click(gameA.X + 4, gameA.Y + 4);
        Steps(world, 60);
    }

    private static void Steps(GameWorld world, int count)
    {
        for (int i = 0; i < count; i++)
        {
            world.Step();
        }
    }

    private static byte[] ReadAll(Daventry.Core.Storage.ISaveStore store, string name)
    {
        using var stream = store.OpenRead(name)!;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    [Fact]
    public void The_inventory_and_object_states_survive_a_save()
    {
        if (GameFolders.Find(V2Folder) is not { } path || PlayingTests.SelectedKids(V2Folder) is not { } world)
        {
            return;
        }

        // the key, given to Dave as picking it up would
        world.Click(4, 100);
        for (int i = 0; i < 300 && world.CurrentRoom?.Number != 1; i++)
        {
            world.Step();
        }

        world.Engine.RunRoomScript(Scripts.ScriptEngine.EntryScriptNumber, (byte[])[0x50, 55, 0, 0x00]);
        Assert.Equal([55], world.Inventory);

        var (loaded, _) = Restored(path, world.Save("key", DateTime.Now));

        Assert.Equal([55], loaded.Inventory);
        Assert.Equal("key", loaded.NameOf(55));
        Assert.Equal(world.Engine.ObjectStates, loaded.Engine.ObjectStates);
        Assert.Equal(world.Engine.ObjectOwners, loaded.Engine.ObjectOwners);
    }

    /// <summary>
    /// A restart in the middle of the game starts it over: the kid's inventory goes, and the game
    /// goes through the same rooms as a fresh boot, step for step, to the kid selection. Only what
    /// the random numbers move, as the portraits' faces, may differ: a restart keeps them going.
    /// </summary>
    [Fact]
    public void A_restart_starts_the_game_over()
    {
        if (GameFolders.Find(V2Folder) is not { } path || PlayingTests.SelectedKids(V2Folder) is not { } world)
        {
            return;
        }

        world.Engine.RunRoomScript(Scripts.ScriptEngine.EntryScriptNumber, (byte[])[0x98]);
        Assert.Empty(world.Inventory);

        var fresh = Fresh(path);
        fresh.Boot();
        for (int step = 0; step < 300; step++)
        {
            world.Step();
            fresh.Step();
            Assert.Equal(fresh.CurrentRoom?.Number, world.CurrentRoom?.Number);
        }

        Assert.Equal(fresh.Engine.Variables[Scripts.ScriptEngine.VarCursorState], world.Engine.Variables[Scripts.ScriptEngine.VarCursorState]);
        Assert.Equal(fresh.Engine.ObjectOwners, world.Engine.ObjectOwners);
    }

    /// <summary>
    /// A load from outside the save screen goes through the save room and back, as ScummVM's does:
    /// the kid's room again, the camera on the kid, and the interface back on.
    /// </summary>
    [Fact]
    public void A_load_goes_through_the_save_room_and_puts_the_camera_on_the_kid()
    {
        if (GameFolders.Find(V2Folder) is not { } path || PlayingTests.SelectedKids(V2Folder) is not { } world)
        {
            return;
        }

        // to the front of the house, then a few steps left, inside the camera's triggers
        world.Click(4, 100);
        for (int i = 0; i < 1000 && (world.CurrentRoom?.Number != 1 || world.Actors[3].Moving != 0); i++)
        {
            world.Step();
        }

        world.Click(100, 120);
        Steps(world, 200);
        var dave = world.Actors[3];
        Assert.NotEqual(Math.Clamp(dave.X * 8, world.Camera.MinX, world.Camera.MaxX), world.Camera.X);

        var loaded = Fresh(path);
        loaded.Load(world.Save("front", DateTime.Now));

        Assert.Equal(1, loaded.CurrentRoom?.Number);
        Assert.Equal(Math.Clamp(dave.X * 8, loaded.Camera.MinX, loaded.Camera.MaxX), loaded.Camera.X);
        Assert.True(loaded.CursorShown);
        Assert.Equal(world.Engine.Variables[Scripts.ScriptEngine.VarCursorState], loaded.Engine.Variables[Scripts.ScriptEngine.VarCursorState]);
        Assert.Equal(world.Engine.Interface, loaded.Engine.Interface);
    }
}
