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
/// What an editor record builds, by the script class <c>BuildObject</c> creates for its kind. The
/// classes are unnamed; these names come from their methods (<c>GetColor</c>, <c>GetYield</c>,
/// <c>AddProduct</c>, <c>BigShipDocking</c>, <c>AddTradeable</c>, <c>DialogAction</c>).
/// </summary>
public enum ScriptObjectType
{
    Unknown,

    /// <summary>Class 2008. Its values carry a colour.</summary>
    Sun,

    /// <summary>Class 2002: a background body with a subtype and an owner. Planet by elimination, unconfirmed.</summary>
    Planet,

    /// <summary>Class 2012.</summary>
    Asteroid,

    /// <summary>Class 2006: makes <see cref="ScriptObject.Products"/> from <see cref="ScriptObject.Resources"/>.</summary>
    Factory,

    /// <summary>Class 2037, built for the factory kind when its subtype is 0.</summary>
    Shipyard,

    /// <summary>Class 2007: trading stations and docks; subtype 2 is the pirates' base.</summary>
    TradingStation,

    /// <summary>Class 2042, built for the trading kind when its subtype is 1: a station you can talk to.</summary>
    TalkingStation,

    /// <summary>Classes 2011 and 2020-2036; the subtype picks the ship class.</summary>
    Ship,
}

/// <summary>A ware as the script names it: the engine's main type and the index within it. See <see cref="ScriptWares"/>.</summary>
public readonly record struct ScriptWare(int MainType, int SubType);

/// <summary>
/// One object the editor placed. By index the record is: kind, east, up, north, subtype, race,
/// <see cref="TypeId"/>, then pairs of (list kind, list) — wares the object trades or makes, and
/// objects docked at it. Coordinates are the game's own units; the axes were matched against
/// elite-games.ru's XBTF station map, the scale has not been measured.
///
/// For a station, <see cref="TypeId"/> is its text id over ten: the name is <c>TypeId·10 + 1</c>
/// and a factory's product <c>TypeId·10 + 2</c>'s ware, <c>TypeId·10 + 3</c>. Trading stations are
/// <c>3R8</c> and equipment docks <c>3R9</c>, one pair per race. This matches the executables' station
/// records for 1,088 of X-Tension's 1,099 stations and 601 of XBTF's 609, with the race agreeing
/// wherever the script's race list names it. XBTF's records number some factories from 5000 (5721
/// for the 5 MW shield factory, 272 here); the product still agrees. The pirate base (376) and one
/// Xenon station (360) use text ids no station record has.
/// </summary>
public sealed record ScriptObject(
    int Kind,
    ScriptObjectType Type,
    int East,
    int Up,
    int North,
    int Subtype,
    int Race,
    int TypeId,
    IReadOnlyList<ScriptWare> Products,
    IReadOnlyList<ScriptWare> Resources,
    IReadOnlyList<ScriptWare> SecondaryResources,
    IReadOnlyList<ScriptWare> Tradeables,
    IReadOnlyList<ScriptObject> Docked,
    ScriptLiteral Source)
{
    public bool IsStation => Type is ScriptObjectType.Factory or ScriptObjectType.Shipyard
        or ScriptObjectType.TradingStation or ScriptObjectType.TalkingStation;

    /// <summary>The text id that names a station, or null for anything else.</summary>
    public int? StationTextId => IsStation ? TypeId * 10 + 1 : null;

    /// <summary>The text id of the ware a factory makes, or null for anything else.</summary>
    public int? ProductTextId => Type == ScriptObjectType.Factory ? TypeId * 10 + 3 : null;
}

/// <summary>
/// One sector as the map editor exported it. <see cref="X"/> and <see cref="Y"/> are the map cell
/// counted from 1, as in the text ids. <see cref="Owner"/> is the race index the game gives the
/// sector, or null for none (see <see cref="ScriptUniverse.Races"/>).
/// </summary>
public sealed record ScriptSector(int X, int Y, SectorGates Gates, int? Owner, int? Background,
    IReadOnlyList<ScriptObject> Objects);

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
/// <c>GS_AddGate(bit index, y − 1, x − 1)</c>. Which bit faces which way is not in the code; it was
/// matched against elite-games.ru's map of each game, and agrees for every sector.
///
/// Each object's kind is a case of <c>class2000.BuildObject</c>, which this reads to learn which
/// class each kind creates: the numbers differ between the games (XBTF's are mostly X-Tension's
/// plus one) but the classes do not. The one kind <c>BuildObject</c> does not handle is the
/// two-element background record, which the loop turns into <c>SetGalaxyCut(value + 1000)</c>.
///
/// A sector's owner is set by the loop, in both games, from its trading stations of subtype other
/// than 1 and 2: <c>SetOwner(races[record[5]])</c>. Sectors without one have no owner. The races
/// are class-2003 objects created in <c>class2000.Build</c>, each named by a text id read just
/// before it.
/// </summary>
public sealed class ScriptUniverse
{
    public const string BuilderClassMethod = "BuildFromEditor";
    public const string ObjectBuilderMethod = "BuildObject";
    public const string AddGateNative = "GS_AddGate";
    public const string ReadTextNative = "SE_ReadText";
    public const int RaceClass = 2003;

