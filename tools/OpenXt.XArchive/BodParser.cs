using System.Globalization;
using System.Numerics;
using System.Text;

namespace OpenXt.XArchive;

/// <summary>
/// Parser for the text body format found in <c>v/*.pbd</c>.
///
/// Layout, verified against every body file in both games (1,124 bodies in XBTF, 1,310 in
/// X-Tension, zero unparsed records):
///
/// <code>
/// / free comment lines
/// MATERIAL3: idx;texId; ambient r;g;b; diffuse r;g;b; specular r;g;b; ...
/// 1000;                                  &lt;- optional size hint
/// x; y; z;                               &lt;- vertex list
/// -1;  -1;   -1;                         &lt;- end of coords
/// /----- Part 0: (a / b / c)"B_Torna"    &lt;- part name lives in a comment
/// mat; v0; v1; v2; -25; flags; u0;v0; u1;v1; u2;v2;
/// -99; 0000000000010001;                 &lt;- end of part
/// -99; 0000000000000000;                 &lt;- end of body (always follows the last part's terminator)
/// </code>
///
/// Two rules are load-bearing and were both established empirically rather than assumed:
///
/// 1. <b>A body ends at two consecutive <c>-99;</c> lines</b> — the last part's terminator followed
///    by the body's own. Do not treat an all-zero flag mask as the body terminator: 13 XBTF files
///    contain a part whose flags are legitimately zero.
/// 2. <b>Face records are variable width.</b> Read the material index, then vertex indices until a
///    negative marker, then decide from what is left: 2N+1 trailing fields means flags followed by
///    UVs, 2N means UVs alone, 1 means flags alone, 0 means neither.
/// </summary>
public static class BodParser
{
    public static BodFile Parse(byte[] utf8OrLatin1) => Parse(Encoding.Latin1.GetString(utf8OrLatin1));

