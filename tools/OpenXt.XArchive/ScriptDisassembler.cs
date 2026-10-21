using System.Globalization;

namespace OpenXt.XArchive;

/// <summary>
/// Turns a <see cref="ScriptFile"/> into a readable listing: one instruction per line, names
/// resolved through the string pool, function entry points labelled. A listing of a real script is
/// the game's own code, so like any other extracted data it never belongs in the repository.
/// </summary>
public static class ScriptDisassembler
{
    public static void Write(ScriptFile script, TextWriter output)
    {
        Dictionary<int, string> labels = Labels(script);

        foreach (ScriptInstruction instruction in script.Instructions())
        {
            if (labels.TryGetValue(instruction.Address, out string? label))
            {
                output.WriteLine();
                output.WriteLine($"{label}:");
            }

            output.WriteLine($"{instruction.Address:X6}  {Format(script, instruction, labels)}");
        }
    }

    /// <summary>One instruction as text, without its address.</summary>
    public static string Format(ScriptFile script, ScriptInstruction instruction,
        IReadOnlyDictionary<int, string>? labels = null)
    {
        if (!instruction.IsKnown)
            return $".word 0x{instruction.Word:X4}";

        if (instruction.SmallConstant is { } constant)
            return $"Const {constant}";

        string mnemonic = instruction.Opcode.ToString();
        return instruction.OperandKind switch
        {
            ScriptOperandKind.Number => $"{mnemonic,-15} {instruction.Operand.ToString(CultureInfo.InvariantCulture)}",
            ScriptOperandKind.Name => $"{mnemonic,-15} {script.GetString(instruction.Operand) ?? $"?{instruction.Operand}"}",
            ScriptOperandKind.Address when labels is not null && labels.TryGetValue(instruction.Operand, out string? target)
                => $"{mnemonic,-15} {instruction.Operand:X6} ; {target}",
            ScriptOperandKind.Address => $"{mnemonic,-15} {instruction.Operand:X6}",
            _ => mnemonic,
        };
    }

    /// <summary>Function entry points: globals by name, methods as <c>class&lt;id&gt;.name</c>.</summary>
    public static Dictionary<int, string> Labels(ScriptFile script)
    {
        Dictionary<int, string> labels = [];

        foreach (ScriptFunction function in script.Functions)
            labels.TryAdd(function.Address, function.Name);

        foreach (ScriptClass type in script.Classes)
        foreach (ScriptFunction method in type.Functions)
            labels.TryAdd(method.Address, $"class{type.Id}.{method.Name}");

        return labels;
    }
}
