using System.Buffers.Binary;
using System.Text;
using Xunit;

namespace OpenXt.XArchive.Tests;

public sealed class ScriptFileTests
{
    /// <summary>Builds a script the way the game's compiler lays one out, string pool scrambled.</summary>
    private sealed class ScriptBuilder
    {
        private readonly MemoryStream _chunks = new();
        private readonly MemoryStream _pool = new();

        /// <summary>Adds a string to the pool and returns its offset, the way names are referenced.</summary>
        public int Name(string value)
        {
            int offset = (int)_pool.Length;
            _pool.Write(Encoding.Latin1.GetBytes(value));
            _pool.WriteByte(0);
            return offset;
        }

        public ScriptBuilder Chunk(string tag, int count, params int[] values)
        {
            _chunks.Write(Encoding.ASCII.GetBytes(tag));
            WriteInt(count);
            foreach (int value in values)
                WriteInt(value);
            return this;
        }

        public ScriptBuilder Code(params ushort[] words)
        {
            Chunk("STO2", words.Length);
            Span<byte> word = stackalloc byte[2];
            foreach (ushort w in words)
            {
                BinaryPrimitives.WriteUInt16BigEndian(word, w);
                _chunks.Write(word);
            }

            return this;
        }

        public byte[] Build(int leadingBytes = 0)
        {
            MemoryStream file = new();
            file.Write(new byte[leadingBytes]);
            file.Write(_chunks.ToArray());
            file.Write("STRG"u8);
            file.Write(Scramble(_pool.ToArray()));
            return file.ToArray();
        }

        private void WriteInt(int value)
        {
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(bytes, value);
            _chunks.Write(bytes);
        }
    }

    /// <summary>The inverse of <see cref="ScriptFile.DecodeStrings"/>.</summary>
    private static byte[] Scramble(byte[] plain)
    {
        byte[] scrambled = new byte[plain.Length];
        for (int i = 0; i < plain.Length; i++)
            scrambled[i] = (byte)~(plain[i] + (i == 0 ? 0 : scrambled[i - 1]));
        return scrambled;
    }

    [Fact]
    public void StringPoolUnscrambles()
    {
        byte[] plain = "SE_ReadText\0P_Delay\0ä\0"u8.ToArray();

        Assert.Equal(plain, ScriptFile.DecodeStrings(Scramble(plain)));
        Assert.Empty(ScriptFile.DecodeStrings([]));
    }

    [Fact]
    public void ParsesChunksAndResolvesNames()
    {
        ScriptBuilder builder = new();
        int init = builder.Name("_Init");
        int debug = builder.Name("gs_Debug");
        int update = builder.Name("Update");
        int speed = builder.Name("speed");

        byte[] data = builder
            .Code(0x0000, (ushort)ScriptOpcode.Return, (ushort)ScriptOpcode.Return)
            .Chunk("CTIM", 0x3AEAC8A7)
            .Chunk("SYMB", 1, 1, init, 2)
            .Chunk("VARS", 1, 0, debug, unchecked((int)0x80000000))
            // Two classes, the second deriving from the first; each with its own SYMB / VARS.
            .Chunk("OBJ2", 2, 7, -1)
            .Chunk("SYMB", 1, 2, update, 0)
            .Chunk("VARS", 1, 0, speed, 100)
            .Chunk("VARS", 0, 8, 7)
            .Build();

        ScriptFile script = ScriptFile.Parse(data);

        Assert.Equal(3, script.Code.Count);
        Assert.Equal(0x3AEAC8A7u, script.CompileTime);
        Assert.Equal(new ScriptFunction("_Init", 1, 2), Assert.Single(script.Functions));
        Assert.Equal(new ScriptVariable("gs_Debug", 0, int.MinValue), Assert.Single(script.Globals));

        Assert.Equal(2, script.Classes.Count);
        Assert.Equal(-1, script.Classes[0].ParentId);
        Assert.Equal("Update", Assert.Single(script.Classes[0].Functions).Name);
        Assert.Equal(new ScriptVariable("speed", 0, 100), Assert.Single(script.Classes[0].Fields));
        Assert.Equal(8, script.Classes[1].Id);
        Assert.Equal(7, script.Classes[1].ParentId);
        Assert.Empty(script.Classes[1].Fields);
    }

