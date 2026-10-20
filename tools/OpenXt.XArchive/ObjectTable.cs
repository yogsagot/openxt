using System.Buffers.Binary;

namespace OpenXt.XArchive;

/// <summary>
/// What the executables' object tables (ships, stations, wares) have in common: fixed-size records
/// of little-endian int32s, found by the text id one known record carries.
/// </summary>
internal static class ObjectTable
{
    /// <summary>Byte offset of the text id within ship and station records ([6]).</summary>
    public const int TextIdOffset = 24;

    public static int[] ReadRaw(ReadOnlySpan<byte> exe, int offset, int stride)
    {
        int[] raw = new int[stride / 4];
        for (int i = 0; i < raw.Length; i++)
            raw[i] = BinaryPrimitives.ReadInt32LittleEndian(exe.Slice(offset + i * 4));
        return raw;
    }

    public static int ReadInt(ReadOnlySpan<byte> exe, int offset) =>
        offset >= 0 && offset + 4 <= exe.Length ? BinaryPrimitives.ReadInt32LittleEndian(exe.Slice(offset)) : 0;

    /// <summary>
    /// Start of the first record whose text-id slot holds <paramref name="textId"/> and which is
    /// followed by another record. One neighbour is enough to reject the stray constants elsewhere
    /// in the image. -1 if there is none.
    /// </summary>
    public static int FindRecord(ReadOnlySpan<byte> exe, int stride, int textId, int textIdOffset = TextIdOffset)
    {
        Span<byte> needle = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(needle, textId);

        int searched = 0;
        while (searched < exe.Length)
        {
            int hit = exe[searched..].IndexOf(needle);
            if (hit < 0)
                return -1;

            int at = searched + hit;
            int start = at - textIdOffset;

            if (start >= 0 && at % 4 == 0 && IsObjectTextId(ReadInt(exe, at + stride)))
                return start;

            searched = at + 1;
        }

        return -1;
    }

    /// <summary>Ship, station and ware names live at 2000-3999 in the text tables; see <see cref="TextTable"/>.</summary>
    public static bool IsObjectTextId(int id) => id is >= 2000 and < 4000;
}