    public static BodFile Parse(string text)
    {
        BodFile file = new();
        StringBuilder title = new();

        BodBody? body = null;
        BodPart? part = null;
        string? pendingPartName = null;
        bool inParts = false;
        bool previousWasTerminator = false;
        bool capturingTitle = true;

        // Hoisted out of the loop: a stackalloc inside it would grow the frame on every line.
        Span<float> numbers = stackalloc float[4];

        foreach (ReadOnlySpan<char> rawLine in text.AsSpan().EnumerateLines())
        {
            ReadOnlySpan<char> line = rawLine.TrimEnd('\r');

            // A '/' starts a comment, either whole-line or trailing. Part names hide in them.
            int comment = line.IndexOf('/');
            ReadOnlySpan<char> commentText = comment >= 0 ? line[(comment + 1)..] : default;
            ReadOnlySpan<char> data = comment >= 0 ? line[..comment] : line;
            data = data.Trim();

            if (comment >= 0)
            {
                if (TryReadPartName(commentText, out string? name))
                    pendingPartName = name;
                // Only the first meaningful header comment. Some files carry commented-out
                // MATERIAL lines and 3ds paths below it, which are not part of the name.
                else if (capturingTitle && title.Length == 0 && data.IsEmpty)
                    AppendTitle(title, commentText);
            }

            if (data.IsEmpty)
                continue;

            // The title is the header comment only. Separators like "---- Next Body of Bodyarray!"
            // sit between bodies and must not be glued onto it.
            capturingTitle = false;

            if (data.StartsWith("MATERIAL", StringComparison.OrdinalIgnoreCase))
            {
                if (TryParseMaterial(data, out BodMaterial? material) && material is not null)
                    file.Materials.Add(material);
                continue;
            }

            // Scene files (VER:/P;/B;) share the .pbd extension but are a different format.
            if (data.StartsWith("VER", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("This .pbd is a scene, not a body. Use SceneParser.");

            if (!inParts)
            {
                body ??= new BodBody();

                if (IsEndOfCoords(data))
                {
                    inParts = true;
                    previousWasTerminator = false;
                    part = null;
                    continue;
                }

                int count = ReadNumbers(data, numbers);

                if (count == 3)
                    body.Vertices.Add(new Vector3(numbers[0], numbers[1], numbers[2]));
                else if (count == 1)
                    body.SizeHint = (int)numbers[0];

                continue;
            }

            if (data.StartsWith("-99", StringComparison.Ordinal))
            {
                if (previousWasTerminator)
                {
                    // Second terminator in a row: the body is finished.
                    file.Bodies.Add(body!);
                    body = null;
                    part = null;
                    inParts = false;
                    previousWasTerminator = false;
                }
                else
                {
                    if (part is not null)
                        part.TerminatorFlags = ReadTerminatorFlags(data);
                    previousWasTerminator = true;
                    part = null;
                }

                continue;
            }

            previousWasTerminator = false;

            BodFace? face = ParseFace(data);
            if (face is null)
                continue;

            if (part is null)
            {
                part = new BodPart { Name = pendingPartName };
                pendingPartName = null;
                body!.Parts.Add(part);
            }

            part.Faces.Add(face);
        }

        // Tolerate a body that runs to EOF without its closing terminator.
        if (body is not null && (body.Vertices.Count > 0 || body.Parts.Count > 0))
            file.Bodies.Add(body);

        file.Title = title.Length > 0 ? title.ToString() : null;
        return file;
    }

    /// <summary>Matches the "-1; -1; -1;" line that closes a vertex list.</summary>
    private static bool IsEndOfCoords(ReadOnlySpan<char> data)
    {
        Span<float> numbers = stackalloc float[4];
        return ReadNumbers(data, numbers) == 3 && numbers is [-1f, -1f, -1f, ..];
    }

    /// <summary>
    /// Parses one face. Returns null for a line that is not a face record rather than throwing,
    /// so an unexpected stray line costs one polygon instead of the whole model.
    /// </summary>
    private static BodFace? ParseFace(ReadOnlySpan<char> data)
    {
        Span<Range> fields = stackalloc Range[24];
        int fieldCount = SplitFields(data, fields);
        if (fieldCount < 2)
            return null;

        // Field 0 is the material slot and may be negative (33 such faces exist in XBTF).
        if (!int.TryParse(data[fields[0]], NumberStyles.Integer, CultureInfo.InvariantCulture, out int material))
            return null;

        // Vertex indices run until the negative marker.
        int cursor = 1;
        Span<int> indices = stackalloc int[4];
        int indexCount = 0;

        while (cursor < fieldCount && indexCount < indices.Length)
        {
            ReadOnlySpan<char> token = data[fields[cursor]];
            if (token.Length == 0 || token[0] == '-')
                break;
            if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
                return null;

            indices[indexCount++] = index;
            cursor++;
        }

        if (indexCount < 3 || cursor >= fieldCount)
            return null;

        if (!int.TryParse(data[fields[cursor]], NumberStyles.Integer, CultureInfo.InvariantCulture, out int marker)
            || marker >= 0)
        {
            return null;
        }

        cursor++;

        int trailing = fieldCount - cursor;
        int uvFloats = indexCount * 2;

        // 2N+1 -> flags then UVs; 2N -> UVs only; 1 -> flags only; 0 -> neither.
        bool hasFlags = trailing == uvFloats + 1 || trailing == 1;
        bool hasUvs = trailing >= uvFloats && uvFloats > 0;

        int? flags = null;
        if (hasFlags)
        {
            if (int.TryParse(data[fields[cursor]], NumberStyles.Integer, CultureInfo.InvariantCulture, out int f))
                flags = f;
            cursor++;
        }

        Vector2[]? uvs = null;
        if (hasUvs && fieldCount - cursor >= uvFloats)
        {
            uvs = new Vector2[indexCount];
            for (int i = 0; i < indexCount; i++)
            {
                float u = ParseFloat(data[fields[cursor++]]);
                float v = ParseFloat(data[fields[cursor++]]);
                uvs[i] = new Vector2(u, v);
            }
        }

        return new BodFace
        {
            MaterialIndex = material,
            VertexIndices = indices[..indexCount].ToArray(),
            Marker = marker,
            Flags = flags,
            Uvs = uvs,
        };
    }

    private static bool TryParseMaterial(ReadOnlySpan<char> data, out BodMaterial? material)
    {
        material = null;

        int colon = data.IndexOf(':');
        if (colon < 0)
            return false;

        Span<Range> fields = stackalloc Range[32];
        ReadOnlySpan<char> body = data[(colon + 1)..];
        int count = SplitFields(body, fields);

        // The first eleven fields are common to MATERIAL, MATERIAL2 and MATERIAL3.
        if (count < 11)
            return false;

        Span<int> values = stackalloc int[11];
        for (int i = 0; i < 11; i++)
        {
            if (!int.TryParse(body[fields[i]], NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i]))
                return false;
        }

        material = new BodMaterial(
            values[0],
            values[1],
            BodMaterial.Colour(values[2], values[3], values[4]),
            BodMaterial.Colour(values[5], values[6], values[7]),
            BodMaterial.Colour(values[8], values[9], values[10]));

        return true;
    }

    /// <summary>Reads the 16-character binary mask on a "-99; 0000000000010001;" line.</summary>
    private static int ReadTerminatorFlags(ReadOnlySpan<char> data)
    {
        Span<Range> fields = stackalloc Range[8];
        int count = SplitFields(data, fields);
        if (count < 2)
            return 0;

        int value = 0;
        foreach (char c in data[fields[1]])
        {
            if (c is '0' or '1')
                value = (value << 1) | (c - '0');
        }

        return value;
    }

    /// <summary>Pulls the quoted name out of a <c>----- Part 3: (1 / 0 / 0)"HalterA01"</c> comment.</summary>
    private static bool TryReadPartName(ReadOnlySpan<char> comment, out string? name)
    {
        name = null;

        ReadOnlySpan<char> trimmed = comment.TrimStart('-').TrimStart();
        if (!trimmed.StartsWith("Part", StringComparison.OrdinalIgnoreCase))
            return false;

        int open = comment.IndexOf('"');
        if (open < 0)
            return true; // A part header without a name still starts a part.

        int close = comment[(open + 1)..].IndexOf('"');
        name = close < 0 ? null : comment.Slice(open + 1, close).ToString();
        return true;
    }

    private static void AppendTitle(StringBuilder title, ReadOnlySpan<char> comment)
    {
        ReadOnlySpan<char> trimmed = comment.Trim().Trim('=').Trim();
        if (trimmed.IsEmpty)
            return;

        if (title.Length > 0)
            title.Append(' ');
        title.Append(trimmed);
    }

    /// <summary>Splits on ';' and tab, dropping empties. Returns the number of fields written.</summary>
    private static int SplitFields(ReadOnlySpan<char> data, Span<Range> fields)
    {
        int count = 0;
        int start = 0;

        for (int i = 0; i <= data.Length && count < fields.Length; i++)
        {
            if (i != data.Length && data[i] is not (';' or '\t'))
                continue;

            ReadOnlySpan<char> token = data[start..i];
            int lead = 0;
            while (lead < token.Length && char.IsWhiteSpace(token[lead]))
                lead++;
            int end = token.Length;
            while (end > lead && char.IsWhiteSpace(token[end - 1]))
                end--;

            if (end > lead)
                fields[count++] = new Range(start + lead, start + end);

            start = i + 1;
        }

        return count;
    }

    private static int ReadNumbers(ReadOnlySpan<char> data, Span<float> destination)
    {
        Span<Range> fields = stackalloc Range[8];
        int count = SplitFields(data, fields);
        int written = 0;

        for (int i = 0; i < count && written < destination.Length; i++)
        {
            if (!float.TryParse(data[fields[i]], NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                return -1;
            destination[written++] = value;
        }

        return count > destination.Length ? count : written;
    }

    private static float ParseFloat(ReadOnlySpan<char> token) =>
        float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : 0f;
}