    [Fact]
    public void Obj3ClassesCarryAnExtraValue()
    {
        ScriptFile script = ScriptFile.Parse(new ScriptBuilder()
            .Code(0)
            .Chunk("OBJ3", 1, 1, -1, 0x39605329)
            .Build());

        Assert.Equal(new[] { (1, -1, 0x39605329) },
            script.Classes.Select(c => (c.Id, c.ParentId, c.Extra)));
    }

    [Fact]
    public void FindsSto2OnlyOnAnEvenOffset()
    {
        ScriptFile script = ScriptFile.Parse(new ScriptBuilder().Code(0x0009).Build(leadingBytes: 2));

        Assert.Equal(new ushort[] { 0x0009 }, script.Code);
        Assert.Throws<InvalidDataException>(() =>
            ScriptFile.Parse(new ScriptBuilder().Code(0x0009).Build(leadingBytes: 1)));
    }

    [Fact]
    public void RejectsTruncatedChunks()
    {
        byte[] data = new ScriptBuilder().Code(1, 2, 3, 4).Build();

        // Cut into the code: the count now promises more words than remain.
        Assert.Throws<InvalidDataException>(() => ScriptFile.Parse(data.AsSpan(0, 12)));
        Assert.Throws<InvalidDataException>(() => ScriptFile.Parse("not a script"u8));
    }

    [Fact]
    public void DecodesOperandsAndLengths()
    {
        ushort[] code =
        [
            (ushort)ScriptOpcode.Push16, 0xFFFE,            // -2, sign-extended
            (ushort)ScriptOpcode.Push32, 0x0001, 0x0002,    // high word first
            0x000E,                                         // Const 5
            (ushort)ScriptOpcode.CallLong, 0x0000, 0x0010, 0xAAAA, 0xBBBB,
            0x00FF,                                         // not an opcode
            (ushort)ScriptOpcode.Jump, 0x0000,              // truncated: operand runs off the end
        ];

        ScriptInstruction[] decoded = Sweep(code);

        Assert.Equal([0, 2, 5, 6, 11, 12, 13], decoded.Select(i => i.Address));
        Assert.Equal(-2, decoded[0].Operand);
        Assert.Equal(0x00010002, decoded[1].Operand);
        Assert.Equal(5, decoded[2].SmallConstant);
        Assert.Equal((0x10, 5, ScriptOperandKind.Address), (decoded[3].Operand, decoded[3].Length, decoded[3].OperandKind));
        Assert.False(decoded[4].IsKnown);
        Assert.Equal(1, decoded[5].Length);
        Assert.Equal(ScriptOperandKind.Number, decoded[0].OperandKind);
    }

    [Fact]
    public void ListingResolvesNamesAndLabels()
    {
        ScriptBuilder builder = new();
        int main = builder.Name("Main");
        int delay = builder.Name("P_Delay");

        ScriptFile script = ScriptFile.Parse(builder
            .Code(0x0000,
                0x000A,                                         // Const 1
                (ushort)ScriptOpcode.CallNative32, 0, (ushort)delay,
                (ushort)ScriptOpcode.Jump, 0, 1,
                (ushort)ScriptOpcode.Return)
            .Chunk("SYMB", 1, 1, main, 0)
            .Build());

        StringWriter listing = new();
        ScriptDisassembler.Write(script, listing);
        string[] lines = listing.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(
        [
            "000000  .word 0x0000",
            "Main:",
            "000001  Const 1",
            "000002  CallNative32    P_Delay",
            "000005  Jump            000001 ; Main",
            "000008  Return",
        ], lines);
    }

    /// <summary>
    /// Compiles a literal the way the game's compiler does: an array pushes its elements last to
    /// first, then its count, then <see cref="ScriptOpcode.MakeArray"/>. Elements are ints or arrays.
    /// </summary>
    private static void EmitLiteral(List<ushort> code, object value)
    {
        if (value is object[] items)
        {
            for (int i = items.Length - 1; i >= 0; i--)
                EmitLiteral(code, items[i]);
            EmitLiteral(code, items.Length);
            code.Add((ushort)ScriptOpcode.MakeArray);
        }
        else if ((int)value is >= 0 and <= 31)
        {
            code.Add((ushort)((int)ScriptOpcode.Const0 + (int)value));
        }
        else
        {
            int number = (int)value;
            code.AddRange([(ushort)ScriptOpcode.Push32, (ushort)(number >> 16), (ushort)number]);
        }
    }

