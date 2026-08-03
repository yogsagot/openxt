namespace OpenXt.Modding;

/// <summary>
/// The loaded packages' content directories, stacked in load order.
///
/// Two lookups, and the difference between them is the whole design:
/// <list type="bullet">
///   <item><see cref="Find"/> — last layer wins. For files that are one thing: a texture, a mesh,
///   an override that replaces wholesale.</item>
///   <item><see cref="Layers"/> — every layer, in load order. For catalogs, which are merged by
///   <see cref="JsonOverlay"/> so two mods can each add a ship without either erasing the other.</item>
/// </list>
/// Relative paths use forward slashes and are checked: nothing rooted, nothing containing
/// <c>..</c>, because these strings come from mod-authored data and must not be able to name a
/// file outside the package stack.
/// </summary>
public sealed class ModContent
{
    private readonly ModPackage[] _packages;

    public ModContent(IReadOnlyList<ModPackage> packagesInLoadOrder) => _packages = [.. packagesInLoadOrder];

    /// <summary>The packages backing this stack, in load order.</summary>
    public IReadOnlyList<ModPackage> Packages => _packages;

    /// <summary>The winning absolute path for a relative content path, or null if no layer has it.</summary>
    public string? Find(string relativePath)
    {
        string relative = Normalise(relativePath);

        for (int i = _packages.Length - 1; i >= 0; i--)
        {
            string candidate = Path.Combine(_packages[i].ContentRoot, relative);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// Every layer that provides this path, in load order — the base game first, the last-loaded
    /// mod last. Callers merge them; the order is the precedence.
    /// </summary>
    public IReadOnlyList<string> Layers(string relativePath)
    {
        string relative = Normalise(relativePath);
        List<string> found = [];

        foreach (ModPackage package in _packages)
        {
            string candidate = Path.Combine(package.ContentRoot, relative);
            if (File.Exists(candidate))
                found.Add(candidate);
        }

        return found;
    }

    /// <summary>
    /// Which package a layer path came from, for diagnostics. Returns null for a path that is not
    /// inside any content root.
    /// </summary>
    public ModPackage? Owner(string absolutePath)
    {
        string full = Path.GetFullPath(absolutePath);

        foreach (ModPackage package in _packages)
        {
            string root = Path.GetFullPath(package.ContentRoot);
            if (full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                return package;
        }

        return null;
    }

    /// <summary>
    /// Files under a relative directory across all layers, keyed by their path relative to the
    /// content root so a later package overrides an earlier one file for file.
    /// </summary>
    public IReadOnlyList<string> Enumerate(string relativeDirectory, string searchPattern = "*")
    {
        string relative = Normalise(relativeDirectory);
        Dictionary<string, string> byRelativePath = new(StringComparer.Ordinal);

        foreach (ModPackage package in _packages)
        {
            string directory = Path.Combine(package.ContentRoot, relative);
            if (!Directory.Exists(directory))
                continue;

            foreach (string file in Directory.EnumerateFiles(directory, searchPattern, SearchOption.AllDirectories))
                byRelativePath[Path.GetRelativePath(package.ContentRoot, file)] = file;
        }

        List<string> keys = [.. byRelativePath.Keys];
        keys.Sort(StringComparer.Ordinal);

        List<string> files = new(keys.Count);
        foreach (string key in keys)
            files.Add(byRelativePath[key]);

        return files;
    }

    private static string Normalise(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("A content path is required.", nameof(relativePath));

        if (Path.IsPathRooted(relativePath))
            throw new ArgumentException($"Content path '{relativePath}' must be relative.", nameof(relativePath));

        string normalised = relativePath.Replace('\\', '/').Trim('/');

        foreach (string segment in normalised.Split('/'))
            if (segment is ".." or ".")
                throw new ArgumentException(
                    $"Content path '{relativePath}' must not leave the package.", nameof(relativePath));

        return normalised.Replace('/', Path.DirectorySeparatorChar);
    }
}
