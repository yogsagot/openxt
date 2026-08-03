using System.Text.Json;

namespace OpenXt.Modding;

/// <summary>
/// Finds packages on disk. A package is any immediate subdirectory of a search root containing a
/// <c>mod.json</c>; nothing recurses, so an unpacked archive with a stray nested copy cannot
/// register itself twice.
/// </summary>
public static class ModDiscovery
{
    /// <summary>
    /// Scans the roots in order and returns one package per id. A later root wins: that is what
    /// makes a player-installed package override the copy bundled with the build, which is how a
    /// mod is updated without touching the installation.
    /// </summary>
    public static IReadOnlyList<ModPackage> Scan(
        IReadOnlyList<(string Root, ModOrigin Origin)> roots,
        ModDiagnostics diagnostics)
    {
        Dictionary<string, ModPackage> byId = new(StringComparer.Ordinal);

        foreach ((string root, ModOrigin origin) in roots)
        {
            if (!Directory.Exists(root))
                continue;

            // Ordinal sort so the same tree always produces the same sequence, whatever the
            // filesystem's own enumeration order happens to be.
            string[] directories = Directory.GetDirectories(root);
            Array.Sort(directories, StringComparer.Ordinal);

            foreach (string directory in directories)
            {
                string manifestPath = Path.Combine(directory, ModApi.ManifestFileName);
                if (!File.Exists(manifestPath))
                    continue;

                string label = Path.GetFileName(directory);
                ModManifest? manifest;

                try
                {
                    manifest = ModManifest.ReadFile(manifestPath);
                }
                catch (Exception ex) when (ex is IOException or JsonException or NotSupportedException)
                {
                    diagnostics.Error(label, $"{ModApi.ManifestFileName} is unreadable: {ex.Message}");
                    continue;
                }

                if (manifest is null)
                {
                    diagnostics.Error(label, $"{ModApi.ManifestFileName} is empty.");
                    continue;
                }

                if (manifest.Validate() is { } problem)
                {
                    diagnostics.Error(string.IsNullOrEmpty(manifest.Id) ? label : manifest.Id, problem);
                    continue;
                }

                ModPackage package = new()
                {
                    Manifest = manifest,
                    Root = Path.GetFullPath(directory),
                    Origin = origin,
                };

                if (byId.TryGetValue(manifest.Id, out ModPackage? existing))
                    diagnostics.Info(
                        manifest.Id,
                        $"{package.Origin.ToString().ToLowerInvariant()} copy {package.Version} overrides " +
                        $"{existing.Origin.ToString().ToLowerInvariant()} copy {existing.Version}.");

                byId[manifest.Id] = package;
            }
        }

        List<ModPackage> packages = [.. byId.Values];
        packages.Sort(static (a, b) => string.CompareOrdinal(a.Id, b.Id));
        return packages;
    }
}
