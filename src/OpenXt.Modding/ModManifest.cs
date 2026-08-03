using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenXt.Modding;

/// <summary>What a package is, which decides how the loader treats it.</summary>
public enum ModKind
{
    /// <summary>
    /// A game: its own ruleset and content. Exactly one loads per run, chosen at startup — the whole
    /// point of the system is that XBTF and X-Tension are two of these on one engine.
    /// </summary>
    Game,

    /// <summary>A mod layered on top of the selected game. Loads whenever its dependencies resolve.</summary>
    Mod,

    /// <summary>
    /// Shared content or code with no opinion of its own. Loads only when something else requires
    /// it, so a library sitting in the mods folder unused costs nothing.
    /// </summary>
    Library,
}

/// <summary>One entry of a manifest's <c>requires</c> list.</summary>
public sealed record ModDependency
{
    public required string Id { get; set; }

    /// <summary>Accepted versions; see <see cref="ModVersionRange"/>. Absent means any.</summary>
    public string? Version { get; set; }

    /// <summary>An optional dependency orders the load but does not gate it.</summary>
    public bool Optional { get; set; }
}

/// <summary>
/// <c>mod.json</c> — the contract between a package on disk and the loader. Everything a package
/// declares about itself lives here; the id in this file is the id, never the directory name.
///
/// Properties are <c>set</c> rather than <c>init</c> on purpose: System.Text.Json's source
/// generator builds init-only types without running their constructor, which drops every property
/// initializer. With <c>init</c>, a manifest omitting <c>kind</c> would come back as
/// <see cref="ModKind.Game"/> (the zero value) rather than <see cref="ModKind.Mod"/>, and
/// <see cref="Content"/> would be null instead of <c>"data"</c>.
/// </summary>
public sealed record ModManifest
{
    public required string Id { get; set; }

    public string? Name { get; set; }

    /// <summary>Package version, <c>major.minor.patch</c>. Defaults to 0.0.0 when absent.</summary>
    public string? Version { get; set; }

    public ModKind Kind { get; set; } = ModKind.Mod;

    /// <summary>Contract version this package was written against; see <see cref="ModApi"/>.</summary>
    public int ApiVersion { get; set; } = ModApi.Version;

    public string? Description { get; set; }
    public IReadOnlyList<string>? Authors { get; set; }
    public string? License { get; set; }
    public string? Homepage { get; set; }

    public IReadOnlyList<ModDependency>? Requires { get; set; }

    /// <summary>Soft ordering: load after these packages if they are present. Not a dependency.</summary>
    public IReadOnlyList<string>? LoadAfter { get; set; }

    public IReadOnlyList<string>? LoadBefore { get; set; }

    /// <summary>
    /// Relative path to a .NET assembly to load, or null for a data-only package. Declaring it is
    /// deliberate and visible: code runs with the process's full rights, so nothing is scanned for
    /// or loaded by accident. See docs/modding.md.
    /// </summary>
    public string? Assembly { get; set; }

    /// <summary>Directory holding this package's layered content, relative to its root.</summary>
    public string Content { get; set; } = "data";

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Id : Name;

    public ModVersion SemanticVersion =>
        ModVersion.TryParse(Version, out ModVersion version) ? version : ModVersion.Zero;

    /// <summary>
    /// Ids are used in dependency lists, file paths and log lines, so they are restricted to a
    /// shape that is unambiguous in all three: lowercase, starting alphanumeric, then
    /// letters/digits/<c>. _ -</c>.
    /// </summary>
    public static bool IsValidId(string? id)
    {
        if (string.IsNullOrEmpty(id) || !char.IsAsciiLetterOrDigit(id[0]))
            return false;

        foreach (char c in id)
        {
            bool ok = (char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '.' or '_' or '-');
            if (!ok)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Returns why this manifest cannot be used, or null when it can. Manifests come from strangers;
    /// every field that the loader will act on is checked here rather than where it is used.
    /// </summary>
    public string? Validate()
    {
        if (!IsValidId(Id))
            return $"'{Id}' is not a valid package id (lowercase letters, digits, '.', '_', '-').";

        if (Version is not null && !ModVersion.TryParse(Version, out _))
            return $"version '{Version}' is not major.minor.patch.";

        if (!ModApi.IsSupported(ApiVersion))
            return $"apiVersion {ApiVersion} is not supported by this build " +
                   $"(supported: {ModApi.MinimumSupported}-{ModApi.Version}).";

        foreach (ModDependency dependency in Requires ?? [])
        {
            if (!IsValidId(dependency.Id))
                return $"requires an invalid package id '{dependency.Id}'.";

            if (!ModVersionRange.TryParse(dependency.Version, out _))
                return $"requires '{dependency.Id}' with an unreadable version range '{dependency.Version}'.";
        }

        if (Assembly is not null && (Path.IsPathRooted(Assembly) || Assembly.Contains("..", StringComparison.Ordinal)))
            return $"assembly path '{Assembly}' must be relative to the package and stay inside it.";

        if (Path.IsPathRooted(Content) || Content.Contains("..", StringComparison.Ordinal))
            return $"content path '{Content}' must be relative to the package and stay inside it.";

        return null;
    }

    public static ModManifest? Read(Stream json) =>
        JsonSerializer.Deserialize(json, ModJsonContext.Default.ModManifest);

    public static ModManifest? ReadFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Read(stream);
    }
}

/// <summary>Source-generated, like every other JSON contract here — no reflection-based serializer.</summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    Converters = [typeof(JsonStringEnumConverter<ModKind>)])]
[JsonSerializable(typeof(ModManifest))]
public partial class ModJsonContext : JsonSerializerContext;
