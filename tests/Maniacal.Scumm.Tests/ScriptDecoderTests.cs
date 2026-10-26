using System.Globalization;
using System.Text.RegularExpressions;
using Maniacal.Scumm.Resources;
using Maniacal.Scumm.Rooms;
using Maniacal.Scumm.Scripts;
using Maniacal.Scumm.Storage;

namespace Maniacal.Scumm.Tests;

public class ScriptDecoderTests
{
    private const string V1Folder = "maniac_mansion_original_DOS.com";
    private const string V2Folder = "maniac_mansion_enhanced_DOS.com";
    private const string C64Folder = "maniac_mansion_c64";

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

    /// <summary>Version 0 reads a word operand as a byte, but a jump still as a word.</summary>
    [Fact]
    public void Version_0_words_are_bytes_but_jumps_are_not()
    {
        var decoder = new ScriptDecoder(0);

        // isEqual (0x48): Var[3] == 2, or jump 0x0010 bytes on; move (0x1A): Var[5] = 200
        var compare = decoder.Decode([0x48, 3, 2, 0x10, 0x00], 0);
        var move = decoder.Decode([0x1A, 5, 200], 0);

        Assert.Equal(5, compare.Length);
        Assert.Equal([Operand.Variable(3), Operand.Constant(2)], compare.Operands);
        Assert.Equal(5 + 0x10, compare.JumpTarget);
        Assert.Equal(5, move.Result);
        Assert.Equal([Operand.Constant(200)], move.Operands);
    }

    /// <summary>
    /// Version 0's state instructions take their object as a byte, or with bit 0x40 none at all:
    /// the object of the running sentence.
    /// </summary>
    [Fact]
    public void Version_0_state_instructions_can_mean_the_sentences_object()
    {
        var decoder = new ScriptDecoder(0);

        var named = decoder.Decode([0x97, 50], 0);
        var sentence = decoder.Decode([0xD7], 0);
        var test = decoder.Decode([0xDF, 0x02, 0x00], 0);

        Assert.Equal("setStateIntrinsicOn", named.Name);
        Assert.Equal([Operand.Constant(50)], named.Operands);
        Assert.Equal([Operand.CommandObject], sentence.Operands);
        Assert.Equal(1, sentence.Length);
        Assert.Equal("ifStateLocked", test.Name);
        Assert.Equal(3 + 2, test.JumpTarget);
    }

    /// <summary>Version 0 text runs to a zero with no escapes; doSentence is a verb and two object bytes.</summary>
    [Fact]
    public void Version_0_text_and_sentences()
    {
        var decoder = new ScriptDecoder(0);

        var print = decoder.Decode([0x0D, (byte)'N', (byte)'o' | 0x80, 4, 0], 0);
        var sentence = decoder.Decode([0x03, 9, 0xFE, 0], 0);

        Assert.Equal(5, print.Length);
        Assert.Equal(new byte[] { (byte)'N', (byte)'o' | 0x80, 4 }, print.Operands[0].Text);
        Assert.Equal("doSentence", sentence.Name);
        Assert.Equal([Operand.Constant(9), Operand.Constant(0xFE), Operand.Constant(0)], sentence.Operands);
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
    [InlineData(V1Folder, 1, 604, 8374, "")]
    [InlineData(V2Folder, 2, 604, 8539, "script 59")]
    [InlineData(C64Folder, 0, 570, 7343, "script 54")]
    public void Every_script_of_the_game_decodes(string folder, int version, int scripts, int instructions, string leftovers)
        => CheckEveryScript(folder, version, scripts, instructions, leftovers);

    /// <summary>
    /// Every global script, room entry and exit script and object verb script decodes along every
    /// path execution can take, with jumps landing on instruction boundaries. The only leftover
    /// bytes are 20 after the end of V2 script 59, which the C64 has too, as its script 54.
    /// </summary>
    private static void CheckEveryScript(string folder, int version, int expectedScripts, int expectedInstructions, string expectedLeftovers)
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
            if (!location.IsPresent || location.Offset == 0 || !LflFile.Exists(loader.Game, location.Room))
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
            if (!LflFile.Exists(loader.Game, number) || loader.LoadRoom(number) is not { } room)
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
        Assert.Equal(expectedScripts, scripts);
        Assert.Equal(expectedInstructions, instructions);
        Assert.Equal(expectedLeftovers, string.Join(", ", leftovers));
    }

