using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenXt.Assets;
using OpenXt.XArchive;

namespace OpenXt.AssetImport;

/// <summary>
/// Converts an installation into OpenXT's local asset cache.
///
/// Nothing this writes belongs in the repository. The cache is derived from the player's own copy
/// of the game, lives in their local application data directory, and can be deleted and rebuilt at
/// any time.
/// </summary>
public static partial class ImportCommand
{
    [GeneratedRegex(@"(\d+)\.pbd$", RegexOptions.IgnoreCase)]
    private static partial Regex BodyIdPattern { get; }

    [GeneratedRegex(@"^tex/true/(\d+)\.jpg$", RegexOptions.IgnoreCase)]
    private static partial Regex TexturePattern { get; }

    /// <summary>Matches t/44001.txt and friends: the leading digits are the ITU language code.</summary>
    [GeneratedRegex(@"^t/(\d{2})(\d+)\.txt$", RegexOptions.IgnoreCase)]
    private static partial Regex TextPattern { get; }

    public static int Run(XInstall install, bool force, float? scaleOverride = null)
    {
        using CatArchive archive = install.OpenArchive();

        string gameKey = install.Key;
        string manifestPath = AssetCachePaths.Manifest(gameKey);
        float scale = scaleOverride ?? MeshConverter.DefaultMetresPerUnit;

        Console.WriteLine($"source  {install.DisplayName}  {install.Root}");
        Console.WriteLine($"cache   {AssetCachePaths.ForGame(gameKey)}");
        Console.WriteLine($"scale   {scale:0.######} m per unit");
        Console.WriteLine();

        Console.Write("hashing source archive... ");
        string catHash = Sha256(archive.CatPath);
        string datHash = Sha256(archive.DatPath);
        Console.WriteLine("done");

        CacheManifest? existing = CacheManifest.ReadFile(manifestPath);
        if (!force
            && existing is { IsCurrent: true }
            && existing.CatSha256 == catHash
            && existing.DatSha256 == datHash
            && Math.Abs(existing.MetresPerUnit - scale) < float.Epsilon)
        {
            Console.WriteLine("Cache is already current. Use --force to rebuild.");
            return 0;
        }

        Directory.CreateDirectory(AssetCachePaths.Meshes(gameKey));
        Directory.CreateDirectory(AssetCachePaths.Textures(gameKey));
        Directory.CreateDirectory(AssetCachePaths.Text(gameKey));

        MeshConverter converter = new(scale);
        List<string> problems = [];
        HashSet<int> referencedTextures = [];
        HashSet<int> presentTextures = [];

        int meshes = 0, skippedScenes = 0, emptyBodies = 0;

        foreach (CatEntry entry in archive.Entries)
        {
            if (!entry.Path.EndsWith(".pbd", StringComparison.OrdinalIgnoreCase))
                continue;

            Match match = BodyIdPattern.Match(entry.Path);
            if (!match.Success)
                continue;

            int bodyId = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);

            try
            {
                string text = Encoding.Latin1.GetString(PckStream.Decode(archive.Read(entry)));

                // Scene files share the extension but describe body placement, not geometry.
                if (text.Contains("VER:", StringComparison.Ordinal))
                {
                    skippedScenes++;
                    continue;
                }

                BodFile parsed = BodParser.Parse(text);
                foreach (BodMaterial material in parsed.Materials)
                    referencedTextures.Add(material.TextureId);

                OxMesh mesh = converter.Convert(bodyId, parsed);
                if (mesh.Lods.Length == 0)
                {
                    emptyBodies++;
                    continue;
                }

                mesh.WriteFile(AssetCachePaths.MeshFile(gameKey, bodyId));
                meshes++;
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            {
                problems.Add($"{entry.Path}: {ex.Message}");
            }
        }

        Console.WriteLine($"meshes    {meshes,6:N0} written  ({skippedScenes:N0} scenes skipped, " +
                          $"{emptyBodies:N0} empty bodies)");

        int textures = 0;
        foreach (CatEntry entry in archive.Entries)
        {
            Match match = TexturePattern.Match(entry.Path);
            if (!match.Success)
                continue;

            int id = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);

            // Copied verbatim: these are ordinary JPEGs that MonoGame decodes directly, so
            // re-encoding would only lose quality and add a dependency.
            File.WriteAllBytes(AssetCachePaths.TextureFile(gameKey, id), archive.Read(entry));
            presentTextures.Add(id);
            textures++;
        }

        Console.WriteLine($"textures  {textures,6:N0} copied");

        int textTables = 0;
        Dictionary<int, TextTable> byLanguage = [];

        foreach (CatEntry entry in archive.Entries)
        {
            Match match = TextPattern.Match(entry.Path);
            if (!match.Success)
                continue;

            int language = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            TextTable table = TextTable.Parse(archive.Read(entry));

            if (byLanguage.TryGetValue(language, out TextTable? merged))
                merged.Overlay(table);
            else
                byLanguage[language] = table;
        }

        foreach ((int language, TextTable table) in byLanguage)
        {
            using FileStream stream = File.Create(AssetCachePaths.TextFile(gameKey, language));
            JsonSerializer.Serialize(
                stream,
                table.Entries.ToDictionary(p => p.Key, p => p.Value),
                AssetJsonContext.Default.DictionaryInt32String);
            textTables++;
            Console.WriteLine($"text      {table.Count,6:N0} strings -> {language}.json");
        }

        int[] missing = referencedTextures.Where(id => !presentTextures.Contains(id)).Order().ToArray();

        new CacheManifest
        {
            Game = gameKey,
            SourceRoot = install.Root,
            CatSha256 = catHash,
            DatSha256 = datHash,
            EntryCount = archive.Entries.Count,
            MeshCount = meshes,
            TextureCount = textures,
            TextCount = textTables,
            MetresPerUnit = scale,
            ImportedUtc = DateTimeOffset.UtcNow,
            MissingTextures = missing,
        }.WriteFile(manifestPath);

        Console.WriteLine();
        if (missing.Length > 0)
            Console.WriteLine($"note: {missing.Length} referenced textures are absent from the archive: " +
                              string.Join(", ", missing));

        if (problems.Count > 0)
        {
            Console.Error.WriteLine($"{problems.Count:N0} entries failed:");
            foreach (string problem in problems.Take(20))
                Console.Error.WriteLine($"  {problem}");
            return 1;
        }

        Console.WriteLine($"Import complete: {manifestPath}");
        return 0;
    }

    private static string Sha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return System.Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
