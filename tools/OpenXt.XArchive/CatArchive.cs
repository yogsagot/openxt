using System.Text;

namespace OpenXt.XArchive;

/// <summary>One file inside a <see cref="CatArchive"/>.</summary>
/// <param name="Path">Archive-relative path, e.g. <c>v/00000.pbd</c>. Separators are always '/'.</param>
/// <param name="Offset">Byte offset into the .dat blob.</param>
/// <param name="Size">Length in bytes.</param>
public readonly record struct CatEntry(string Path, long Offset, int Size);

/// <summary>
/// A <c>01.cat</c> / <c>01.dat</c> pair, the only container XBTF and X-Tension ship.
///
/// The .cat is an index encrypted with a positional XOR; the .dat is one blob encrypted with a
/// single constant. Offsets are not stored — they are the running sum of the entry sizes, which is
/// why <see cref="Open"/> checks that sum against the real .dat length before trusting anything.
/// </summary>
public sealed class CatArchive : IDisposable
{
    /// <summary>Index cipher: each byte is XORed with its own position plus a fixed seed.</summary>
    private const byte CatKeySeed = 0xDB;

    /// <summary>Blob cipher: a single constant across the whole .dat.</summary>
    internal const byte DatKey = 0x33;

    private readonly Stream _dat;
    private readonly Dictionary<string, CatEntry> _byPath;

    public string CatPath { get; }
    public string DatPath { get; }

    /// <summary>
    /// Every line of the index, including superseded duplicates. Superseded entries still occupy
    /// bytes in the .dat, so this — not <see cref="Entries"/> — is what the offsets are built from.
    /// </summary>
    public IReadOnlyList<CatEntry> AllEntries { get; }

    /// <summary>
    /// The live entries: duplicates resolved last-wins. Iterate this to read content; X-Tension's
    /// index is an append log where a later line replaces an earlier one of the same name.
    /// </summary>
    public IReadOnlyList<CatEntry> Entries { get; }

    private CatArchive(string catPath, string datPath, Stream dat, List<CatEntry> all)
    {
        CatPath = catPath;
        DatPath = datPath;
        _dat = dat;
        AllEntries = all;

        // Paths mix cases across the archives (.jpg/.JPG, .siz/.SIZ), so lookups ignore case.
        _byPath = new Dictionary<string, CatEntry>(all.Count, StringComparer.OrdinalIgnoreCase);

        // Keep each name at the position of its first appearance so listings stay stable, but
        // carry the last entry's offset and size.
        List<CatEntry> live = [];
        Dictionary<string, int> slot = new(StringComparer.OrdinalIgnoreCase);

        foreach (CatEntry entry in all)
        {
            _byPath[entry.Path] = entry;

            if (slot.TryGetValue(entry.Path, out int index))
            {
                live[index] = entry;
            }
            else
            {
                slot[entry.Path] = live.Count;
                live.Add(entry);
            }
        }

        Entries = live;
    }

    public static CatArchive Open(string catPath)
    {
        string datPath = Path.ChangeExtension(catPath, ".dat");
        if (!File.Exists(datPath))
        {
            // The archives ship lowercase on Steam but the originals were mixed-case.
            string upper = Path.ChangeExtension(catPath, ".DAT");
            if (!File.Exists(upper))
                throw new FileNotFoundException($"No .dat alongside '{catPath}'.", datPath);
            datPath = upper;
        }

        List<CatEntry> all = ParseIndex(File.ReadAllBytes(catPath), catPath);

        FileStream dat = File.Open(datPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            long total = 0;
            foreach (CatEntry entry in all)
                total += entry.Size;

            // The one integrity check the format affords us. Entry sizes must tile the .dat exactly:
            // no header, no padding, no alignment. A mismatch means we mis-parsed the index.
            if (total != dat.Length)
            {
                throw new InvalidDataException(
                    $"'{catPath}' lists {total:N0} bytes but '{datPath}' is {dat.Length:N0} bytes. " +
                    "The index did not decode correctly.");
            }

            return new CatArchive(catPath, datPath, dat, all);
        }
        catch
        {
            dat.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Decrypts and parses the index. Line 1 names the .dat; every later line is "<c>path size</c>".
    /// </summary>
    private static List<CatEntry> ParseIndex(byte[] encrypted, string catPath)
    {
        byte[] plain = new byte[encrypted.Length];
        for (int i = 0; i < encrypted.Length; i++)
            plain[i] = (byte)(encrypted[i] ^ (byte)(CatKeySeed + i));

        // Latin-1: the archives predate Unicode and a few paths carry high bytes.
        string text = Encoding.Latin1.GetString(plain);

        // Every line, duplicates included: each one consumes its own bytes of the .dat, so dropping
        // superseded entries here would shift every subsequent offset.
        List<CatEntry> all = [];
        long offset = 0;
        bool first = true;

        foreach (ReadOnlySpan<char> rawLine in text.AsSpan().EnumerateLines())
        {
            // Line endings are mixed \n and \r\n within a single file.
            ReadOnlySpan<char> line = rawLine.TrimEnd('\r').Trim();
            if (line.IsEmpty)
                continue;

            if (first)
            {
                // Header names the blob (always "01.dat"); it consumes no bytes of it.
                first = false;
                continue;
            }

            // Split on the LAST space: archive paths may legitimately contain spaces.
            int split = line.LastIndexOf(' ');
            if (split <= 0 || !int.TryParse(line[(split + 1)..], out int size) || size < 0)
                throw new InvalidDataException($"Malformed index line in '{catPath}': '{line}'");

            string path = line[..split].Trim().ToString().Replace('\\', '/');
            all.Add(new CatEntry(path, offset, size));
            offset += size;
        }

        return all;
    }

    public bool Contains(string path) => _byPath.ContainsKey(path);

    public bool TryGetEntry(string path, out CatEntry entry) => _byPath.TryGetValue(path, out entry);

    /// <summary>Reads and decrypts one entry. Returns a fresh array; nothing is cached.</summary>
    public byte[] Read(string path) =>
        _byPath.TryGetValue(path, out CatEntry entry)
            ? Read(entry)
            : throw new FileNotFoundException($"'{path}' is not in {Path.GetFileName(CatPath)}.", path);

    public byte[] Read(CatEntry entry)
    {
        byte[] buffer = new byte[entry.Size];
        _dat.Seek(entry.Offset, SeekOrigin.Begin);
        _dat.ReadExactly(buffer);

        for (int i = 0; i < buffer.Length; i++)
            buffer[i] ^= DatKey;

        return buffer;
    }

    /// <summary>Entries under <paramref name="prefix"/>, e.g. <c>"v/"</c> or <c>"tex/true/"</c>.</summary>
    public IEnumerable<CatEntry> EnumerateUnder(string prefix)
    {
        foreach (CatEntry entry in Entries)
        {
            if (entry.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                yield return entry;
        }
    }

    public void Dispose() => _dat.Dispose();
}