    /// <summary>
    /// The C64's global scripts as <c>docs/scripts/retail/global-scripts.c</c> lists them: every
    /// offset the listing gives is where an instruction starts in what is decoded here, reached or
    /// not. The listing counts from after the 4-byte header, but from the start for scripts listed
    /// with their events, as 54, whose header and leftover bytes it decodes too, which are left out
    /// here. It writes an <c>else if</c> at its jump, so the condition after it has no line of its
    /// own. Script 44 stops at once, and the listing decodes the dead bytes after it; script 67's
    /// index entry is stale, its size past the end of its room file.
    /// </summary>
    [Fact]
    public void The_c64_global_scripts_decode_as_the_documented_listing_has_them()
    {
        if (GameFolders.Find(C64Folder) is not { } path || DocsFile("scripts/retail/global-scripts.c") is not { } listing)
        {
            return;
        }

        var loader = ResourceLoader.Open(new DirectoryGameContainer(path));
        var decoder = new ScriptDecoder(0);
        var scripts = ListedOffsets(listing, @"^Script-(\d+)", heading => heading.Groups[1].Value);
        int checkedScripts = 0;
        foreach (var (name, (offsets, events)) in scripts)
        {
            int number = int.Parse(name, CultureInfo.InvariantCulture);
            if (number is 44 or 67)
            {
                continue;
            }

            var code = loader.Load(ResourceType.Script, number)!.Value;
            var starts = InstructionStarts(decoder, code, [ScriptHeader]);
            int shift = events ? 0 : ScriptHeader;
            var documented = offsets.Select(offset => offset + shift).Where(offset => offset >= ScriptHeader && (number != 54 || offset < 0xE2));
            Assert.All(documented, offset => Assert.True(starts.Contains(offset), $"script {number}: the listing has an instruction at {offset - shift:X4}"));
            checkedScripts++;
        }

        Assert.Equal(154, checkedScripts);
    }

    /// <summary>
    /// The C64's room entry and exit scripts, and its object scripts, as
    /// <c>docs/scripts/retail/room-scripts.c</c> and <c>object-scripts.c</c> list them: every
    /// offset is where an instruction starts here. Room scripts count from their start, object
    /// scripts from the start of the object's code block, as the dumps they were listed from did;
    /// the dumps name an object by its number, a background one by its number with its type.
    /// </summary>
    [Fact]
    public void The_c64_room_and_object_scripts_decode_as_the_documented_listing_has_them()
    {
        if (GameFolders.Find(C64Folder) is not { } path
            || DocsFile("scripts/retail/room-scripts.c") is not { } roomListing
            || DocsFile("scripts/retail/object-scripts.c") is not { } objectListing)
        {
            return;
        }

        var loader = ResourceLoader.Open(new DirectoryGameContainer(path));
        var decoder = new ScriptDecoder(0);

        // room scripts: a "Room N" heading, then its Entry and Exit scripts
        int room = 0;
        var roomScripts = ListedOffsets(roomListing, @"^ \* Room (\d+)|^(Entry|Exit)\b", heading =>
        {
            if (heading.Groups[1].Success)
            {
                room = int.Parse(heading.Groups[1].Value, CultureInfo.InvariantCulture);
                return null;
            }

            return $"{room} {heading.Groups[2].Value}";
        });
        foreach (var (name, (offsets, _)) in roomScripts)
        {
            var parts = name.Split(' ');
            var listed = loader.LoadRoom(int.Parse(parts[0], CultureInfo.InvariantCulture))!;
            var code = parts[1] == "Entry" ? listed.EntryScript : listed.ExitScript;
            var starts = InstructionStarts(decoder, code, [0]);
            Assert.All(offsets, offset => Assert.True(starts.Contains(offset), $"room {name}: the listing has an instruction at {offset:X4}"));
        }

        // object scripts: a "roomobj-R-N.dmp" heading, then a block for each verb. The meteor's Pick
        // up differs in the copy here: it starts with ifStateIntrinsicOff(143), 4 bytes, where the
        // listing's copy has if (Var[89] == 2), 5, and the rest follows a byte earlier
        const int Meteor = 252;
        var objectScripts = ListedOffsets(objectListing, @"^roomobj-(\d+)-(\d+)\.dmp", heading => $"{heading.Groups[1].Value} {heading.Groups[2].Value}");
        foreach (var (name, (offsets, _)) in objectScripts)
        {
            var parts = name.Split(' ');
            var objectRoom = loader.LoadRoom(int.Parse(parts[0], CultureInfo.InvariantCulture))!;
            var obj = objectRoom.FindObject(int.Parse(parts[1], CultureInfo.InvariantCulture))!;
            if (parts[0] == "51" && obj.Number == Meteor)
            {
                continue;
            }

            var starts = InstructionStarts(decoder, obj.Code, obj.Verbs.Select(v => v.Offset));
            Assert.All(offsets, offset => Assert.True(starts.Contains(offset), $"room {parts[0]} object {obj.Number}: the listing has an instruction at {offset:X4}"));
        }

        Assert.Equal(75, roomScripts.Count);
        Assert.Equal(434, objectScripts.Count);
    }