    private const int ListKindProducts = 7;
    private const int ListKindResources = 8;
    private const int ListKindSecondary = 1;
    private const int ListKindDocked = 2;

    private ScriptUniverse(IReadOnlyList<ScriptSector> sectors, IReadOnlyList<ScriptGate> extraGates,
        IReadOnlyDictionary<int, int> races, IReadOnlyDictionary<int, IReadOnlyList<int>> kindClasses)
    {
        Sectors = sectors;
        ExtraGates = extraGates;
        Races = races;
        KindClasses = kindClasses;
    }

    public IReadOnlyList<ScriptSector> Sectors { get; }

    /// <summary>
    /// <c>GS_AddGate</c> calls elsewhere in the script whose arguments are constants. These are
    /// conditional — they run when the plot reaches them — so they are reported, not merged.
    /// </summary>
    public IReadOnlyList<ScriptGate> ExtraGates { get; }

    /// <summary>Race index to the text id of its name. Indices without an entry are races the script builds otherwise.</summary>
    public IReadOnlyDictionary<int, int> Races { get; }

    /// <summary>Record kind to the classes its <c>BuildObject</c> case creates, in code order.</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<int>> KindClasses { get; }

    /// <summary>The universe, or null if the script has no editor data (XBTF's <c>l/002.obj</c>).</summary>
    public static ScriptUniverse? Read(ScriptFile script)
    {
        Dictionary<int, string> labels = ScriptDisassembler.Labels(script);
        int[] entryPoints = labels.Keys.Order().ToArray();

        ScriptFunction? builder = Method(script, BuilderClassMethod);
        if (builder is null)
            return null;

        int end = FunctionEnd(script, entryPoints, builder.Address);
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

        ScriptFunction? objectBuilder = Method(script, ObjectBuilderMethod);
        Dictionary<int, IReadOnlyList<int>> kindClasses = objectBuilder is null
            ? []
            : KindClassesOf(script, objectBuilder.Address, FunctionEnd(script, entryPoints, objectBuilder.Address));
        Dictionary<int, ScriptObjectType> kinds = kindClasses.ToDictionary(k => k.Key, k => TypeOf(k.Value));

        List<ScriptSector> sectors = [];
        foreach (ScriptLiteral entry in entries)
        {
            if (entry[0].Number != marker)
                continue;

            List<ScriptObject> objects = [];
            int? background = null;
            int? owner = null;

            foreach (ScriptLiteral record in entry.Items!.Skip(4))
            {
                if (!record.IsArray || record.Count == 0 || record[0].IsArray)
                    continue;

                if (record.Count == 2 && !kinds.ContainsKey(record[0].Number) && !record[1].IsArray)
                {
                    background = record[1].Number;
                    continue;
                }

                ScriptObject item = ObjectOf(record, kinds);
                objects.Add(item);

                if (item.Type == ScriptObjectType.TradingStation && item.Subtype is not (1 or 2))
                    owner = item.Race;
            }

            sectors.Add(new ScriptSector(entry[1].Number + 1, entry[2].Number + 1, (SectorGates)entry[3].Number,
                owner, background, objects));
        }

        return new ScriptUniverse(sectors, FindConstantGates(script, labels, builder.Address, end),
            FindRaces(script), kindClasses);
    }

    private static ScriptFunction? Method(ScriptFile script, string name) =>
        script.Classes.SelectMany(c => c.Functions).FirstOrDefault(f => f.Name == name);

    private static int FunctionEnd(ScriptFile script, int[] entryPoints, int start)
    {
        int next = Array.BinarySearch(entryPoints, start + 1);
        if (next < 0)
            next = ~next;
        return next < entryPoints.Length ? entryPoints[next] : script.Code.Count;
    }

    private static ScriptObject ObjectOf(ScriptLiteral record, IReadOnlyDictionary<int, ScriptObjectType> kinds)
    {
        int kind = record[0].Number;
        int Value(int index) => index < record.Count && !record[index].IsArray ? record[index].Number : 0;

        ScriptObjectType type = kinds.GetValueOrDefault(kind);
        int subtype = Value(4);

        // Both station kinds pick their class by subtype, in BuildObject itself.
        if (type == ScriptObjectType.Factory && subtype == 0)
            type = ScriptObjectType.Shipyard;
        else if (type == ScriptObjectType.TradingStation && subtype == 1)
            type = ScriptObjectType.TalkingStation;

        List<ScriptWare> products = [], resources = [], secondary = [], tradeables = [];
        List<ScriptObject> docked = [];

        for (int i = 7; i + 1 < record.Count; i += 2)
        {
            if (record[i].IsArray || !record[i + 1].IsArray)
                continue;

            ScriptLiteral list = record[i + 1];
            switch (record[i].Number)
            {
                case ListKindProducts:
                    products.AddRange(Wares(list));
                    break;
                case ListKindResources:
                    resources.AddRange(Wares(list));
                    break;
                // The same list kind is AddSResource on a factory and AddTradeable on a trading station.
                case ListKindSecondary when type is ScriptObjectType.TradingStation or ScriptObjectType.TalkingStation:
                    tradeables.AddRange(Wares(list));
                    break;
                case ListKindSecondary:
                    secondary.AddRange(Wares(list));
                    break;
                case ListKindDocked:
                    foreach (ScriptLiteral child in list.Items!)
                        if (child.IsArray && child.Count > 0 && !child[0].IsArray)
                            docked.Add(ObjectOf(child, kinds));
                    break;
            }
        }

        return new ScriptObject(kind, type, Value(1), Value(2), Value(3), subtype, Value(5), Value(6),
            products, resources, secondary, tradeables, docked, record);
    }

