namespace OpenXt.Modding;

/// <summary>How to build a <see cref="ModHost"/>. The composition root fills this in.</summary>
public sealed record ModHostOptions
{
    /// <summary>Search roots in scan order; a later root's package of the same id wins.</summary>
    public required IReadOnlyList<(string Root, ModOrigin Origin)> SearchRoots { get; init; }

    /// <summary>Which game to run. Null is only valid when exactly one game is installed.</summary>
    public string? GameId { get; init; }

    /// <summary>Package ids the player has switched off.</summary>
    public IReadOnlySet<string> Disabled { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Safe mode. Content layers still load; declared assemblies do not. A mod cannot be sandboxed
    /// once it runs, so the only real protection on offer is not running it — and being able to
    /// start the game without third-party code is what makes a broken install diagnosable.
    /// </summary>
    public bool LoadAssemblies { get; init; } = true;

    /// <summary>The roots a normal build uses: games/ and mods/ next to the executable.</summary>
    public static IReadOnlyList<(string, ModOrigin)> DefaultRoots(string baseDirectory)
    {
        List<(string, ModOrigin)> roots = [];
        foreach (string root in ModPaths.Bundled(baseDirectory))
            roots.Add((root, ModOrigin.Bundled));

        return roots;
    }
}

/// <summary>
/// The loaded world of packages: which game is running, what layers its content, and which plugin
/// objects the layers above should configure.
///
/// Construction never throws over package problems — everything that went wrong is in
/// <see cref="Diagnostics"/>, and <see cref="IsLoaded"/> is false only when there is no game at all
/// to run. Each layer then asks for the interface it understands via <see cref="PluginsOf{T}"/>,
/// which is what lets the headless simulation ignore render plugins entirely.
/// </summary>
public sealed class ModHost
{
    private static readonly IPlugin[] NoPlugins = [];

    private readonly IPlugin[] _plugins;

    private ModHost(ModLoadPlan? plan, ModDiagnostics diagnostics, IPlugin[] plugins, bool assembliesAllowed)
    {
        Plan = plan;
        Diagnostics = diagnostics;
        _plugins = plugins;
        AssembliesAllowed = assembliesAllowed;
        Content = new ModContent(plan?.Packages ?? []);
    }

    public ModLoadPlan? Plan { get; }

    public ModDiagnostics Diagnostics { get; }

    /// <summary>False when no game could be resolved — the caller has nothing to run.</summary>
    public bool IsLoaded => Plan is not null;

    /// <summary>The running game package. Only valid when <see cref="IsLoaded"/>.</summary>
    public ModPackage Game => Plan?.Game ?? throw new InvalidOperationException("No game package is loaded.");

    /// <summary>Loaded packages in load order, the game included.</summary>
    public IReadOnlyList<ModPackage> Packages => Plan?.Packages ?? [];

    /// <summary>The layered content of those packages.</summary>
    public ModContent Content { get; }

    /// <summary>Plugin instances in package load order.</summary>
    public IReadOnlyList<IPlugin> Plugins => _plugins;

    /// <summary>False when running in safe mode, so callers can say so rather than guess.</summary>
    public bool AssembliesAllowed { get; }

    /// <summary>Ordered <c>id@version</c> identity of this package set; see <see cref="ModLoadPlan.Fingerprint"/>.</summary>
    public string Fingerprint => Plan?.Fingerprint ?? string.Empty;

    /// <summary>Plugins implementing a capability interface, in load order. Not a hot path.</summary>
    public IEnumerable<T> PluginsOf<T>() where T : class
    {
        foreach (IPlugin plugin in _plugins)
            if (plugin is T typed)
                yield return typed;
    }

    public static ModHost Load(ModHostOptions options)
    {
        ModDiagnostics diagnostics = new();

        IReadOnlyList<ModPackage> discovered = ModDiscovery.Scan(options.SearchRoots, diagnostics);
        ModLoadPlan? plan = ModResolver.Resolve(discovered, options.GameId, options.Disabled, diagnostics);

        if (plan is null)
            return new ModHost(null, diagnostics, NoPlugins, options.LoadAssemblies);

        List<IPlugin> plugins = [];

        foreach (ModPackage package in plan.Packages)
        {
            if (!package.HasAssembly)
                continue;

            if (!options.LoadAssemblies)
            {
                diagnostics.Info(package.Id, "code not loaded (safe mode); its content still applies.");
                continue;
            }

            plugins.AddRange(ModAssemblyLoader.Load(package, diagnostics));
        }

        return new ModHost(plan, diagnostics, [.. plugins], options.LoadAssemblies);
    }

    /// <summary>A one-line-per-package summary, for <c>--list-mods</c> and the debug overlay.</summary>
    public IEnumerable<string> Describe()
    {
        if (Plan is null)
        {
            yield return "no game loaded";
            yield break;
        }

        foreach (ModPackage package in Plan.Packages)
        {
            string kind = package.Kind.ToString().ToLowerInvariant();
            string origin = package.Origin == ModOrigin.User ? "user" : "bundled";
            string code = package.HasAssembly ? (AssembliesAllowed ? ", code" : ", code (not loaded)") : "";
            yield return $"{package.Id} {package.Version} [{kind}, {origin}{code}] {package.Manifest.DisplayName}";
        }
    }
}
