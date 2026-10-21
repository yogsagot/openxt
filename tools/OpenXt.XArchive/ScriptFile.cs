using System.Buffers.Binary;
using System.Text;

namespace OpenXt.XArchive;

/// <summary>
/// A compiled game script, <c>l/NNN.obj</c>: the bytecode that sets up the universe (sector owners,
/// gates, the plot) in both XBTF and X-Tension. Established by decompiling X-Tension's loader and
/// interpreter and confirmed against all three scripts the games ship; the opcodes are documented on
/// <see cref="ScriptOpcode"/>.
///
/// Layout: a run of chunks, each a four-character tag and a big-endian uint32 count. The loader
/// starts at the first <c>STO2</c> on an even offset and stops at the first tag it does not know.
/// <list type="bullet">
///   <item><c>STO2</c> — <i>count</i> big-endian 16-bit code words. Addresses are word indices.</item>
///   <item><c>CTIM</c> — one value, by its position the compile time.</item>
///   <item><c>SYMB</c> — functions, 12 bytes each: code address, name, and a third value
///     (small for global functions, so probably the argument count, but unconfirmed).</item>
///   <item><c>VARS</c> — variables, 12 bytes each: slot, name, initial value.</item>
///   <item><c>OBJE</c> / <c>OBJ2</c> / <c>OBJ3</c> — classes. Each record is an id; <c>OBJ2</c>
///     adds the parent's id (-1 for none) and <c>OBJ3</c> one more unknown value. Each record is
///     followed by its own <c>SYMB</c> and <c>VARS</c> chunks, any number, in any order.</item>
///   <item><c>PTCH</c> — patch records: id, pair count, then 8 bytes per pair. No shipped script
///     uses them.</item>
///   <item><c>STRG</c> — the string pool, to the end of the file. Names everywhere are byte
///     offsets into it, after unscrambling (see <see cref="DecodeStrings"/>).</item>
/// </list>
/// The sibling <c>l/NNN.pch</c> holds one 16-bit value per code word, by the look of it the stack
/// depth at each instruction; it is not needed to read the code.
/// </summary>
public sealed class ScriptFile
{
    private readonly byte[] _strings;

    private ScriptFile(ushort[] code, uint? compileTime, byte[] strings, IReadOnlyList<ScriptFunction> functions,
        IReadOnlyList<ScriptVariable> globals, IReadOnlyList<ScriptClass> classes)
    {
        Code = code;
        CompileTime = compileTime;
        _strings = strings;
        Functions = functions;
        Globals = globals;
        Classes = classes;
    }

    public IReadOnlyList<ushort> Code { get; }

    public uint? CompileTime { get; }

    /// <summary>Global functions, from the top-level <c>SYMB</c>.</summary>
    public IReadOnlyList<ScriptFunction> Functions { get; }

    /// <summary>Global variables, from the top-level <c>VARS</c>.</summary>
    public IReadOnlyList<ScriptVariable> Globals { get; }

    public IReadOnlyList<ScriptClass> Classes { get; }

    /// <summary>The NUL-terminated string at a pool offset, or null if the offset is outside it.</summary>
    public string? GetString(int offset) => ReadString(_strings, offset);

    private static string? ReadString(byte[] pool, int offset)
    {
        if ((uint)offset >= (uint)pool.Length)
            return null;

        int length = pool.AsSpan(offset).IndexOf((byte)0);
        return Encoding.Latin1.GetString(pool, offset, length < 0 ? pool.Length - offset : length);
    }

    public static ScriptFile Parse(ReadOnlySpan<byte> data)
    {
        int position = -1;
        for (int i = 0; i + 4 <= data.Length; i += 2)
        {
            if (data.Slice(i, 4).SequenceEqual("STO2"u8))
            {
                position = i;
                break;
            }
        }

        if (position < 0)
            throw new InvalidDataException("Not a compiled script: no STO2 chunk.");

        ushort[] code = [];
        uint? compileTime = null;
        byte[] strings = [];
        List<RawSymbol> functions = [];
        List<RawSymbol> globals = [];
        List<RawClass> classes = [];

        while (position + 4 <= data.Length)
        {
            ReadOnlySpan<byte> tag = data.Slice(position, 4);

            if (tag.SequenceEqual("STRG"u8))
            {
                strings = DecodeStrings(data[(position + 4)..]);
                break;
            }

            if (tag.SequenceEqual("STO2"u8))
            {
                int count = Count(data, ref position, 2);
                code = new ushort[count];
                for (int i = 0; i < count; i++)
                    code[i] = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(position + i * 2));
                position += count * 2;
            }
            else if (tag.SequenceEqual("CTIM"u8))
            {
                compileTime = (uint)Count(data, ref position, 0);
            }
            else if (tag.SequenceEqual("SYMB"u8))
            {
                ReadSymbols(data, ref position, functions);
            }
            else if (tag.SequenceEqual("VARS"u8))
            {
                ReadSymbols(data, ref position, globals);
            }
            else if (tag.SequenceEqual("OBJE"u8) || tag.SequenceEqual("OBJ2"u8) || tag.SequenceEqual("OBJ3"u8))
            {
                ReadClasses(data, ref position, classes);
            }
            else if (tag.SequenceEqual("PTCH"u8))
            {
                SkipPatches(data, ref position);
            }
            else
            {
                break;
            }
        }

