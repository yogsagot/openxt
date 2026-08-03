using OpenXt.Sim;
using OpenXt.Sim.Modding;
using OpenXt.Sim.Systems;

namespace OpenXt.Modding.Tests;

/// <summary>
/// Plugins used by <see cref="PluginLoadingTests"/>. They live in the test assembly, which that
/// test copies into a package and declares as the package's assembly — so the loader really does
/// read a file from disk, find <see cref="IPlugin"/> types in it and construct them.
/// </summary>
public sealed class CountingSimPlugin : ISimPlugin
{
    public const string SystemId = "test.counter";

    public void ConfigureSim(ISimRegistry registry) =>
        registry.AddSectorSystem(SystemId, SectorStage.Late, static _ => new CountingSystem());
}

public sealed class CountingSystem : ISectorSystem
{
    public int Ticks { get; private set; }

    public void Update(Sector sector, float dt) => Ticks++;
}

/// <summary>Registers one system, then throws — everything it registered must be rolled back.</summary>
public sealed class DoomedSimPlugin : ISimPlugin
{
    public const string SystemId = "test.doomed";

    public void ConfigureSim(ISimRegistry registry)
    {
        registry.AddSectorSystem(SystemId, SectorStage.Late, static _ => new CountingSystem());
        throw new InvalidOperationException("this plugin is broken on purpose");
    }
}