    [Fact]
    public void LiteralArraysIndexFromTheLastValuePushed()
    {
        List<ushort> code = [(ushort)ScriptOpcode.This];
        EmitLiteral(code, new object[] { 1, new object[] { -7200000, 2 }, 40 });
        code.Add((ushort)ScriptOpcode.Return);

        ScriptLiteral literal = ScriptLiteral.FindLargestArray(
            ScriptFile.Parse(new ScriptBuilder().Code([.. code]).Build()), 0, code.Count)!;

        Assert.Equal(3, literal.Count);
        Assert.Equal(1, literal[0].Number);
        Assert.Equal([-7200000, 2], literal[1].Items!.Select(i => i.Number));
        Assert.Equal(40, literal[2].Number);
    }

    [Fact]
    public void ReadsSectorsAndConstantGates()
    {
        ScriptBuilder builder = new();
        int build = builder.Name(ScriptUniverse.BuilderClassMethod);
        int plot = builder.Name("Plot");
        int addGate = builder.Name(ScriptUniverse.AddGateNative);

        List<ushort> code = [0x0000];
        EmitLiteral(code, new object[]
        {
            // marker, x − 1, y − 1, gate mask, objects…
            new object[] { 2, 0, 0, (int)(SectorGates.South | SectorGates.East), new object[] { 0, 7, 17 } },
            new object[] { 2, 1, 0, (int)SectorGates.West },
            new object[] { 9, 5, 5, 0 },     // a different marker: the loop skips it
        });
        code.AddRange([(ushort)ScriptOpcode.StoreStack, 1, (ushort)ScriptOpcode.Return]);

        int plotAddress = code.Count;
        code.AddRange([0x000B, 0x0009, 0x000A, 0x000C, (ushort)ScriptOpcode.CallNative32, 0, (ushort)addGate,
            (ushort)ScriptOpcode.Return]);

        ScriptUniverse universe = ScriptUniverse.Read(ScriptFile.Parse(builder
            .Code([.. code])
            .Chunk("SYMB", 1, plotAddress, plot, 0)
            .Chunk("OBJE", 1, 2000)
            .Chunk("SYMB", 1, 1, build, 0)
            .Build()))!;

        Assert.Equal(2, universe.Sectors.Count);
        Assert.Equal((1, 1, SectorGates.South | SectorGates.East), (universe.Sectors[0].X, universe.Sectors[0].Y, universe.Sectors[0].Gates));
        Assert.Equal(3, Assert.Single(universe.Sectors[0].Objects).Source.Count);
        Assert.Equal((2, 1, SectorGates.West), (universe.Sectors[1].X, universe.Sectors[1].Y, universe.Sectors[1].Gates));

        // GS_AddGate(side 2, y − 1 = 0, x − 1 = 1): the west gate of the sector at (2, 1).
        Assert.Equal(new ScriptGate("Plot", 2, 1, SectorGates.West), Assert.Single(universe.ExtraGates));
    }

    private static void Emit(List<ushort> code, ScriptOpcode opcode, params int[] operands)
    {
        code.Add((ushort)opcode);
        foreach (int operand in operands)
            code.Add((ushort)operand);
    }

    private static void EmitJumpIfFalse(List<ushort> code, int target) =>
        code.AddRange([(ushort)ScriptOpcode.JumpIfFalse, (ushort)(target >> 16), (ushort)target]);

