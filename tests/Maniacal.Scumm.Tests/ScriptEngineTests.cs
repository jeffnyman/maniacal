using Daventry.Core.Storage;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Scripts;

namespace Maniacal.Scumm.Tests;

public class ScriptEngineTests
{
    // a few version 2 encodings, for hand-made scripts
    private static byte[] Move(int variable, int value) => [0x1A, (byte)variable, (byte)value, (byte)(value >> 8)];

    private static byte[] Add(int variable, int value) => [0x5A, (byte)variable, (byte)value, (byte)(value >> 8)];

    private static byte[] Delay(int ticks) => [0x2E, (byte)(0xFFFFFF - ticks), (byte)((0xFFFFFF - ticks) >> 8), (byte)((0xFFFFFF - ticks) >> 16)];

    private static byte[] StartScript(int number) => [0x42, (byte)number];

    private static byte[] StopScript(int number) => [0x62, (byte)number];

    private const byte BreakHere = 0x80, Stop = 0x00, Cutscene = 0x40, EndCutscene = 0xC0;

    /// <summary>An engine over hand-made global scripts, each given without its 4-byte header.</summary>
    private static (ScriptEngine Engine, RecordingHost Host) Engine(params (int Number, byte[] Code)[] scripts)
    {
        var table = scripts.ToDictionary(s => s.Number, s => (ReadOnlyMemory<byte>)new byte[ScriptEngine.ScriptHeaderSize].Concat(s.Code).ToArray());
        var host = new RecordingHost();
        var engine = new ScriptEngine(2, n => table.TryGetValue(n, out var code) ? code : null, host, new byte[600], new byte[600]);
        return (engine, host);
    }

    private static byte[] Code(params byte[][] parts) => [.. parts.SelectMany(p => p)];

    [Fact]
    public void Arithmetic_and_conditions()
    {
        // Var[60] = 5; Var[60] += 3; if Var[60] == 8 then Var[61] = 1 (isEqual jumps over it otherwise)
        var (engine, _) = Engine((10, Code(Move(60, 5), Add(60, 3), [0x48, 60, 8, 0, 4, 0], Move(61, 1), [Stop])));

        engine.RunScript(10);

        Assert.Equal(8, engine.Variables[60]);
        Assert.Equal(1, engine.Variables[61]);
        Assert.False(engine.IsScriptRunning(10));
    }

    [Fact]
    public void A_failed_condition_jumps()
    {
        var (engine, _) = Engine((10, Code(Move(60, 7), [0x48, 60, 8, 0, 4, 0], Move(61, 1), [Stop])));

        engine.RunScript(10);

        Assert.Equal(0, engine.Variables[61]);
    }

    [Fact]
    public void Delays_count_ticks_and_the_script_carries_on_after()
    {
        var (engine, _) = Engine((10, Code(Move(60, 1), Delay(10), Move(60, 2), [Stop])));

        engine.RunScript(10);
        Assert.Equal(1, engine.Variables[60]);
        Assert.True(engine.IsScriptRunning(10));

        for (int step = 0; step < 2; step++)
        {
            engine.DecreaseScriptDelay(4);
            engine.RunAllScripts();
        }

        Assert.Equal(1, engine.Variables[60]);

        // 12 ticks have passed; the delay ends once it is below zero
        engine.DecreaseScriptDelay(4);
        engine.RunAllScripts();
        Assert.Equal(2, engine.Variables[60]);
        Assert.False(engine.IsScriptRunning(10));
    }

    [Fact]
    public void A_started_script_runs_at_once_then_its_caller_carries_on()
    {
        var (engine, _) = Engine(
            (10, Code(StartScript(11), Move(60, 1), [Stop])),
            (11, Code(Move(61, 1), [BreakHere], Move(62, 1), [Stop])));

        engine.RunScript(10);

        Assert.Equal(1, engine.Variables[61]);
        Assert.Equal(1, engine.Variables[60]);
        Assert.Equal(0, engine.Variables[62]);
        Assert.True(engine.IsScriptRunning(11));

        engine.RunAllScripts();
        Assert.Equal(1, engine.Variables[62]);
    }

    [Fact]
    public void Stop_script_0_stops_the_running_script()
    {
        var (engine, _) = Engine((10, Code(StopScript(0), Move(60, 1), [Stop])));

        engine.RunScript(10);

        Assert.Equal(0, engine.Variables[60]);
        Assert.False(engine.IsScriptRunning(10));
    }