    /// <summary>
    /// The offsets a listing gives, by script: a line the heading pattern matches starts a script,
    /// named by <paramref name="name"/> (null to start none), and every following line that begins
    /// with an offset in brackets adds it, as does an "Events:" line say the script is listed whole.
    /// </summary>
    private static Dictionary<string, (List<int> Offsets, bool Events)> ListedOffsets(string listing, string heading, Func<Match, string?> name)
    {
        var scripts = new Dictionary<string, (List<int> Offsets, bool Events)>();
        string? current = null;
        var headingPattern = new Regex(heading);
        var offsetPattern = new Regex(@"^\s*\[([0-9A-Fa-f]{4})\]");
        foreach (var line in File.ReadLines(listing))
        {
            if (headingPattern.Match(line) is { Success: true } match)
            {
                current = name(match);
                if (current is not null && !scripts.ContainsKey(current))
                {
                    scripts[current] = ([], false);
                }
            }
            else if (current is not null && offsetPattern.Match(line) is { Success: true } offset)
            {
                scripts[current].Offsets.Add(Convert.ToInt32(offset.Groups[1].Value, 16));
            }
            else if (current is not null && line.StartsWith("Events:", StringComparison.Ordinal))
            {
                scripts[current] = (scripts[current].Offsets, true);
            }
        }

        return scripts;
    }

    /// <summary>
    /// Where instructions start in some code: those execution reaches from the entries, those a
    /// listing decodes one after another in the gaps no path reaches, and the end of the code.
    /// </summary>
    private static HashSet<int> InstructionStarts(ScriptDecoder decoder, ReadOnlyMemory<byte> code, IEnumerable<int> entries)
    {
        var reachable = decoder.DecodeReachable(code, entries);
        var starts = reachable.Keys.Append(code.Length).ToHashSet();
        foreach (var instruction in reachable.Values.Where(i => !starts.Contains(i.End)).ToList())
        {
            for (int offset = instruction.End; offset < code.Length && !reachable.ContainsKey(offset);)
            {
                starts.Add(offset);
                try
                {
                    offset = decoder.Decode(code.Span, offset).End;
                }
                catch (InvalidDataException)
                {
                    break;
                }
            }
        }

        return starts;
    }

    /// <summary>A file of this repository's documentation, found by walking up from the test binary.</summary>
    private static string? DocsFile(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "docs", relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
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