    /// <summary>
    /// A miniature of the game's script: a <c>BuildObject</c> switch from kinds to classes, one race
    /// created the way <c>Build</c> creates them, and editor data with one sector of every object type.
    /// </summary>
    [Fact]
    public void DecodesObjectsRacesAndOwners()
    {
        ScriptBuilder builder = new();
        int buildFromEditor = builder.Name(ScriptUniverse.BuilderClassMethod);
        int buildObject = builder.Name(ScriptUniverse.ObjectBuilderMethod);
        int build = builder.Name("Build");
        int readText = builder.Name(ScriptUniverse.ReadTextNative);

        List<ushort> code = [0x0000];

        int editorAddress = code.Count;
        EmitLiteral(code, new object[]
        {
            new object[]
            {
                1, 0, 0, 0,
                new object[] { 2, 7 },                                      // background, not a BuildObject kind
                new object[] { 5, 100, -5, -200, 0, 1, 318, 1, new object[] { new object[] { 8, 2 } } },
                new object[] { 5, 0, 0, 0, 2, 7, 376 },                     // pirate base: never the owner
                new object[]
                {
                    6, 0, 0, 0, 2, 1, 214,
                    7, new object[] { new object[] { 13, 0 } },
                    8, new object[] { new object[] { 11, 0 } },
                    2, new object[] { new object[] { 17, 0, 0, 0, 1, 0, 0 } },
                },
                new object[] { 6, 0, 0, 0, 0, 1, 310 },
                new object[] { 5, 0, 0, 0, 1, 8, 374 },
                new object[] { 17, 0, 0, 0, 1, 0, 0 },
            },
        });
        Emit(code, ScriptOpcode.StoreStack, 1);
        Emit(code, ScriptOpcode.Return);

        // BuildObject: Dup; kind; Equal; JumpIfFalse next — each case creating its classes.
        int objectAddress = code.Count;
        (int Kind, int[] Classes)[] cases = [(6, [2037, 2006]), (5, [2042, 2007]), (17, [2012])];
        List<int> patches = [];
        foreach ((int kind, int[] classes) in cases)
        {
            if (patches.Count > 0)
            {
                int here = code.Count;
                code[patches[^1]] = (ushort)(here >> 16);
                code[patches[^1] + 1] = (ushort)here;
            }

            Emit(code, ScriptOpcode.Dup);
            EmitLiteral(code, kind);
            Emit(code, ScriptOpcode.Equal);
            EmitJumpIfFalse(code, 0);
            patches.Add(code.Count - 2);
            Emit(code, ScriptOpcode.Pop);
            foreach (int type in classes)
            {
                Emit(code, ScriptOpcode.Push16, type);
                Emit(code, ScriptOpcode.New);
            }

            Emit(code, ScriptOpcode.Return);
        }

        int last = code.Count;
        code[patches[^1]] = (ushort)(last >> 16);
        code[patches[^1] + 1] = (ushort)last;
        Emit(code, ScriptOpcode.Return);

        // Build: SE_ReadText(751); index 1; 4; New 2003.
        int buildAddress = code.Count;
        Emit(code, ScriptOpcode.Push16, 751);
        EmitLiteral(code, 1);
        Emit(code, ScriptOpcode.CallNative32, 0, readText);
        EmitLiteral(code, 1);
        EmitLiteral(code, 4);
        Emit(code, ScriptOpcode.Push16, ScriptUniverse.RaceClass);
        Emit(code, ScriptOpcode.New);
        Emit(code, ScriptOpcode.Return);

        ScriptUniverse universe = ScriptUniverse.Read(ScriptFile.Parse(builder
            .Code([.. code])
            .Chunk("SYMB", 1, buildAddress, build, 0)
            .Chunk("OBJE", 1, 2000)
            .Chunk("SYMB", 2, editorAddress, buildFromEditor, 0, objectAddress, buildObject, 0)
            .Build()))!;

        Assert.Equal([2037, 2006], universe.KindClasses[6]);
        Assert.Equal([2012], universe.KindClasses[17]);
        Assert.Equal(751, universe.Races[1]);

        ScriptSector sector = Assert.Single(universe.Sectors);
        Assert.Equal(7, sector.Background);
        Assert.Equal(1, sector.Owner);
        Assert.Equal(
            [
                ScriptObjectType.TradingStation, ScriptObjectType.TradingStation, ScriptObjectType.Factory,
                ScriptObjectType.Shipyard, ScriptObjectType.TalkingStation, ScriptObjectType.Asteroid,
            ],
            sector.Objects.Select(o => o.Type));

        ScriptObject trading = sector.Objects[0];
        Assert.Equal((100, -5, -200, 1), (trading.East, trading.Up, trading.North, trading.Race));
        Assert.Equal(3181, trading.StationTextId);
        Assert.Equal(new ScriptWare(8, 2), Assert.Single(trading.Tradeables));
        Assert.Empty(trading.SecondaryResources);

        ScriptObject factory = sector.Objects[2];
        Assert.Equal((2141, 2143), (factory.StationTextId, factory.ProductTextId));
        Assert.Equal(new ScriptWare(13, 0), Assert.Single(factory.Products));
        Assert.Equal(new ScriptWare(11, 0), Assert.Single(factory.Resources));
        Assert.Equal(ScriptObjectType.Asteroid, Assert.Single(factory.Docked).Type);
        Assert.Null(sector.Objects[5].StationTextId);
    }

