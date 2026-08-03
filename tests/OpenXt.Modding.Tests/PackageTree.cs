using OpenXt.Modding;

namespace OpenXt.Modding.Tests;

/// <summary>
/// A throwaway install layout on disk: <c>games/</c> and <c>mods/</c> under a temp directory, plus
/// an optional second root standing in for the player's own mods folder.
///
/// Tests write real files because that is what the loader reads — a mock filesystem would test a
/// different program.
/// </summary>
public sealed class PackageTree : IDisposable
{
    public PackageTree()
    {
        Root = Path.Combine(Path.GetTempPath(), "openxt-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(Games);
        Directory.CreateDirectory(Mods);
        Directory.CreateDirectory(UserMods);
    }

    public string Root { get; }

    public string Games => Path.Combine(Root, "games");
    public string Mods => Path.Combine(Root, "mods");
    public string UserMods => Path.Combine(Root, "user-mods");

    public IReadOnlyList<(string Root, ModOrigin Origin)> SearchRoots =>
    [
        (Games, ModOrigin.Bundled),
        (Mods, ModOrigin.Bundled),
        (UserMods, ModOrigin.User),
    ];

    /// <summary>Writes a package directory with the given manifest JSON, returning its path.</summary>
    public string Package(string root, string id, string manifestJson)
    {
        string directory = Path.Combine(root, id);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, ModApi.ManifestFileName), manifestJson);
        return directory;
    }

    /// <summary>Writes a content file inside a package, creating directories as needed.</summary>
    public void Content(string packageDirectory, string relativePath, string text)
    {
        string full = Path.Combine(packageDirectory, "data", relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    /// <summary>A minimal manifest, for tests that care about one field at a time.</summary>
    public static string Manifest(
        string id,
        ModKind kind = ModKind.Mod,
        string version = "1.0.0",
        string? requires = null,
        string? extra = null)
    {
        string requiresJson = requires is null ? "" : $", \"requires\": {requires}";
        string extraJson = extra is null ? "" : $", {extra}";

        return $$"""
                 {
                   "id": "{{id}}",
                   "version": "{{version}}",
                   "kind": "{{kind.ToString().ToLowerInvariant()}}",
                   "apiVersion": {{ModApi.Version}}{{requiresJson}}{{extraJson}}
                 }
                 """;
    }

    public ModHost Load(string? gameId = null, bool assemblies = false, params string[] disabled) =>
        ModHost.Load(new ModHostOptions
        {
            SearchRoots = SearchRoots,
            GameId = gameId,
            Disabled = new HashSet<string>(disabled, StringComparer.Ordinal),
            LoadAssemblies = assemblies,
        });

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }
}
