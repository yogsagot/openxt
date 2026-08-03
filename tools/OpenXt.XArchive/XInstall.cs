namespace OpenXt.XArchive;

public enum XGame
{
    BeyondTheFrontier,
    Tension,
}

/// <summary>
/// A located installation of one of the original games.
///
/// OpenXT never redistributes EGOSOFT data: the player's own installation is the only source, and
/// everything we produce from it is a local cache derived from a copy they already own.
/// </summary>
/// <param name="Game">Which of the two titles this is.</param>
/// <param name="Root">Directory containing <c>01.cat</c>.</param>
public sealed record XInstall(XGame Game, string Root)
{
    public string CatPath => Path.Combine(Root, "01.cat");

    /// <summary>Short, stable key used for the cache subdirectory.</summary>
    public string Key => Game == XGame.BeyondTheFrontier ? "xbtf" : "xtension";

    public string DisplayName =>
        Game == XGame.BeyondTheFrontier ? "X: Beyond the Frontier" : "X-Tension";

    public CatArchive OpenArchive() => CatArchive.Open(CatPath);

    /// <summary>
    /// Identifies an installation by its directory. Returns null if the directory holds no archive.
    /// The two games are told apart by their executable, falling back to the folder name.
    /// </summary>
    public static XInstall? Identify(string root)
    {
        if (!Directory.Exists(root))
            return null;

        string cat = Path.Combine(root, "01.cat");
        if (!File.Exists(cat))
        {
            // Some installs use uppercase names.
            string? found = Directory
                .EnumerateFiles(root)
                .FirstOrDefault(f => string.Equals(Path.GetFileName(f), "01.cat", StringComparison.OrdinalIgnoreCase));

            if (found is null)
                return null;
        }

        bool tension =
            File.Exists(Path.Combine(root, "X-TENSION.exe")) ||
            File.Exists(Path.Combine(root, "X-Tension.exe")) ||
            root.Contains("Tension", StringComparison.OrdinalIgnoreCase);

        return new XInstall(tension ? XGame.Tension : XGame.BeyondTheFrontier, root);
    }

    /// <summary>
    /// Searches the usual Steam locations for both games. Explicit paths always win over this;
    /// it exists so the common case needs no configuration.
    /// </summary>
    public static IReadOnlyList<XInstall> Discover()
    {
        List<XInstall> found = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (string library in SteamLibraries())
        {
            string common = Path.Combine(library, "steamapps", "common");
            if (!Directory.Exists(common))
                continue;

            foreach (string candidate in Directory.EnumerateDirectories(common))
            {
                string name = Path.GetFileName(candidate);
                bool interesting =
                    name.Contains("X Beyond the Frontier", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("X-Tension", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("XTension", StringComparison.OrdinalIgnoreCase);

                if (!interesting)
                    continue;

                // Steam roots overlap through symlinks (~/.steam/steam -> ~/.local/share/Steam),
                // so identity is the resolved path, not the one we walked in on.
                if (XInstall.Identify(candidate) is { } install && seen.Add(RealPath(install.Root)))
                    found.Add(install);
            }
        }

        return found;
    }

    /// <summary>Fully resolved path, following symlinks, for identity comparisons.</summary>
    private static string RealPath(string path)
    {
        try
        {
            return Directory.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName
                   ?? Path.GetFullPath(path);
        }
        catch (IOException)
        {
            return Path.GetFullPath(path);
        }
    }

    private static IEnumerable<string> SteamLibraries()
    {
        List<string> roots = [];
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (!string.IsNullOrEmpty(home))
        {
            roots.Add(Path.Combine(home, ".steam", "steam"));
            roots.Add(Path.Combine(home, ".local", "share", "Steam"));
            roots.Add(Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"));
            roots.Add(Path.Combine(home, "Library", "Application Support", "Steam")); // macOS
        }

        foreach (string variable in (string[])["ProgramFiles(x86)", "ProgramFiles"])
        {
            string? programFiles = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrEmpty(programFiles))
                roots.Add(Path.Combine(programFiles, "Steam"));
        }

        HashSet<string> unique = new(StringComparer.OrdinalIgnoreCase);

        foreach (string candidate in roots)
        {
            // ~/.steam/steam is normally a symlink to ~/.local/share/Steam, so both spellings
            // reach the same library. Resolve before deduplicating or every install lists twice.
            string root = RealPath(candidate);

            if (Directory.Exists(root) && unique.Add(root))
                yield return root;

            // Steam records extra drives in libraryfolders.vdf.
            string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf))
                continue;

            foreach (string extra in ParseLibraryFolders(vdf))
            {
                string resolved = RealPath(extra);
                if (Directory.Exists(resolved) && unique.Add(resolved))
                    yield return resolved;
            }
        }
    }

    /// <summary>
    /// Pulls the "path" values out of Valve's KeyValues file. Deliberately minimal: we only need
    /// the quoted paths, not a general VDF parser.
    /// </summary>
    private static IEnumerable<string> ParseLibraryFolders(string vdfPath)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(vdfPath);
        }
        catch (IOException)
        {
            yield break;
        }

        foreach (string line in lines)
        {
            int key = line.IndexOf("\"path\"", StringComparison.OrdinalIgnoreCase);
            if (key < 0)
                continue;

            int open = line.IndexOf('"', key + 6);
            if (open < 0)
                continue;

            int close = line.IndexOf('"', open + 1);
            if (close > open)
                yield return line[(open + 1)..close].Replace("\\\\", "\\");
        }
    }
}