    [Theory]
    [InlineData(XGame.Tension, 8, 0, 2613)]       // alpha impulse ray emitter
    [InlineData(XGame.BeyondTheFrontier, 9, 0, 2613)]
    [InlineData(XGame.Tension, 13, 10, 2563)]     // swamp plant
    [InlineData(XGame.Tension, 16, 4, 2083)]      // computer components
    [InlineData(XGame.BeyondTheFrontier, 17, 4, 2083)]
    public void WaresFollowTextIdOrderWithinTheirCategory(XGame game, int mainType, int subType, int textId) =>
        Assert.Equal(textId, ScriptWares.TextId(game, new ScriptWare(mainType, subType)));

    [Theory]
    [InlineData(XGame.Tension, 12, 0)]            // a category nothing has decoded
    [InlineData(XGame.Tension, 16, 7)]            // equipment beyond the factory goods
    [InlineData(XGame.Tension, 9, 4)]             // past the last shield
    public void UndecodedWaresAreNull(XGame game, int mainType, int subType) =>
        Assert.Null(ScriptWares.TextId(game, new ScriptWare(mainType, subType)));

    [Fact]
    public void ScriptWithoutEditorDataHasNoUniverse()
    {
        ScriptFile script = ScriptFile.Parse(new ScriptBuilder().Code(0, (ushort)ScriptOpcode.Return).Build());

        Assert.Null(ScriptUniverse.Read(script));
    }

    private static ScriptInstruction[] Sweep(ushort[] code)
    {
        List<ScriptInstruction> result = [];
        for (int address = 0; address < code.Length;)
        {
            ScriptInstruction instruction = ScriptInstruction.Decode(code, address);
            result.Add(instruction);
            address += instruction.Length;
        }

        return result.ToArray();
    }
}

/// <summary>
/// The real scripts. Every word must decode and every jump and call must land on an instruction:
/// one wrong operand length would knock the sweep out of step and fail both.
/// </summary>
public sealed class ScriptInstallationTests
{
    public static TheoryData<string, string, int> KnownScripts => new()
    {
        // game key, entry, code words
        { "xbtf", "l/001.obj", 594_200 },
        { "xbtf", "l/002.obj", 17_643 },
        { "xtension", "l/001.obj", 871_085 },
    };

    [RequiresInstallTheory]
    [MemberData(nameof(KnownScripts))]
    public void SweepsCleanly(string gameKey, string entryPath, int codeWords)
    {
        XInstall? install = InstallProbe.ByKey(gameKey);
        if (install is null)
            return; // The other game is installed but not this one.

        using CatArchive archive = install.OpenArchive();
        Assert.True(archive.TryGetEntry(entryPath, out CatEntry entry));
        ScriptFile script = ScriptFile.Parse(archive.Read(entry));

        Assert.Equal(codeWords, script.Code.Count);

        // XBTF's l/002.obj has empty global tables and keeps all its code in class methods.
        ScriptFunction[] entryPoints = script.Functions.Concat(script.Classes.SelectMany(c => c.Functions)).ToArray();
        Assert.NotEmpty(entryPoints);

        HashSet<int> starts = [];
        List<ScriptInstruction> branches = [];

        foreach (ScriptInstruction instruction in script.Instructions())
        {
            // Word 0 may be the null return address rather than code.
            Assert.True(instruction.IsKnown || instruction.Address == 0,
                $"unknown word 0x{instruction.Word:X4} at {instruction.Address:X6}");
            starts.Add(instruction.Address);

            if (instruction.OperandKind == ScriptOperandKind.Address)
                branches.Add(instruction);
            else if (instruction.OperandKind == ScriptOperandKind.Name)
                Assert.False(string.IsNullOrEmpty(script.GetString(instruction.Operand)));
        }

        Assert.NotEmpty(branches);
        Assert.All(branches, b => Assert.Contains(b.Operand, starts));
        Assert.All(entryPoints, f => Assert.Contains(f.Address, starts));
    }

