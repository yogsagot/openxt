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
}