    [Fact]
    public void Bit_variables_share_the_variable_array()
    {
        // setBitVar(Bit[0x32 + 1] = 1): bit 51 is bit 3 of variable 3; then getBitVar into Var[60]
        var (engine, _) = Engine((10, Code([0x1B, 0x32, 0x00, 1, 1], [0x31, 60, 0x32, 0x00, 1], [Stop])));

        engine.RunScript(10);

        Assert.Equal(1 << 3, engine.Variables[3]);
        Assert.Equal(1, engine.Variables[60]);
    }

    [Fact]
    public void Variables_14_to_16_are_read_through()
    {
        var (engine, _) = Engine();
        engine.Variables[15] = 100;
        engine.Variables[100] = 42;

        Assert.Equal(42, engine.ReadVar(15));
        Assert.Equal(100, engine.Variables[15]);
    }

    [Fact]
    public void The_cutscene_exit_key_is_remapped_to_escape()
    {
        var (engine, _) = Engine();

        engine.WriteVar(ScriptEngine.VarCutsceneExitKey, 4);

        Assert.Equal(27, engine.Variables[ScriptEngine.VarCutsceneExitKey]);
    }

    [Fact]
    public void A_cutscene_freezes_the_other_scripts_until_it_ends()
    {
        var (engine, _) = Engine(
            (10, Code([BreakHere], Add(60, 1), [BreakHere], Add(60, 1), [Stop])),
            (11, Code([Cutscene, BreakHere, BreakHere, EndCutscene, Stop])));

        engine.RunScript(10);
        engine.RunScript(11);
        engine.RunAllScripts();
        Assert.Equal(0, engine.Variables[60]);
        Assert.False(engine.CursorOn);

        engine.RunAllScripts();
        engine.RunAllScripts();
        Assert.Equal(1, engine.Variables[60]);
    }

    [Fact]
    public void Aborting_a_cutscene_takes_the_override_jump()
    {
        // beginOverride is followed by a jump over the cutscene's body to Var[61] = 1 (variables
        // 20 and 21 would not do: 21 is the cursor state, which endCutscene restores)
        byte[] body = Code(Move(60, 1), [BreakHere], Move(60, 2), [BreakHere]);
        var (engine, _) = Engine((10, Code([Cutscene, 0x58, 0x18, (byte)body.Length, 0], body, Move(61, 1), [EndCutscene, Stop])));

        engine.RunScript(10);
        Assert.Equal(1, engine.Variables[60]);

        engine.AbortCutscene();
        engine.RunAllScripts();

        // the jump skipped the rest of the body; endCutscene then clears the override variable again
        Assert.Equal(1, engine.Variables[60]);
        Assert.Equal(1, engine.Variables[61]);
        Assert.Equal(0, engine.Variables[ScriptEngine.VarOverride]);
    }

    [Fact]
    public void Object_states_and_owners()
    {
        // setStateLocked(300); ifStateUnlocked(300) (the names say when to jump: not here, it is locked); Var[60] = 1; setOwnerOf(300, 3)
        var (engine, host) = Engine((10, Code([0x27, 0x2C, 0x01], [0x6F, 0x2C, 0x01, 4, 0], Move(60, 1), [0x29, 0x2C, 0x01, 3], [Stop])));

        engine.RunScript(10);

        Assert.Equal(ScriptEngine.StateLocked, engine.GetState(300));
        Assert.Equal(1, engine.Variables[60]);
        Assert.Equal(3, engine.GetOwner(300));
        Assert.Contains(host.Calls, c => c.Contains("setOwnerOf(300, 3)"));
    }

    [Fact]
    public void A_script_that_never_yields_is_stopped()
    {
        // jumpRelative back to itself
        var (engine, _) = Engine((10, [0x18, 0xFD, 0xFF]));

        Assert.Throws<InvalidOperationException>(() => engine.RunScript(10));
    }
}

/// <summary>The game's own boot script, run headless.</summary>
public class BootTests
{
    private const string V1Folder = "maniac_mansion_original_DOS.com";
    private const string V2Folder = "maniac_mansion_enhanced_DOS.com";

