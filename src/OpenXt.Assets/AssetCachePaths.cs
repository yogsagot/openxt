namespace OpenXt.Assets;

/// <summary>
/// Where converted assets live.
///
/// The cache sits in the user's local application data directory, deliberately outside the
/// repository. EGOSOFT data is copyrighted and must never be committed; keeping the cache
/// physically elsewhere means no .gitignore mistake can leak it. It is derived from the player's
/// own installation and is disposable — deleting it costs nothing but a re-import.
/// </summary>
public static class AssetCachePaths
{
    /// <summary>Override for developers who want the cache somewhere specific.</summary>
    public const string OverrideVariable = "OPENXT_ASSET_CACHE";

    /// <summary>
    /// <c>~/.local/share/openxt</c> on Linux, <c>%LOCALAPPDATA%\openxt</c> on Windows,
    /// <c>~/Library/Application Support/openxt</c> on macOS.
    /// </summary>
    public static string Root
    {
        get
        {
            string? overridden = Environment.GetEnvironmentVariable(OverrideVariable);
            if (!string.IsNullOrWhiteSpace(overridden))
                return Path.GetFullPath(overridden);

            string baseDirectory = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.DoNotVerify);

            if (string.IsNullOrEmpty(baseDirectory))
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                baseDirectory = Path.Combine(home, ".local", "share");
            }

            return Path.Combine(baseDirectory, "openxt");
        }
    }

    /// <summary>Root of one game's converted assets, e.g. <c>…/openxt/assets/xbtf</c>.</summary>
    public static string ForGame(string gameKey) => Path.Combine(Root, "assets", gameKey);

    public static string Meshes(string gameKey) => Path.Combine(ForGame(gameKey), "meshes");

    public static string Textures(string gameKey) => Path.Combine(ForGame(gameKey), "textures");

    public static string Text(string gameKey) => Path.Combine(ForGame(gameKey), "text");

    public static string Manifest(string gameKey) => Path.Combine(ForGame(gameKey), "manifest.json");

    public static string MeshFile(string gameKey, int bodyId) =>
        Path.Combine(Meshes(gameKey), $"{bodyId:D5}.oxmesh");

    public static string TextureFile(string gameKey, int textureId) =>
        Path.Combine(Textures(gameKey), $"{textureId:D3}.jpg");

    public static string TextFile(string gameKey, int language) =>
        Path.Combine(Text(gameKey), $"{language}.json");
}
