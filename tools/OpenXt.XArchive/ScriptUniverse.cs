namespace OpenXt.XArchive;

/// <summary>Which sides of a sector have a gate. The values are the script's own bits.</summary>
[Flags]
public enum SectorGates
{
    None = 0,
    North = 1,
    South = 2,
    West = 4,
    East = 8,
}

/// <summary>
/// One sector as the map editor exported it. <see cref="X"/> and <see cref="Y"/> are the map cell
/// counted from 1, as in the text ids; <see cref="Objects"/> are the sector's contents, not decoded
/// yet.
/// </summary>
public sealed record ScriptSector(int X, int Y, SectorGates Gates, IReadOnlyList<ScriptLiteral> Objects);

/// <summary>A gate the script adds outside the editor data, such as Getsu Fune's after the Perseus mission.</summary>
public sealed record ScriptGate(string Function, int X, int Y, SectorGates Side);

/// <summary>
/// The universe the compiled script builds, read straight from its bytecode.
///
/// <c>class2000.BuildFromEditor</c> — present in both games — holds the editor data as an array
/// literal, one element per sector, and loops over it. XBTF's has two literals chosen by an
/// argument: the real 54-sector universe, and an 8-sector one used only when the argument is −2;
/// the larger one is the game. Each sector array is, by index: a marker the loop checks (1 in
/// X-Tension, 2 in XBTF), cell x − 1, cell y − 1, a gate mask, then the sector's objects. For each set
/// bit of the mask the loop creates a gate object (class 2014) 7.2 km out on that side and calls
/// <c>GS_AddGate(bit index, y − 1, x − 1)</c>.
///
/// Which bit faces which way is not in the code; it was matched against elite-games.ru's map of
/// each game, and agrees for every sector. Sector ownership is not among the four values.
/// </summary>
public sealed class ScriptUniverse
{
    public const string BuilderClassMethod = "BuildFromEditor";
    public const string AddGateNative = "GS_AddGate";

    private ScriptUniverse(IReadOnlyList<ScriptSector> sectors, IReadOnlyList<ScriptGate> extraGates)
    {
        Sectors = sectors;
        ExtraGates = extraGates;
    }

    public IReadOnlyList<ScriptSector> Sectors { get; }

    /// <summary>
    /// <c>GS_AddGate</c> calls elsewhere in the script whose arguments are constants. These are
    /// conditional — they run when the plot reaches them — so they are reported, not merged.
    /// </summary>
    public IReadOnlyList<ScriptGate> ExtraGates { get; }

    /// <summary>The universe, or null if the script has no editor data (XBTF's <c>l/002.obj</c>).</summary>
    public static ScriptUniverse? Read(ScriptFile script)
    {
        Dictionary<int, string> labels = ScriptDisassembler.Labels(script);
        int[] entryPoints = labels.Keys.Order().ToArray();

        ScriptFunction? builder = script.Classes
            .SelectMany(c => c.Functions)
            .FirstOrDefault(f => f.Name == BuilderClassMethod);
        if (builder is null)
            return null;

        int next = Array.BinarySearch(entryPoints, builder.Address + 1);
        int end = next >= 0 ? entryPoints[next] : ~next < entryPoints.Length ? entryPoints[~next] : script.Code.Count;

        ScriptLiteral? editor = ScriptLiteral.FindLargestArray(script, builder.Address, end);
        if (editor is null)
            return null;

        ScriptLiteral[] entries = editor.Items!
            .Where(e => e.IsArray && e.Count >= 4 && !e[0].IsArray && !e[1].IsArray && !e[2].IsArray && !e[3].IsArray)
            .ToArray();
        if (entries.Length == 0)
            return null;

        // The loop compares element 0 with a per-game constant and skips entries that differ.
        int marker = entries.GroupBy(e => e[0].Number).MaxBy(g => g.Count())!.Key;

        List<ScriptSector> sectors = [];
        foreach (ScriptLiteral entry in entries)
        {
            if (entry[0].Number != marker)
                continue;

            sectors.Add(new ScriptSector(entry[1].Number + 1, entry[2].Number + 1, (SectorGates)entry[3].Number,
                entry.Items!.Skip(4).ToArray()));
        }

        return new ScriptUniverse(sectors, FindConstantGates(script, labels, builder.Address, end));
    }

    /// <summary>
    /// Every <c>GS_AddGate</c> call outside the builder whose three arguments were pushed as plain
    /// constants just before it: side (bit index), y − 1, x − 1, then the argument count.
    /// </summary>
    private static List<ScriptGate> FindConstantGates(ScriptFile script, Dictionary<int, string> labels,
        int builderStart, int builderEnd)
    {
        List<ScriptGate> gates = [];
        List<ScriptInstruction> recent = [];
        string function = "";

        foreach (ScriptInstruction instruction in script.Instructions())
        {
            if (labels.TryGetValue(instruction.Address, out string? label))
                function = label;

            bool isAddGate = instruction.Opcode is ScriptOpcode.CallNative or ScriptOpcode.CallNative32
                             && script.GetString(instruction.Operand) == AddGateNative;

            if (isAddGate && (instruction.Address < builderStart || instruction.Address >= builderEnd)
                          && recent.Count >= 4 && Constant(recent[^1]) == 3
                          && Constant(recent[^4]) is >= 0 and < 4 and var side
                          && Constant(recent[^3]) is { } y && Constant(recent[^2]) is { } x)
            {
                gates.Add(new ScriptGate(function, x + 1, y + 1, (SectorGates)(1 << side)));
            }

            recent.Add(instruction);
            if (recent.Count > 4)
                recent.RemoveAt(0);
        }

        return gates;
    }

    private static int? Constant(ScriptInstruction instruction) =>
        instruction.SmallConstant ?? (instruction.IsKnown && instruction.Opcode is ScriptOpcode.Push16
            or ScriptOpcode.Push16Alt or ScriptOpcode.Push32 or ScriptOpcode.Push32Alt
            ? instruction.Operand
            : null);
}
