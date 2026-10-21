namespace OpenXt.XArchive;

/// <summary>
/// The <c>STO2</c> instruction set, read from X-Tension's interpreter (the big switch at
/// <c>0x0043AC70</c> in <c>X-TENSION.exe</c>) and checked by a full linear sweep of every shipped
/// script in both games: every word decodes, and every jump and call lands on an instruction.
///
/// A stack machine over 32-bit slots. An instruction is one opcode word and up to four operand
/// words; 32-bit operands are two words, high first. Operands that name something — a native, a
/// method, a global function — are byte offsets into the string pool. XBTF's compiler only emits
/// the 16-bit name forms; X-Tension's emits the 32-bit ones.
///
/// Calls push the argument count after the arguments; the callee's <see cref="Return"/> pops the
/// arguments, the count and the frame and leaves the result in their place. A return address of
/// 0 ends the thread, which is why code word 0 is never executed.
///
/// Names describe what the interpreter does. Where the opcode only hands off to a helper that has
/// not been decompiled yet, the name is the opcode and the summary gives the stack effect.
/// </summary>
public enum ScriptOpcode : ushort
{
    /// <summary>Push the operand, sign-extended. 1 operand word.</summary>
    Push16 = 0x01,

    /// <summary>Push a 32-bit operand. 2 operand words.</summary>
    Push32 = 0x02,

    /// <summary>Same as <see cref="Push16"/>: both fall to the interpreter's default case.</summary>
    Push16Alt = 0x03,

    /// <summary>Push global variable [operand].</summary>
    LoadGlobal = 0x04,

    /// <summary>Push the slot [operand − 1] below the stack top: locals and arguments, by depth.</summary>
    LoadStack = 0x05,

    /// <summary>Push field [operand] of <c>this</c>.</summary>
    LoadField = 0x06,

    Dup = 0x07,

    /// <summary>Pops two, pushes one (helper at <c>0x00435AC0</c>).</summary>
    Op08 = 0x08,

    /// <summary>Push 0. <c>0x09</c>–<c>0x28</c> push 0–31; see <see cref="ScriptInstruction.SmallConstant"/>.</summary>
    Const0 = 0x09,
    Const31 = 0x28,

    /// <summary>Store the top into global [operand]; the value stays on the stack.</summary>
    StoreGlobal = 0x29,

    /// <summary>Store the top into the slot [operand − 1] below it; the value stays.</summary>
    StoreStack = 0x2A,

    /// <summary>Store the top into field [operand] of <c>this</c>; the value stays.</summary>
    StoreField = 0x2B,

    Pop = 0x2C,

    /// <summary>Pops three, pushes one (helper at <c>0x00435B10</c>).</summary>
    Op2D = 0x2D,

    /// <summary>Drop [operand] slots.</summary>
    PopN = 0x2E,

    /// <summary>Pops a count and that many values, pushes one (helper at <c>0x004359F0</c>).</summary>
    Op2F = 0x2F,

    /// <summary>Pops a count and that many values, pushes one (helper at <c>0x00438FA0</c>).</summary>
    Op30 = 0x30,

    /// <summary>Jump to the 32-bit word address.</summary>
    Jump = 0x31,

    /// <summary>Pop; jump if it was non-zero.</summary>
    JumpIfTrue = 0x32,

    /// <summary>Pop; jump if it was zero.</summary>
    JumpIfFalse = 0x33,

    /// <summary>Call the global function named by the operand. Unknown name: pops the arguments, pushes 0.</summary>
    CallGlobal = 0x34,

    /// <summary>Return the top to the caller, or end the thread if the return address is 0.</summary>
    Return = 0x35,

    /// <summary>
    /// Call the engine function named by the operand, searching 21 native libraries. The top is the
    /// argument count. The engine may suspend the thread here (<c>P_Delay</c>). Once resolved, the
    /// game patches the operand in memory to <c>0x80 | library</c>, index — files hold the name.
    /// </summary>
    CallNative = 0x36,

    /// <summary>Call the method named by the operand on the object on top, walking up its classes.</summary>
    CallMethod = 0x37,

    /// <summary>Call the 32-bit address with no <c>this</c>. A third operand word is unused.</summary>
    Call = 0x38,

    /// <summary>Call the 32-bit address on the object on top. A third operand word is unused.</summary>
    CallObject = 0x39,

    /// <summary>Pop a class id, push a new instance's handle (0 if the class is unknown).</summary>
    New = 0x3A,

    /// <summary>Pop a handle and destroy the object.</summary>
    Delete = 0x3B,

    /// <summary>Push <c>this</c>'s handle, or −1.</summary>
    This = 0x3C,

    /// <summary>Push −1, the null handle.</summary>
    Null = 0x3D,

    Add = 0x3E,
    Subtract = 0x3F,
    Multiply = 0x40,

    /// <summary>Signed. Division by zero ends the thread.</summary>
    Divide = 0x41,

    /// <summary>Signed. Division by zero ends the thread.</summary>
    Modulo = 0x42,

