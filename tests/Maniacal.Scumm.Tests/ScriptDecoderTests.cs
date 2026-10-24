using Daventry.Core.Storage;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Scripts;

namespace Maniacal.Scumm.Tests;

public class ScriptDecoderTests
{
    private const string V1Folder = "maniac_mansion_original_DOS.com";
    private const string V2Folder = "maniac_mansion_enhanced_DOS.com";

    /// <summary>Global script code starts after the 4-byte resource header (<c>_resourceHeaderSize</c>).</summary>
    private const int ScriptHeader = 4;

    [Fact]
    public void Operands_follow_the_opcode_bits()
    {
        var decoder = new ScriptDecoder(2);

        // putActor (0x01): three constants; 0xC1 has its first two operands in variables
        var constants = decoder.Decode([0x01, 5, 30, 20], 0);
        var variables = decoder.Decode([0xC1, 7, 8, 20], 0);

        Assert.Equal("putActor", constants.Name);
        Assert.Equal([Operand.Constant(5), Operand.Constant(30), Operand.Constant(20)], constants.Operands);
        Assert.Equal([Operand.Variable(7), Operand.Variable(8), Operand.Constant(20)], variables.Operands);
        Assert.Equal(4, constants.Length);
    }

    [Fact]
    public void Jumps_count_from_the_end_of_the_instruction()
    {
        // isEqual (0x48): Var[3] == 0x0102 or jump 0x0010 bytes on
        var instruction = new ScriptDecoder(2).Decode([0x00, 0x48, 3, 0x02, 0x01, 0x10, 0x00], 1);

        Assert.Equal(6, instruction.Length);
        Assert.Equal(7 + 0x10, instruction.JumpTarget);
        Assert.Equal([Operand.Variable(3), Operand.Constant(0x0102)], instruction.Operands);
    }

    [Fact]
    public void Messages_end_at_zero_and_keep_their_escapes()
    {
        // print (0x14) to actor 3: "Hi", escape 4 with its byte, then the end
        byte[] code = [0x14, 3, (byte)'H', (byte)'i', 4, 9, 0];

        var instruction = new ScriptDecoder(2).Decode(code, 0);

        Assert.Equal(code.Length, instruction.Length);
        Assert.Equal(new byte[] { (byte)'H', (byte)'i', 4, 9 }, instruction.Operands[1].Text);
    }

    [Theory]
    [InlineData(V1Folder, 1)]
    [InlineData(V2Folder, 2)]
    public void Every_script_of_the_game_decodes(string folder, int version) => CheckEveryScript(folder, version, version == 1 ? 8374 : 8539, version == 1 ? "" : "script 59");

    /// <summary>
    /// Every global script, room entry and exit script and object verb script decodes along every
    /// path execution can take, with jumps landing on instruction boundaries. The only leftover
    /// bytes are 20 after the end of V2 script 59.
    /// </summary>
    private static void CheckEveryScript(string folder, int version, int expectedInstructions, string expectedLeftovers)
    {
        if (GameFolders.Find(folder) is not { } path)
        {
            return;
        }

        var loader = ResourceLoader.Open(new DirectoryGameContainer(path));
        var decoder = new ScriptDecoder(version);
        var problems = new List<string>();
        var leftovers = new List<string>();
        int scripts = 0;
        int instructions = 0;

        void Check(string name, ReadOnlyMemory<byte> code, IEnumerable<int> entries)
        {
            SortedDictionary<int, Instruction> reachable;
            try
            {
                reachable = decoder.DecodeReachable(code, entries);
            }
            catch (InvalidDataException ex)
            {
                problems.Add($"{name}: {ex.Message}");
                return;
            }

            scripts++;
            instructions += reachable.Count;

            // decoding from every entry must agree: no reachable instruction starts inside another
            int covered = 0;
            foreach (var instruction in reachable.Values)
            {
                if (instruction.Offset < covered)
                {
                    problems.Add($"{name}: the instruction at {instruction.Offset:X4} overlaps the one before it");
                }

                covered = Math.Max(covered, instruction.End);
            }

            // after the last reachable instruction there may be an unreachable end: instructions
            // closing with stopObjectCode at the very end, as after chainScript or restart. Anything
            // else is leftover bytes
            if (covered < code.Length && !IsUnreachableEnd(decoder, code, covered))
            {
                leftovers.Add(name);
            }
        }

        for (int number = 0; number < loader.Index.Count(ResourceType.Script); number++)
        {
            var location = loader.Index.Locate(ResourceType.Script, number);
            if (!location.IsPresent || location.Offset == 0 || !LflFile.Exists(new DirectoryGameContainer(path), location.Room))
            {
                continue;
            }

            ReadOnlyMemory<byte>? data;
            try
            {
                data = loader.Load(ResourceType.Script, number);
            }
            catch (InvalidDataException)
            {
                continue; // a stale index entry
            }

            if (data is { } script && IsResourceStart(loader, location))
            {
                Check($"script {number}", script, [ScriptHeader]);
            }
        }

        for (int number = 1; number < loader.Index.Count(ResourceType.Room); number++)
        {
            if (!LflFile.Exists(new DirectoryGameContainer(path), number) || loader.LoadRoom(number) is not { } room)
            {
                continue;
            }

            if (!room.EntryScript.IsEmpty)
            {
                Check($"room {number} entry", room.EntryScript, [0]);
            }

            if (!room.ExitScript.IsEmpty)
            {
                Check($"room {number} exit", room.ExitScript, [0]);
            }

            foreach (var obj in room.Objects.Where(o => o.Verbs.Count > 0))
            {
                Check($"room {number} object {obj.Number}", obj.Code, obj.Verbs.Select(v => v.Offset));
            }
        }

        Assert.True(problems.Count == 0, $"{problems.Count} problems in {scripts} scripts:\n" + string.Join("\n", problems.Take(40)));
        Assert.Equal(604, scripts);
        Assert.Equal(expectedInstructions, instructions);
        Assert.Equal(expectedLeftovers, string.Join(", ", leftovers));
    }

    private static bool IsUnreachableEnd(ScriptDecoder decoder, ReadOnlyMemory<byte> code, int start)
    {
        try
        {
            var tail = decoder.DecodeAll(code, start).ToList();
            return tail.Count > 0 && tail[^1].End == code.Length && tail[^1].Handler == "o5_stopObjectCode";
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    /// <summary>Whether an index entry points at the start of a resource in its room file, not into the middle of one.</summary>
    private static bool IsResourceStart(ResourceLoader loader, ResourceLocation location)
    {
        var file = loader.RoomFile(location.Room).Span;
        for (int position = 0; position + 2 <= file.Length;)
        {
            if (position == location.Offset)
            {
                return true;
            }

            int size = file[position] | (file[position + 1] << 8);
            if (size < 2)
            {
                return false;
            }

            position += size;
        }

        return false;
    }
}
