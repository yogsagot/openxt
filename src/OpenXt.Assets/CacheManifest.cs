using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenXt.Assets;

/// <summary>
/// Records what an import produced and what it was produced from.
///
/// The game validates this before trusting the cache: if the importer version, the mesh format
/// version or either source hash has moved, the cache is stale. The game never re-imports on its
/// own — that would mean the runtime reaching into the player's installation behind their back.
/// It reports the problem and leaves the decision to them.
/// </summary>
public sealed record CacheManifest
{
    /// <summary>Bumped whenever conversion output changes in a way that invalidates existing caches.</summary>
    public const int CurrentImporterVersion = 1;

    public int ImporterVersion { get; init; } = CurrentImporterVersion;
    public int MeshVersion { get; init; } = OxMesh.CurrentVersion;

    /// <summary>"xbtf" or "xtension".</summary>
    public required string Game { get; init; }

    /// <summary>Where the source archive was read from, for diagnostics only.</summary>
    public required string SourceRoot { get; init; }

    public required string CatSha256 { get; init; }
    public required string DatSha256 { get; init; }
    public int EntryCount { get; init; }

    public int MeshCount { get; init; }
    public int TextureCount { get; init; }
    public int TextCount { get; init; }

    /// <summary>Metres per archive unit used for this import; see the importer's scale constant.</summary>
    public float MetresPerUnit { get; init; }

    public DateTimeOffset ImportedUtc { get; init; }

    /// <summary>Texture IDs referenced by materials but absent from the archive.</summary>
    public int[] MissingTextures { get; init; } = [];

    [JsonIgnore]
    public bool IsCurrent =>
        ImporterVersion == CurrentImporterVersion && MeshVersion == OxMesh.CurrentVersion;

    public void WriteFile(string path)
    {
        using FileStream stream = File.Create(path);
        JsonSerializer.Serialize(stream, this, AssetJsonContext.Default.CacheManifest);
    }

    public static CacheManifest? ReadFile(string path)
    {
        if (!File.Exists(path))
            return null;

        using FileStream stream = File.OpenRead(path);
        return JsonSerializer.Deserialize(stream, AssetJsonContext.Default.CacheManifest);
    }
}

/// <summary>Source-generated JSON, matching the sim layer's no-reflection policy.</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(CacheManifest))]
[JsonSerializable(typeof(Dictionary<int, string>))]
public partial class AssetJsonContext : JsonSerializerContext;