    BitOr = 0x43,
    BitAnd = 0x44,
    BitXor = 0x45,
    LogicalOr = 0x46,
    LogicalAnd = 0x47,
    ShiftLeft = 0x48,

    /// <summary>Arithmetic shift.</summary>
    ShiftRight = 0x49,

    LessThan = 0x4A,
    GreaterThan = 0x4B,
    LessOrEqual = 0x4C,
    GreaterOrEqual = 0x4D,
    NotEqual = 0x4E,
    Equal = 0x4F,
    Negate = 0x50,
    LogicalNot = 0x51,
    BitNot = 0x52,

    /// <summary>Start a new thread on a copy of the stack; the parent gets the new thread's id.</summary>
    Fork = 0x53,

    /// <summary>End the thread. Any undefined opcode does the same.</summary>
    End = 0x54,

    /// <summary>Make sure [operand] more slots fit, growing the stack if not.</summary>
    Reserve = 0x55,

    /// <summary>Same as <see cref="Push32"/>.</summary>
    Push32Alt = 0x56,

    /// <summary><see cref="CallMethod"/> with a 32-bit name.</summary>
    CallMethod32 = 0x57,

    /// <summary><see cref="Call"/> with two unused words instead of one.</summary>
    CallLong = 0x58,

    /// <summary><see cref="CallObject"/> with two unused words instead of one.</summary>
    CallObjectLong = 0x59,

    /// <summary><see cref="CallNative"/> with a 32-bit name.</summary>
    CallNative32 = 0x5A,

    /// <summary><see cref="CallGlobal"/> with a 32-bit name.</summary>
    CallGlobal32 = 0x5B,
}

/// <summary>What an instruction's operand means, which decides how a listing shows it.</summary>
public enum ScriptOperandKind
{
    None,
    Number,
    Address,
    Name,
}

/// <summary>One decoded instruction. <see cref="Operand"/> is sign-extended when it is a single word.</summary>
public readonly record struct ScriptInstruction(int Address, ushort Word, int Operand, int Length)
{
    public ScriptOpcode Opcode => (ScriptOpcode)Word;

    public bool IsKnown => Word is >= 0x01 and <= 0x5B;

    /// <summary>The constant a <see cref="ScriptOpcode.Const0"/>-range opcode pushes, or null.</summary>
    public int? SmallConstant => Word is >= (ushort)ScriptOpcode.Const0 and <= (ushort)ScriptOpcode.Const31
        ? Word - (int)ScriptOpcode.Const0
        : null;

    public ScriptOperandKind OperandKind => Opcode switch
    {
        _ when !IsKnown => ScriptOperandKind.None,
        ScriptOpcode.Jump or ScriptOpcode.JumpIfTrue or ScriptOpcode.JumpIfFalse
            or ScriptOpcode.Call or ScriptOpcode.CallObject or ScriptOpcode.CallLong or ScriptOpcode.CallObjectLong
            => ScriptOperandKind.Address,
        ScriptOpcode.CallGlobal or ScriptOpcode.CallGlobal32 or ScriptOpcode.CallNative or ScriptOpcode.CallNative32
            or ScriptOpcode.CallMethod or ScriptOpcode.CallMethod32
            => ScriptOperandKind.Name,
        _ => OperandWords(Word) > 0 ? ScriptOperandKind.Number : ScriptOperandKind.None,
    };

    public static ScriptInstruction Decode(IReadOnlyList<ushort> code, int address)
    {
        ushort word = code[address];
        int words = OperandWords(word);

        // A truncated tail decodes as an unknown word rather than reading past the end.
        if (address + words >= code.Count)
            return new ScriptInstruction(address, word, 0, 1);

        int operand = words switch
        {
            0 => 0,
            1 => (short)code[address + 1],
            // 32-bit operands are high word first; trailing words of the long calls are unused.
            _ => (code[address + 1] << 16) | code[address + 2],
        };

        return new ScriptInstruction(address, word, operand, 1 + words);
    }

    /// <summary>Operand words after the opcode, as the interpreter steps over them.</summary>
    public static int OperandWords(ushort word) => (ScriptOpcode)word switch
    {
        ScriptOpcode.Push16 or ScriptOpcode.Push16Alt or ScriptOpcode.LoadGlobal or ScriptOpcode.LoadStack
            or ScriptOpcode.LoadField or ScriptOpcode.StoreGlobal or ScriptOpcode.StoreStack
            or ScriptOpcode.StoreField or ScriptOpcode.PopN or ScriptOpcode.CallGlobal
            or ScriptOpcode.CallNative or ScriptOpcode.CallMethod or ScriptOpcode.Reserve => 1,
        ScriptOpcode.Push32 or ScriptOpcode.Push32Alt or ScriptOpcode.Jump or ScriptOpcode.JumpIfTrue
            or ScriptOpcode.JumpIfFalse or ScriptOpcode.CallMethod32 or ScriptOpcode.CallNative32
            or ScriptOpcode.CallGlobal32 => 2,
        ScriptOpcode.Call or ScriptOpcode.CallObject => 3,
        ScriptOpcode.CallLong or ScriptOpcode.CallObjectLong => 4,
        _ => 0,
    };
}