    /// <summary>
    /// Script 1 sets Dave as kid 1 and Escape as the cutscene key (written as 4, remapped to 27),
    /// loads the title room 45 and prints the copyright; after a while it is asking the player to
    /// pick two kids. Another script runs alongside: in V2, script 177 makes the kids' portraits
    /// blink; V1 has no blinking, and script 168 waits out a long delay instead.
    /// </summary>
    [Theory]
    [InlineData(V1Folder, 168)]
    [InlineData(V2Folder, 177)]
    public void The_boot_script_reaches_the_kid_selection(string folder, int alongside)
    {
        if (GameFolders.Find(folder) is not { } path)
        {
            return;
        }

        var loader = ResourceLoader.Open(new DirectoryGameContainer(path));
        var host = new RecordingHost();
        var engine = new ScriptEngine(
            loader.Index.Version,
            n => loader.Index.Locate(ResourceType.Script, n).IsPresent ? loader.Load(ResourceType.Script, n) : null,
            host,
            loader.Index.ObjectOwners,
            loader.Index.ObjectStates);

        engine.RunScript(1);
        for (int step = 0; step < 200; step++)
        {
            engine.DecreaseScriptDelay(Math.Max(1, engine.Variables[ScriptEngine.VarTimerNext]));
            engine.RunAllScripts();
        }

        Assert.Equal(3, engine.Variables[ScriptEngine.VarEgo]);
        Assert.Equal(27, engine.Variables[ScriptEngine.VarCutsceneExitKey]);
        Assert.Contains(host.Calls, c => c.EndsWith("loadRoom(45)", StringComparison.Ordinal));
        Assert.Contains(host.Calls, c => c.Contains("Copyrigh", StringComparison.Ordinal));
        Assert.Contains(host.Calls, c => c.Contains("selec", StringComparison.Ordinal) && c.Contains("kids", StringComparison.Ordinal));
        Assert.True(engine.IsScriptRunning(1));
        Assert.True(engine.IsScriptRunning(alongside));
    }
}

/// <summary>Version 0's own script instructions.</summary>
public class ScriptEngineV0Tests
{
    private const byte Stop = 0x00;

    /// <summary>A version 0 engine over hand-made global scripts, each given without its 4-byte header.</summary>
    private static ScriptEngine EngineV0(params (int Number, byte[] Code)[] scripts)
    {
        var table = scripts.ToDictionary(s => s.Number, s => (ReadOnlyMemory<byte>)new byte[ScriptEngine.ScriptHeaderSize].Concat(s.Code).ToArray());
        return new ScriptEngine(0, n => table.TryGetValue(n, out var code) ? code : null, new RecordingHost(), new byte[256], new byte[256]);
    }

    /// <summary>Version 0's bit variables are bytes and bits of their own (o_setBitVar, o_getBitVar).</summary>
    [Fact]
    public void Version_0_bit_variables_are_a_byte_and_a_bit()
    {
        // setBitVar(3, 5, 1); Var[60] = getBitVar(3, 5); Var[61] = getBitVar(3, 4)
        var engine = EngineV0((10, [0x1D, 3, 5, 1, 0x31, 60, 3, 5, 0x31, 61, 3, 4, Stop]));

        engine.RunScript(10);

        Assert.Equal(0x20, engine.BitVariables[3]);
        Assert.Equal((1, 0), (engine.Variables[60], engine.Variables[61]));
        Assert.All(engine.Variables.Take(60), v => Assert.Equal(0, v));
    }

    /// <summary>
    /// Version 0's state and owner instructions can mean the running sentence's object: its number
    /// without the type in the high byte.
    /// </summary>
    [Fact]
    public void Version_0_instructions_can_mean_the_sentences_object()
    {
        // setStateIntrinsicOn(cmdObject); Var[61] = getObjectOwner(0)
        var engine = EngineV0((10, [0xD7, 0x73, 61, 0, Stop]));
        engine.CommandObject = 5;
        engine.ObjectOwners[5] = 7;

        engine.RunScript(10);

        Assert.Equal(ScriptEngine.StateIntrinsic, engine.GetState(5));
        Assert.Equal(7, engine.Variables[61]);
    }

    /// <summary>o_stopCurrentScript stops the script that runs it, there and then.</summary>
    [Fact]
    public void Version_0_scripts_stop_themselves()
    {
        // Var[60] = 1; stopCurrentScript; Var[60] = 2
        var engine = EngineV0((10, [0x1A, 60, 1, 0x05, 0x1A, 60, 2, Stop]));

        engine.RunScript(10);

        Assert.Equal(1, engine.Variables[60]);
        Assert.False(engine.IsScriptRunning(10));
    }
}
