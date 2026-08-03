using System.Globalization;
using System.Text;

namespace OpenXt.XArchive;

/// <summary>
/// The <c>t/*.txt</c> language tables: one <c>id&lt;TAB&gt;"string"</c> per line, Latin-1, CRLF.
///
/// File numbering follows ITU country codes — <c>44xxx</c> is English, <c>49xxx</c> is German.
/// Within a table the IDs are grouped by meaning: roughly 200-300 sector names, 2000-2999 station
/// and ware entries in blocks of ten, 3000-3999 ship names with the description at id+1, 10000+
/// comms menus, and six-digit ranges for dialogue and plot text.
/// </summary>
public sealed class TextTable
{
    private readonly Dictionary<int, string> _entries;

    private TextTable(Dictionary<int, string> entries) => _entries = entries;

    public int Count => _entries.Count;

    public IReadOnlyDictionary<int, string> Entries => _entries;

    public string? this[int id] => _entries.GetValueOrDefault(id);

    public static TextTable Parse(byte[] data) => Parse(Encoding.Latin1.GetString(data));

    public static TextTable Parse(string text)
    {
        Dictionary<int, string> entries = [];

        foreach (ReadOnlySpan<char> rawLine in text.AsSpan().EnumerateLines())
        {
            ReadOnlySpan<char> line = rawLine.TrimEnd('\r').Trim();
            if (line.IsEmpty)
                continue;

            // The two games separate the id from the value differently: XBTF writes a zero-padded
            // id and a tab ("000001\t\"Entering \""), X-Tension writes a bare id and spaces
            // ("1  \"Entering\""). Take the leading digit run and skip whatever whitespace follows.
            int digits = 0;
            while (digits < line.Length && char.IsAsciiDigit(line[digits]))
                digits++;

            if (digits == 0 || digits == line.Length)
                continue;

            if (!char.IsWhiteSpace(line[digits]))
                continue;

            if (!int.TryParse(line[..digits], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
                continue;

            ReadOnlySpan<char> value = line[digits..].Trim();

            // Values are quoted; strip the pair but leave any interior quotes alone.
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                value = value[1..^1];

            // Later files in the archive patch earlier ones, so last write wins.
            entries[id] = value.ToString();
        }

        return new TextTable(entries);
    }

    /// <summary>
    /// Merges another table over this one. Used to layer the archive's supplementary tables
    /// (44002, 44100, …) on top of the base 44001.
    /// </summary>
    public void Overlay(TextTable other)
    {
        foreach ((int id, string value) in other._entries)
            _entries[id] = value;
    }
}