    public static TheoryData<string, int, int> KnownUniverses => new()
    {
        // game key, sectors in the editor data, gates the script adds with constant arguments
        { "xbtf", 54, 0 },
        { "xtension", 90, 1 },
    };

    public static TheoryData<string, int, int, int> KnownObjects => new()
    {
        // game key, stations, sectors with an owner, named races
        { "xbtf", 609, 49, 6 },
        { "xtension", 1_099, 81, 6 },
    };

    /// <summary>
    /// Every station has a kind <c>BuildObject</c> creates, every owner is a race the script names,
    /// and every factory's products decode to a ware.
    /// </summary>
    [RequiresInstallTheory]
    [MemberData(nameof(KnownObjects))]
    public void UniverseObjectsDecode(string gameKey, int stations, int owned, int races)
    {
        XInstall? install = InstallProbe.ByKey(gameKey);
        if (install is null)
            return;

        using CatArchive archive = install.OpenArchive();
        Assert.True(archive.TryGetEntry("l/001.obj", out CatEntry entry));
        ScriptUniverse universe = ScriptUniverse.Read(ScriptFile.Parse(archive.Read(entry)))!;

        ScriptObject[] all = universe.Sectors.SelectMany(s => s.Objects).ToArray();
        Assert.Equal(stations, all.Count(o => o.IsStation));
        Assert.Equal(owned, universe.Sectors.Count(s => s.Owner is not null));
        Assert.Equal(races, universe.Races.Count);
        Assert.All(universe.Sectors, s => Assert.NotNull(s.Background));
        Assert.All(universe.Sectors.Where(s => s.Owner is not null), s => Assert.Contains(s.Owner!.Value, universe.Races.Keys));
        Assert.DoesNotContain(all, o => o.Type == ScriptObjectType.Unknown);

        foreach (ScriptObject factory in all.Where(o => o.Type == ScriptObjectType.Factory))
        {
            ScriptWare product = Assert.Single(factory.Products);
            Assert.True(ScriptWares.TextId(install.Game, product) is not null || product.MainType == (install.Game == XGame.Tension ? 16 : 17),
                $"{gameKey}: product {product} of factory {factory.StationTextId} does not decode");
        }
    }

    /// <summary>
    /// Every gate must have its partner on the neighbour's facing side: a wrong bit-to-side mapping
    /// or a misread cell breaks that at once.
    /// </summary>
    [RequiresInstallTheory]
    [MemberData(nameof(KnownUniverses))]
    public void UniverseGatesPairUp(string gameKey, int sectors, int extraGates)
    {
        XInstall? install = InstallProbe.ByKey(gameKey);
        if (install is null)
            return;

        using CatArchive archive = install.OpenArchive();
        Assert.True(archive.TryGetEntry("l/001.obj", out CatEntry entry));
        ScriptUniverse universe = ScriptUniverse.Read(ScriptFile.Parse(archive.Read(entry)))!;

        Assert.Equal(sectors, universe.Sectors.Count);
        Assert.Equal(extraGates, universe.ExtraGates.Count);

        Dictionary<(int, int), SectorGates> cells = universe.Sectors.ToDictionary(s => (s.X, s.Y), s => s.Gates);
        (SectorGates Side, int Dx, int Dy, SectorGates Opposite)[] sides =
        [
            (SectorGates.North, 0, -1, SectorGates.South), (SectorGates.South, 0, 1, SectorGates.North),
            (SectorGates.West, -1, 0, SectorGates.East), (SectorGates.East, 1, 0, SectorGates.West),
        ];

        foreach (((int x, int y), SectorGates gates) in cells)
        foreach ((SectorGates side, int dx, int dy, SectorGates opposite) in sides)
        {
            if (!gates.HasFlag(side))
                continue;

            Assert.True(cells.TryGetValue((x + dx, y + dy), out SectorGates neighbour) && neighbour.HasFlag(opposite),
                $"{gameKey}: the {side} gate of ({x}, {y}) has no partner");
        }
    }
}