    private static IEnumerable<ScriptWare> Wares(ScriptLiteral list)
    {
        foreach (ScriptLiteral pair in list.Items!)
        {
            if (pair.IsArray && pair.Count >= 2 && !pair[0].IsArray && !pair[1].IsArray)
                yield return new ScriptWare(pair[0].Number, pair[1].Number);
        }
    }

    private static ScriptObjectType TypeOf(IReadOnlyList<int> classes)
    {
        if (classes.Contains(2006) || classes.Contains(2037))
            return ScriptObjectType.Factory;
        if (classes.Contains(2007) || classes.Contains(2042))
            return ScriptObjectType.TradingStation;
        if (classes.Contains(2008))
            return ScriptObjectType.Sun;
        if (classes.Contains(2002))
            return ScriptObjectType.Planet;
        if (classes.Contains(2012))
            return ScriptObjectType.Asteroid;
        if (classes.Any(c => c is 2011 or (>= 2020 and <= 2036)))
            return ScriptObjectType.Ship;
        return ScriptObjectType.Unknown;
    }

    /// <summary>
    /// Follows <c>BuildObject</c>'s outer switch. Each case is <c>Dup; constant kind; Equal;
    /// JumpIfFalse next</c>, where <c>next</c> is the following case's test; the classes a case
    /// creates are the constants pushed just before each <see cref="ScriptOpcode.New"/> up to there.
    /// </summary>
    private static Dictionary<int, IReadOnlyList<int>> KindClassesOf(ScriptFile script, int start, int end)
    {
        List<ScriptInstruction> body = [];
        for (int address = start; address < end;)
        {
            ScriptInstruction instruction = ScriptInstruction.Decode(script.Code, address);
            body.Add(instruction);
            address += instruction.Length;
        }

        Dictionary<int, int> indexOf = [];
        for (int i = 0; i < body.Count; i++)
            indexOf[body[i].Address] = i;

        Dictionary<int, IReadOnlyList<int>> result = [];
        int test = Enumerable.Range(0, body.Count).FirstOrDefault(i => IsCaseTest(body, i), -1);

        while (test >= 0)
        {
            int kind = Constant(body[test + 1])!.Value;
            int target = body[test + 3].Operand;
            int stop = indexOf.TryGetValue(target, out int t) ? t : body.Count;

            List<int> classes = [];
            for (int i = test + 4; i < stop && i < body.Count; i++)
            {
                if (body[i].IsKnown && body[i].Opcode == ScriptOpcode.New && Constant(body[i - 1]) is { } type)
                    classes.Add(type);
            }

            result.TryAdd(kind, classes);
            test = stop < body.Count && IsCaseTest(body, stop) ? stop : -1;
        }

        return result;
    }

    private static bool IsCaseTest(List<ScriptInstruction> body, int i) =>
        i + 3 < body.Count
        && body[i].IsKnown && body[i].Opcode == ScriptOpcode.Dup
        && Constant(body[i + 1]) is not null
        && body[i + 2].IsKnown && body[i + 2].Opcode == ScriptOpcode.Equal
        && body[i + 3].IsKnown && body[i + 3].Opcode == ScriptOpcode.JumpIfFalse;

    /// <summary>
    /// Race objects are created as <c>SE_ReadText(name); index; 4; New 2003; Create</c> and then
    /// stored at that index of the race list. Returns index to the name's text id.
    /// </summary>
    private static Dictionary<int, int> FindRaces(ScriptFile script)
    {
        Dictionary<int, int> races = [];
        List<ScriptInstruction> recent = [];

        foreach (ScriptInstruction instruction in script.Instructions())
        {
            recent.Add(instruction);
            if (recent.Count > 7)
                recent.RemoveAt(0);

            if (recent.Count == 7 && instruction.IsKnown && instruction.Opcode == ScriptOpcode.New
                && Constant(recent[5]) == RaceClass
                && IsNative(script, recent[2], ReadTextNative)
                && Constant(recent[3]) is { } index
                && Constant(recent[0]) is { } textId)
            {
                races.TryAdd(index, textId);
            }
        }

        return races;
    }

    private static bool IsNative(ScriptFile script, ScriptInstruction instruction, string name) =>
        instruction.IsKnown && instruction.Opcode is ScriptOpcode.CallNative or ScriptOpcode.CallNative32
                            && script.GetString(instruction.Operand) == name;

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

            if (IsNative(script, instruction, AddGateNative)
                && (instruction.Address < builderStart || instruction.Address >= builderEnd)
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
