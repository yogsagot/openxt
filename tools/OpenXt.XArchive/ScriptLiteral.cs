namespace OpenXt.XArchive;

/// <summary>
/// A constant value the script builds with no code in between: a number, or an array of literals.
/// The game's map editor exports the universe as one such array, compiled as a long run of pushes
/// and <see cref="ScriptOpcode.MakeArray"/>s.
/// </summary>
public sealed class ScriptLiteral
{
    private ScriptLiteral(int number, IReadOnlyList<ScriptLiteral>? items)
    {
        Number = number;
        Items = items;
    }

    public int Number { get; }

    /// <summary>The elements, in index order, or null for a number.</summary>
    public IReadOnlyList<ScriptLiteral>? Items { get; }

    public bool IsArray => Items is not null;

    public int Count => Items?.Count ?? 0;

    public ScriptLiteral this[int index] => Items is { } items
        ? items[index]
        : throw new InvalidOperationException("A number has no elements.");

    /// <summary>
    /// The largest array literal in <c>[start, end)</c>. Runs of literal instructions are evaluated
    /// on a stack: pushes push, <see cref="ScriptOpcode.MakeArray"/> pops a count and that many values and
    /// pushes them as an array. Any other instruction ends the run.
    ///
    /// The interpreter indexes arrays from the last value pushed (helper <c>0x004359F0</c>, and the
    /// way <c>BuildFromEditor</c> reads them back), so element 0 is the last one in the source.
    /// </summary>
    public static ScriptLiteral? FindLargestArray(ScriptFile script, int start, int end)
    {
        ScriptLiteral? best = null;
        List<ScriptLiteral> stack = [];

        int address = start;
        while (address < Math.Min(end, script.Code.Count))
        {
            ScriptInstruction instruction = ScriptInstruction.Decode(script.Code, address);
            address += instruction.Length;

            if (instruction.SmallConstant is { } small)
            {
                stack.Add(new ScriptLiteral(small, null));
                continue;
            }

            switch (instruction.IsKnown ? instruction.Opcode : default)
            {
                case ScriptOpcode.Push16 or ScriptOpcode.Push16Alt or ScriptOpcode.Push32 or ScriptOpcode.Push32Alt:
                    stack.Add(new ScriptLiteral(instruction.Operand, null));
                    continue;

                case ScriptOpcode.MakeArray when stack.Count > 0 && !stack[^1].IsArray
                                            && stack[^1].Number >= 0 && stack[^1].Number < stack.Count:
                    int count = stack[^1].Number;
                    ScriptLiteral[] items = new ScriptLiteral[count];
                    for (int i = 0; i < count; i++)
                        items[i] = stack[stack.Count - 2 - i];
                    stack.RemoveRange(stack.Count - 1 - count, count + 1);
                    stack.Add(new ScriptLiteral(0, items));
                    continue;
            }

            Keep(stack, ref best);
            stack.Clear();
        }

        Keep(stack, ref best);
        return best;
    }

    private static void Keep(List<ScriptLiteral> stack, ref ScriptLiteral? best)
    {
        foreach (ScriptLiteral value in stack)
        {
            if (value.IsArray && value.Count > (best?.Count ?? 0))
                best = value;
        }
    }
}
