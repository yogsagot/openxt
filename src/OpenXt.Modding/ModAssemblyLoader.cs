using System.Reflection;
using System.Runtime.Loader;

namespace OpenXt.Modding;

/// <summary>
/// Loads one package's assembly into its own <see cref="AssemblyLoadContext"/>.
///
/// The rule that matters: anything already loaded by the host — OpenXt's own assemblies, DefaultEcs,
/// Bepu, MonoGame, the framework — resolves to the default context rather than being loaded a
/// second time. Without that, a mod shipping its own copy of OpenXt.Sim.dll would produce a second,
/// incompatible <c>Pose</c> type and the failure would read as a nonsensical cast error rather than
/// "your mod bundled the engine". Private dependencies of the mod itself still load privately, via
/// the package's deps.json.
///
/// Contexts are collectible so a future dev-time reload can drop one, though nothing unloads today.
/// </summary>
internal sealed class ModAssemblyLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public ModAssemblyLoadContext(ModPackage package, string assemblyPath)
        : base($"mod:{package.Id}", isCollectible: true) =>
        _resolver = new AssemblyDependencyResolver(assemblyPath);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Share whatever the host already has. Returning null falls through to the default context.
        foreach (Assembly loaded in Default.Assemblies)
            if (string.Equals(loaded.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase))
                return null;

        string? path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        string? path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? nint.Zero : LoadUnmanagedDllFromPath(path);
    }
}

/// <summary>Instantiates the <see cref="IPlugin"/> types a package's assembly exposes.</summary>
internal static class ModAssemblyLoader
{
    public static IReadOnlyList<IPlugin> Load(ModPackage package, ModDiagnostics diagnostics)
    {
        if (package.AssemblyPath is not { } path)
            return [];

        if (!File.Exists(path))
        {
            diagnostics.Error(package.Id, $"declares assembly '{package.Manifest.Assembly}', which is missing.");
            return [];
        }

        Assembly assembly;

        try
        {
            assembly = new ModAssemblyLoadContext(package, path).LoadFromAssemblyPath(path);
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or FileLoadException)
        {
            diagnostics.Error(package.Id, $"assembly could not be loaded: {ex.Message}");
            return [];
        }

        Type[] types;

        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Usually a mod built against a different engine version. Keep whatever did load.
            diagnostics.Warning(package.Id, $"some types could not be loaded: {ex.Message}");

            List<Type> loaded = [];
            foreach (Type? type in ex.Types)
                if (type is not null)
                    loaded.Add(type);

            types = [.. loaded];
        }

        List<IPlugin> plugins = [];

        // Ordinal by full name: a package with several plugin types configures them in the same
        // order on every machine, because that order reaches a deterministic simulation.
        Array.Sort(types, static (a, b) => string.CompareOrdinal(a.FullName, b.FullName));

        foreach (Type type in types)
        {
            if (!typeof(IPlugin).IsAssignableFrom(type) || type.IsAbstract || type.IsInterface)
                continue;

            try
            {
                if (Activator.CreateInstance(type) is IPlugin plugin)
                    plugins.Add(plugin);
            }
            catch (Exception ex) when (ex is MissingMethodException or TargetInvocationException
                                          or TypeLoadException or MemberAccessException)
            {
                diagnostics.Error(package.Id, $"plugin '{type.FullName}' could not be created: {ex.Message}");
            }
        }

        if (plugins.Count == 0)
            diagnostics.Warning(package.Id, $"assembly '{package.Manifest.Assembly}' contains no {nameof(IPlugin)}.");

        return plugins;
    }
}
