using System.Globalization;

namespace OpenXt.Modding;

/// <summary>
/// A package version: <c>major.minor.patch</c>, missing components reading as zero, so both
/// <c>"1"</c> and <c>"1.0.0"</c> parse. No pre-release tags or build metadata — nothing in the
/// loader needs them, and leaving them out keeps comparison total and obvious.
/// </summary>
public readonly record struct ModVersion(int Major, int Minor, int Patch) : IComparable<ModVersion>
{
    public static readonly ModVersion Zero = new(0, 0, 0);

    public static bool TryParse(string? text, out ModVersion version)
    {
        version = Zero;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        ReadOnlySpan<char> span = text.AsSpan().Trim();
        Span<int> parts = [0, 0, 0];
        int index = 0;

        foreach (Range segment in span.Split('.'))
        {
            if (index == parts.Length)
                return false;

            ReadOnlySpan<char> part = span[segment];
            if (part.IsEmpty || !int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
                return false;

            parts[index++] = value;
        }

        version = new ModVersion(parts[0], parts[1], parts[2]);
        return true;
    }

    public static ModVersion Parse(string text) =>
        TryParse(text, out ModVersion version)
            ? version
            : throw new FormatException($"'{text}' is not a version (expected major.minor.patch).");

    public int CompareTo(ModVersion other)
    {
        int result = Major.CompareTo(other.Major);
        if (result != 0)
            return result;

        result = Minor.CompareTo(other.Minor);
        return result != 0 ? result : Patch.CompareTo(other.Patch);
    }

    public static bool operator <(ModVersion left, ModVersion right) => left.CompareTo(right) < 0;
    public static bool operator >(ModVersion left, ModVersion right) => left.CompareTo(right) > 0;
    public static bool operator <=(ModVersion left, ModVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >=(ModVersion left, ModVersion right) => left.CompareTo(right) >= 0;

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}

/// <summary>
/// The versions of a dependency a package will accept.
///
/// Two forms:
/// <list type="bullet">
///   <item><c>"1.2"</c> — caret by default: at least 1.2.0, below 2.0.0. This is what almost every
///   manifest should say, because the interesting question is "has it broken compatibility".</item>
///   <item><c>"&gt;=1.2 &lt;1.5"</c> — an explicit window, for the rare mod that knows it breaks on a
///   specific later release.</item>
/// </list>
/// An empty or absent range accepts anything.
/// </summary>
public readonly record struct ModVersionRange(ModVersion Minimum, ModVersion? ExclusiveMaximum)
{
    /// <summary>Accepts every version. What a dependency with no <c>version</c> field gets.</summary>
    public static readonly ModVersionRange Any = new(ModVersion.Zero, null);

    public bool Allows(ModVersion version) =>
        version >= Minimum && (ExclusiveMaximum is not { } max || version < max);

    public static bool TryParse(string? text, out ModVersionRange range)
    {
        range = Any;

        if (string.IsNullOrWhiteSpace(text))
            return true;

        string trimmed = text.Trim();

        // Bare version: caret semantics. "0.3" allows 0.3.x and 0.9 alike — a zero major is still
        // treated as one compatibility line, because pre-1.0 packages break constantly and a mod
        // author who cares can always write the explicit form.
        if (!trimmed.StartsWith('>') && !trimmed.StartsWith('<') && !trimmed.StartsWith('^'))
        {
            if (!ModVersion.TryParse(trimmed, out ModVersion exact))
                return false;

            range = new ModVersionRange(exact, new ModVersion(exact.Major + 1, 0, 0));
            return true;
        }

        ModVersion minimum = ModVersion.Zero;
        ModVersion? maximum = null;

        foreach (string term in trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (term.StartsWith("^", StringComparison.Ordinal))
            {
                if (!ModVersion.TryParse(term[1..], out ModVersion caret))
                    return false;

                minimum = caret;
                maximum = new ModVersion(caret.Major + 1, 0, 0);
                continue;
            }

            if (term.StartsWith(">=", StringComparison.Ordinal))
            {
                if (!ModVersion.TryParse(term[2..], out minimum))
                    return false;

                continue;
            }

            if (term.StartsWith("<", StringComparison.Ordinal))
            {
                if (!ModVersion.TryParse(term[1..], out ModVersion below))
                    return false;

                maximum = below;
                continue;
            }

            return false;
        }

        range = new ModVersionRange(minimum, maximum);
        return true;
    }

    public static ModVersionRange Parse(string? text) =>
        TryParse(text, out ModVersionRange range)
            ? range
            : throw new FormatException($"'{text}' is not a version range.");

    public override string ToString() =>
        ExclusiveMaximum is { } max ? $">={Minimum} <{max}" : $">={Minimum}";
}
