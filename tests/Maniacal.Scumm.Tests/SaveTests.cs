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

    /// <summary>
    /// A C64 state: each actor's <c>Actor_v0</c> fields in place of version 2's step fields, and
    /// the engine's own after version 2's, which a read before version 124 finds after the music
    /// engine's block by its size.
    /// </summary>
    [Theory]
    [InlineData(SaveFile.CompatibleVersion)]
    [InlineData(SaveFile.CurrentVersion)]
    public void A_c64_state_reads_back_as_it_was_written(int version)
    {
        var state = new ScummState(256, c64: true) { CurrentMode = 3, CurrentLights = 11, ActiveVerb = 13, CmdObject = 0x109, WalkToObjectState = 2 };
        state.SoundEngineData = [1, 2, 3];
        state.Actors[3].CostCommand = 6;
        state.Actors[3].MiscFlags = 0x40;
        state.Actors[3].AnimFrameRepeat = -1;
        state.Actors[3].NewWalkToX = 7;
        state.Actors[3].WalkboxQueue[1] = 4;
        state.Actors[3].WalkboxQueueIndex = 2;

        var file = SaveFile.Write(state, "c64", TimeSpan.Zero, new DateTime(2026, 10, 24), version);
        var (_, read) = SaveFile.Read(file, 256, c64: true);

        Assert.Equal((3, 11, 13, 0x109, 2), (read.CurrentMode, read.CurrentLights, read.ActiveVerb, read.CmdObject, read.WalkToObjectState));
        Assert.Equal([1, 2, 3], read.SoundEngineData);
        var dave = read.Actors[3];
        Assert.Equal((6, 0x40, -1, 7, 4, 2), (dave.CostCommand, dave.MiscFlags, dave.AnimFrameRepeat, dave.NewWalkToX, (int)dave.WalkboxQueue[1], dave.WalkboxQueueIndex));
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
        var (info, state) = SaveFile.Read(file, world.Engine.ObjectOwners.Length, world.Version == 0);
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

    /// <summary>
    /// A C64 game saved and restored plays on as the original: the room, variables and actors are
    /// the same, the kid's clip starts again as the save's load has it, and the screens match step
    /// for step but for the sentence line, which a load leaves clear.
    /// </summary>
    [Fact]
    public void A_c64_game_loaded_from_its_save_plays_on_as_the_original()
    {
        if (GameFolders.Find(C64Folder) is not { } path || GameWorldTests.AfterTheC64Intro() is not { } world)
        {
            return;
        }

        world.Click(60, 130);
        Steps(world, 60);
        var file = world.Save("in the street", DateTime.Now);
        var (loaded, _) = Restored(path, file);

        Assert.Equal(world.CurrentRoom?.Number, loaded.CurrentRoom?.Number);
        Assert.Equal(world.Engine.Variables, loaded.Engine.Variables);
        Assert.Equal(world.Engine.BitVariables, loaded.Engine.BitVariables);
        Assert.Equal((world.CurrentMode, world.CurrentLights, world.ActiveVerb), (loaded.CurrentMode, loaded.CurrentLights, loaded.ActiveVerb));
        Assert.Equal(world.Actors.Select(a => (a.Room, a.X, a.Y, a.Facing, a.Moving, a.Name)), loaded.Actors.Select(a => (a.Room, a.X, a.Y, a.Facing, a.Moving, a.Name)));

        int roomStart = MessageLine.Height * 320, sentenceStart = world.Verbs.Top * 320, sentenceEnd = (world.Verbs.Top + 9) * 320;
        for (int step = 0; step < 200; step++)
        {
            world.Step();
            loaded.Step();
            var a = world.Screen().Pixels;
            var b = loaded.Screen().Pixels;
            Assert.True(a.AsSpan(roomStart, sentenceStart - roomStart).SequenceEqual(b.AsSpan(roomStart, sentenceStart - roomStart))
                && a.AsSpan(sentenceEnd).SequenceEqual(b.AsSpan(sentenceEnd)), $"the screens differ at step {step}");
        }
    }

    /// <summary>
    /// A C64 game saved in the intro, while the camera follows the meteor, loads with the room's
    /// own camera limits (the C64 has no variables for them), so the camera waits, as in the game
    /// played on, until the meteor passes its trigger column, then follows it.
    /// </summary>
    [Fact]
    public void A_c64_camera_follows_on_after_a_load()
    {
        if (GameFolders.Find(C64Folder) is not { } path)
        {
            return;
        }

        var world = Fresh(path);
        world.Boot();
        for (int i = 0; i < 280; i++)
        {
            switch (i)
            {
                case 100:
                    world.Click(76, 121);
                    break;
                case 150:
                    world.Click(236, 121);
                    break;
                case 200:
                    world.Click(290, 69);
                    break;
            }

            world.Step();
        }

        var loaded = Fresh(path);
        loaded.Load(world.Save("the meteor", DateTime.Now));
        Assert.Equal((160, world.CurrentRoom!.Width - 160), (loaded.Camera.MinX, loaded.Camera.MaxX));

        var played = new List<int>();
        var reloaded = new List<int>();
        for (int i = 0; i < 40; i++)
        {
            world.Step();
            loaded.Step();
            played.Add(world.Camera.X);
            reloaded.Add(loaded.Camera.X);
        }

        // the loaded meteor stalls a frame, as ScummVM's does: its unsaved step position starts at 0
        Assert.Equal(played.Take(25), reloaded.Take(25));
        Assert.Equal(played[^2], reloaded[^1]);
    }

    /// <summary>
    /// The C64's save screen (F5, script 2): Save this game, and the save is in the store; in a
    /// fresh game, Load a saved game, and the save screen ends where the kid was when it was saved.
    /// </summary>
    [Fact]
    public void The_c64_save_screen_saves_and_loads()
    {
        if (GameWorldTests.AfterTheC64Intro() is not { } world)
        {
            return;
        }

        var store = new Daventry.Core.Storage.MemorySaveStore();
        world.SaveStore = store;
        world.Click(60, 130);
        Steps(world, 60);
        Assert.Equal((7, 64), (world.Actors[3].X, world.Actors[3].Y));

        // the save screen's buttons, objects 442 and 443 at the left of its lines
        UseC64SaveScreen(world, 24, 117);
        Assert.Equal(["maniac.s01"], store.List("maniac."));
        Assert.Equal("The game was saved.", world.Messages.Plain().Trim());

        var fresh = GameWorldTests.AfterTheC64Intro()!;
        fresh.SaveStore = store;
        UseC64SaveScreen(fresh, 48, 125);
        Steps(fresh, 60);
        Assert.Equal(44, fresh.CurrentRoom?.Number);
        Assert.Equal(GameWorld.ModeNormal, fresh.CurrentMode);
        Assert.Equal((7, 64), (fresh.Actors[3].X, fresh.Actors[3].Y));
        Assert.Empty(fresh.Unhandled);
    }

    /// <summary>F5, then a button of the C64's save screen, at a screen point.</summary>
    private static void UseC64SaveScreen(GameWorld world, int x, int y)
    {
        world.PressKey(GameWorld.KeyF1 + 4);
        Steps(world, 30);
        Assert.Equal(GameWorld.SaveRoom, world.CurrentRoom?.Number);
        world.Click(x, y);
        Steps(world, 200);
    }

    private const string C64Folder = "maniac_mansion_c64";

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
