namespace Maniacal.Scumm.Scripts;

public enum OperandKind
{
    /// <summary>A number in the script.</summary>
    Constant,

    /// <summary>The value of a variable; <see cref="Operand.Value"/> is the variable number.</summary>
    Variable,

    /// <summary>Text in the script, up to and without its zero byte.</summary>
    Text,

    /// <summary>Version 0: the object the running sentence is about (<c>_cmdObject</c>), for which the instruction has no operand.</summary>
    CommandObject,
}

public readonly record struct Operand(OperandKind Kind, int Value, byte[]? Text = null)
{
    public static Operand Constant(int value) => new(OperandKind.Constant, value);

    public static Operand Variable(int number) => new(OperandKind.Variable, number);

    /// <summary>The sentence's object, in version 0.</summary>
    public static readonly Operand CommandObject = new(OperandKind.CommandObject, 0);

    public override string ToString() => Kind switch
    {
        OperandKind.Variable => $"Var[{Value}]",
        OperandKind.CommandObject => "cmdObject",
        OperandKind.Text => $"\"{Escape(Text!)}\"",
        _ => Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    private static string Escape(byte[] text)
    {
        var builder = new System.Text.StringBuilder();
        foreach (byte b in text)
        {
            builder.Append(b is >= 32 and < 127 && b != '"' && b != '\\' ? (char)b : $"\\x{b:X2}");
        }

        return builder.ToString();
    }
}

/// <summary>One decoded script instruction.</summary>
/// <param name="Offset">Where it starts in the script.</param>
/// <param name="Opcode">Its first byte; for the opcodes that take variables, the top 3 bits say which operands are variables.</param>
/// <param name="Handler">The ScummVM handler, such as <c>o2_putActor</c>.</param>
/// <param name="Operands">Its operands in the order the handler fetches them.</param>
/// <param name="Length">Its length in bytes.</param>
/// <param name="Result">The variable it writes, if any.</param>
/// <param name="ResultIndirect">Whether <paramref name="Result"/> names a variable holding the number of the variable written.</param>
/// <param name="JumpTarget">Where it jumps when its condition fails (or always, for <c>jumpRelative</c>).</param>
public sealed record Instruction(
    int Offset,
    byte Opcode,
    string Handler,
    IReadOnlyList<Operand> Operands,
    int Length,
    int? Result = null,
    bool ResultIndirect = false,
    int? JumpTarget = null)
{
    /// <summary>The handler without its version prefix: <c>putActor</c>.</summary>
    public string Name => Handler[(Handler.IndexOf('_') + 1)..];

    public int End => Offset + Length;

    /// <summary>
    /// Whether execution never falls through to the next instruction: stopping the script (including
    /// <c>stopScript(0)</c>, which stops the script running it, as <c>stopScriptCommon</c> does), an
    /// unconditional jump, chaining to another script, or restarting.
    /// </summary>
    public bool EndsFlow => Handler is "o5_stopObjectCode" or "o5_jumpRelative" or "o2_chainScript" or "o2_restart" or "o_stopCurrentScript"
        || (Handler == "o2_stopScript" && Operands[0] is { Kind: OperandKind.Constant, Value: 0 });

    public override string ToString()
    {
        var text = new System.Text.StringBuilder();
        text.Append($"[{Offset:X4}] ");
        if (Result is { } result)
        {
            text.Append(ResultIndirect ? $"Var[Var[{result}]] = " : $"Var[{result}] = ");
        }

        text.Append(Name).Append('(').Append(string.Join(", ", Operands)).Append(')');
        if (JumpTarget is { } target)
        {
            text.Append($" -> [{target:X4}]");
        }

        return text.ToString();
    }
}
