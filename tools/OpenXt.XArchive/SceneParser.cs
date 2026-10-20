using System.Globalization;
using System.Numerics;

namespace OpenXt.XArchive;

/// <summary>One keyframe of a scene part: its type and the numbers that follow it.</summary>
/// <param name="Type">
/// 2 = pose (position, then a rotation as turns and an axis); 34 = position only; 18 = rotation
/// only, the keys a spinning part animates through; 8 = camera (position and target). The last two
/// numbers of every key are its duration and the index of the next key, -1 for none.
/// </param>
public sealed record SceneKey(int Type, double[] Values);

/// <summary>A part of a scene: a body (or -1, for the camera) and its keyframes.</summary>
public sealed record ScenePart(int Index, int BodyId, IReadOnlyList<SceneKey> Keys)
{
    /// <summary>
    /// Where the part rests: its first pose or position key, in archive units, with the rotation
    /// as an angle in whole turns about a unit axis. Identity when the part has no such key.
    /// </summary>
    public (Vector3 Position, float Turns, Vector3 Axis) RestPose
    {
        get
        {
            foreach (SceneKey key in Keys)
            {
                double[] v = key.Values;
                if (key.Type == 2 && v.Length >= 7)
                    return (new Vector3((float)v[0], (float)v[1], (float)v[2]), (float)v[3],
                        new Vector3((float)v[4], (float)v[5], (float)v[6]));
                if (key.Type == 34 && v.Length >= 3)
                    return (new Vector3((float)v[0], (float)v[1], (float)v[2]), 0f, Vector3.UnitY);
            }

            return (Vector3.Zero, 0f, Vector3.UnitY);
        }
    }
}

public sealed record BodScene(string? Title, IReadOnlyList<ScenePart> Parts);

/// <summary>
/// The scene variant of <c>.pbd</c>: text that starts with <c>VER: 2;</c> and places bodies rather
/// than defining one. Station scenes assemble a station from its hull, docking clamps, lights and
/// signs; others are cutscenes. Layout, established from the archives:
///
/// <code>
/// / comment lines, the second one naming the 3ds file it came from
/// VER: 2;
/// P 8; B 184;                          part 8 uses body 184 (-1: no body, the camera)
///   { 2;  0; 0; 0;  0.25; 1; 0; 0;  -1;  -1; } // 0     key: type, values, duration, next
/// </code>
///
/// Parts carry no parent: every position is in the scene's own frame, in the same units as the
/// bodies, so the importer's metres-per-unit applies unchanged.
/// </summary>
public static class SceneParser
{
    public static bool IsScene(string text) => text.Contains("VER:", StringComparison.Ordinal);

    public static BodScene Parse(string text)
    {
        List<ScenePart> parts = [];
        string? title = null;

        int partIndex = -1, bodyId = -1;
        List<SceneKey>? keys = null;

        foreach (ReadOnlySpan<char> raw in text.AsSpan().EnumerateLines())
        {
            ReadOnlySpan<char> line = raw.Trim();
            if (line.IsEmpty)
                continue;

            if (line[0] == '/')
            {
                // The banner's middle line: "/ 3D Scene Information from 3ds file: ...".
                ReadOnlySpan<char> comment = line.TrimStart('/').Trim();
                if (title is null && comment.Length > 0 && !comment.StartsWith("==="))
                    title = comment.ToString();
                continue;
            }

            if (line.StartsWith("P ") || line.StartsWith("P\t"))
            {
                Flush();
                (partIndex, bodyId) = ParsePartHeader(line);
                keys = [];
                continue;
            }

            if (line[0] == '{' && keys is not null)
            {
                if (ParseKey(line) is { } key)
                    keys.Add(key);
            }
        }

        Flush();
        return new BodScene(title, parts);

        void Flush()
        {
            if (keys is not null)
                parts.Add(new ScenePart(partIndex, bodyId, keys));
            keys = null;
        }
    }

    /// <summary>"P 8; B 184;" — the part number, then its body.</summary>
    private static (int Part, int Body) ParsePartHeader(ReadOnlySpan<char> line)
    {
        int part = -1, body = -1;
        foreach (Range field in line.Split(';'))
        {
            ReadOnlySpan<char> f = line[field].Trim();
            if (f.StartsWith("P"))
                int.TryParse(f[1..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out part);
            else if (f.StartsWith("B"))
                int.TryParse(f[1..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out body);
        }

        return (part, body);
    }

    /// <summary>"{ 2;  38300; 72600; 39100;  0; 0; 0; 0;  -1;  -1; } // 0".</summary>
    private static SceneKey? ParseKey(ReadOnlySpan<char> line)
    {
        int close = line.IndexOf('}');
        if (close < 0)
            return null;

        ReadOnlySpan<char> body = line[1..close];
        List<double> numbers = [];

        foreach (Range field in body.Split(';'))
        {
            ReadOnlySpan<char> f = body[field].Trim();
            if (f.IsEmpty)
                continue;
            if (!double.TryParse(f, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                return null;
            numbers.Add(value);
        }

        if (numbers.Count == 0)
            return null;

        return new SceneKey((int)numbers[0], [.. numbers.Skip(1)]);
    }
}
