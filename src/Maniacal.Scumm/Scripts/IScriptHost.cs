namespace Maniacal.Scumm.Scripts;

/// <summary>
/// The world the scripts act on: actors, rooms, objects on screen, verbs, text and sound. The
/// script engine runs the instructions that only touch script state (variables, conditions, flow,
/// script control, object states and owners) and hands every other instruction here, with its
/// operands already evaluated. For a few instructions it does its own part and then hands them on
/// too: <c>cutscene</c>, <c>endCutscene</c>, <c>setOwnerOf</c> and the object state changes.
/// </summary>
internal interface IScriptHost
{
    /// <summary>
    /// Carries out an instruction. <paramref name="arguments"/> holds the value of each operand, in
    /// order (0 for text operands, whose bytes are in the instruction). The return value is written
    /// to the instruction's result variable when it has one; for an instruction with a jump target
    /// it is the condition: non-zero carries on, zero jumps. A host can make the script wait with
    /// <see cref="ScriptEngine.Repeat"/>.
    /// </summary>
    int Execute(ScriptEngine engine, Instruction instruction, IReadOnlyList<int> arguments);
}

/// <summary>A host that records what the scripts ask for and does nothing else: for running scripts headless.</summary>
internal sealed class RecordingHost : IScriptHost
{
    public List<string> Calls { get; } = [];

    /// <summary>The value world queries return; conditions carry on unless it is 0.</summary>
    public int QueryResult { get; set; }

    public int Execute(ScriptEngine engine, Instruction instruction, IReadOnlyList<int> arguments)
    {
        var shown = instruction.Operands.Select((operand, i) => operand.Kind == OperandKind.Text ? operand.ToString() : arguments[i].ToString(System.Globalization.CultureInfo.InvariantCulture));
        Calls.Add($"script {engine.CurrentScriptNumber} [{instruction.Offset:X4}] {instruction.Name}({string.Join(", ", shown)})");
        return instruction.JumpTarget is null ? QueryResult : 1;
    }
}
