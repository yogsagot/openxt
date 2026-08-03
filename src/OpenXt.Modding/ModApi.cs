namespace OpenXt.Modding;

/// <summary>
/// The version of the contract a package is written against, declared as <c>apiVersion</c> in
/// <c>mod.json</c>.
///
/// This is not the engine version. It changes only when the shape of what a plugin sees changes —
/// the manifest schema, the plugin interfaces, the content layout. A package built against an
/// older-but-supported api still loads; one built against a newer one is refused rather than
/// half-loaded, because the failure would otherwise surface as a MissingMethodException somewhere
/// deep in a mod's Update.
/// </summary>
public static class ModApi
{
    /// <summary>The api this build implements. New packages should declare this.</summary>
    public const int Version = 1;

    /// <summary>Oldest api still accepted. Raise it only with a migration note in docs/modding.md.</summary>
    public const int MinimumSupported = 1;

    /// <summary>The manifest file every package must have at its root.</summary>
    public const string ManifestFileName = "mod.json";

    public static bool IsSupported(int apiVersion) =>
        apiVersion >= MinimumSupported && apiVersion <= Version;
}
