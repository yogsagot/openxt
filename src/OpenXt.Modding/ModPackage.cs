namespace OpenXt.Modding;

/// <summary>Which search root a package came from. Purely informational — the loader treats them alike.</summary>
public enum ModOrigin
{
    /// <summary>Shipped with the build, next to the executable.</summary>
    Bundled,

    /// <summary>Installed by the player into their own data directory.</summary>
    User,
}

/// <summary>A package found on disk: its manifest, where it lives, and where it came from.</summary>
public sealed class ModPackage
{
    public required ModManifest Manifest { get; init; }

    /// <summary>Absolute path to the directory holding <c>mod.json</c>.</summary>
    public required string Root { get; init; }

    public required ModOrigin Origin { get; init; }

    public string Id => Manifest.Id;
    public ModKind Kind => Manifest.Kind;
    public ModVersion Version => Manifest.SemanticVersion;

    /// <summary>True when this package ships code, not just data.</summary>
    public bool HasAssembly => !string.IsNullOrWhiteSpace(Manifest.Assembly);

    /// <summary>Absolute path to the package's content directory, whether or not it exists.</summary>
    public string ContentRoot => Path.Combine(Root, Manifest.Content);

    /// <summary>Absolute path to the declared assembly, or null for a data-only package.</summary>
    public string? AssemblyPath =>
        string.IsNullOrWhiteSpace(Manifest.Assembly) ? null : Path.Combine(Root, Manifest.Assembly);

    public override string ToString() => $"{Id} {Version}";
}