        // Names resolve once the pool is in hand, and the pool comes last.
        ScriptFunction[] Functions(List<RawSymbol> raw) =>
            raw.Select(s => new ScriptFunction(ReadString(strings, s.Name) ?? "", s.Value, s.Extra)).ToArray();
        ScriptVariable[] Variables(List<RawSymbol> raw) =>
            raw.Select(s => new ScriptVariable(ReadString(strings, s.Name) ?? "", s.Value, s.Extra)).ToArray();

        return new ScriptFile(code, compileTime, strings, Functions(functions), Variables(globals),
            classes.Select(c => new ScriptClass(c.Id, c.ParentId, c.Extra, Functions(c.Functions), Variables(c.Fields)))
                .ToArray());
    }

    /// <summary>
    /// Unscrambles the string pool. Working from the end, each byte becomes its complement minus the
    /// (still scrambled) byte before it; the first byte is just complemented.
    /// </summary>
    public static byte[] DecodeStrings(ReadOnlySpan<byte> scrambled)
    {
        byte[] plain = scrambled.ToArray();
        for (int i = plain.Length - 1; i > 0; i--)
            plain[i] = (byte)(~plain[i] - plain[i - 1]);
        if (plain.Length > 0)
            plain[0] = (byte)~plain[0];
        return plain;
    }

    /// <summary>Linear sweep over the code. Words that are not an opcode come back as one-word unknowns.</summary>
    public IEnumerable<ScriptInstruction> Instructions()
    {
        int address = 0;
        while (address < Code.Count)
        {
            ScriptInstruction instruction = ScriptInstruction.Decode(Code, address);
            yield return instruction;
            address += instruction.Length;
        }
    }

    /// <summary>Reads a chunk's count and steps past the header, checking <paramref name="itemSize"/>-byte items fit.</summary>
    private static int Count(ReadOnlySpan<byte> data, ref int position, int itemSize)
    {
        if (position + 8 > data.Length)
            throw new InvalidDataException($"Chunk header at {position} is truncated.");

        uint count = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(position + 4));
        position += 8;

        if ((long)count * itemSize > data.Length - position)
        {
            throw new InvalidDataException(
                $"{Encoding.ASCII.GetString(data.Slice(position - 8, 4))} chunk claims {count} items " +
                $"of {itemSize} bytes but {data.Length - position} remain.");
        }

        return (int)count;
    }

    private static int ReadInt(ReadOnlySpan<byte> data, ref int position)
    {
        if (position + 4 > data.Length)
            throw new InvalidDataException($"Value at {position} is truncated.");

        int value = BinaryPrimitives.ReadInt32BigEndian(data.Slice(position));
        position += 4;
        return value;
    }

    private static void ReadSymbols(ReadOnlySpan<byte> data, ref int position, List<RawSymbol> into)
    {
        int count = Count(data, ref position, 12);
        for (int i = 0; i < count; i++)
        {
            int value = ReadInt(data, ref position);
            int name = ReadInt(data, ref position);
            int extra = ReadInt(data, ref position);
            into.Add(new RawSymbol(value, name, extra));
        }
    }

    private static void ReadClasses(ReadOnlySpan<byte> data, ref int position, List<RawClass> into)
    {
        bool hasParent = !data.Slice(position, 4).SequenceEqual("OBJE"u8);
        bool hasExtra = data.Slice(position, 4).SequenceEqual("OBJ3"u8);
        int count = Count(data, ref position, 4);

        for (int i = 0; i < count; i++)
        {
            RawClass type = new(ReadInt(data, ref position),
                hasParent ? ReadInt(data, ref position) : -1,
                hasExtra ? ReadInt(data, ref position) : 0);

            while (position + 4 <= data.Length)
            {
                if (data.Slice(position, 4).SequenceEqual("SYMB"u8))
                    ReadSymbols(data, ref position, type.Functions);
                else if (data.Slice(position, 4).SequenceEqual("VARS"u8))
                    ReadSymbols(data, ref position, type.Fields);
                else
                    break;
            }

            into.Add(type);
        }
    }

    private static void SkipPatches(ReadOnlySpan<byte> data, ref int position)
    {
        int count = Count(data, ref position, 8);
        for (int i = 0; i < count; i++)
        {
            ReadInt(data, ref position);
            int pairs = ReadInt(data, ref position);
            if (pairs < 0 || (long)pairs * 8 > data.Length - position)
                throw new InvalidDataException($"PTCH record at {position} claims {pairs} pairs.");
            position += pairs * 8;
        }
    }

    private readonly record struct RawSymbol(int Value, int Name, int Extra);

    private sealed record RawClass(int Id, int ParentId, int Extra)
    {
        public List<RawSymbol> Functions { get; } = [];
        public List<RawSymbol> Fields { get; } = [];
    }
}

/// <summary>A function: where its code starts, and a third value of unconfirmed meaning.</summary>
public sealed record ScriptFunction(string Name, int Address, int Extra);

/// <summary>A variable: its slot and its initial value.</summary>
public sealed record ScriptVariable(string Name, int Slot, int InitialValue);

/// <summary>A class. Field slots continue the parent's, so a class's slots start after its parent's last.</summary>
public sealed record ScriptClass(int Id, int ParentId, int Extra,
    IReadOnlyList<ScriptFunction> Functions, IReadOnlyList<ScriptVariable> Fields);
