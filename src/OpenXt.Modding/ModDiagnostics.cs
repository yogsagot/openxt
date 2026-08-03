namespace OpenXt.Modding;

public enum ModSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>Something worth telling the player about a package, tied to the package it concerns.</summary>
public readonly record struct ModDiagnostic(ModSeverity Severity, string PackageId, string Message)
{
    public override string ToString() => $"[{Severity.ToString().ToLowerInvariant()}] {PackageId}: {Message}";
}

/// <summary>
/// Everything that went wrong (or is worth noting) while loading packages.
///
/// The loader never throws over a bad package. A mod with a broken manifest, a missing dependency
/// or a plugin that throws in its constructor disables itself and lands here; the game still
/// starts, and the overlay says which package failed and why. Anything else and one stale
/// third-party mod becomes an unbootable game.
/// </summary>
public sealed class ModDiagnostics
{
    private readonly List<ModDiagnostic> _entries = [];

    public IReadOnlyList<ModDiagnostic> Entries => _entries;

    public bool HasErrors
    {
        get
        {
            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i].Severity == ModSeverity.Error)
                    return true;

            return false;
        }
    }

    public void Add(ModSeverity severity, string packageId, string message) =>
        _entries.Add(new ModDiagnostic(severity, packageId, message));

    public void Info(string packageId, string message) => Add(ModSeverity.Info, packageId, message);
    public void Warning(string packageId, string message) => Add(ModSeverity.Warning, packageId, message);
    public void Error(string packageId, string message) => Add(ModSeverity.Error, packageId, message);
}
