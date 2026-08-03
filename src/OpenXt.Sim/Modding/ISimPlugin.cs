using OpenXt.Modding;
using OpenXt.Sim.Data;
using OpenXt.Sim.Systems;

namespace OpenXt.Sim.Modding;

/// <summary>
/// What a package registers with the simulation. Everything a plugin adds is declared here, once,
/// before any world exists — so the set of systems that will run is known and orderable up front
/// rather than mutating mid-tick.
/// </summary>
public interface ISimRegistry
{
    /// <summary>The merged ship catalog, in case a system needs to resolve ids at registration time.</summary>
    ShipCatalog Ships { get; }

    /// <summary>The running game's ruleset.</summary>
    GameRuleset Rules { get; }

    /// <summary>
    /// Adds a system to the tick.
    /// </summary>
    /// <param name="id">Qualified name, conventionally <c>yourpackage.something</c>.</param>
    /// <param name="stage">Which part of the tick it runs in.</param>
    /// <param name="factory">Builds the system for one sector; called once per sector.</param>
    /// <param name="order">Tie-break inside the stage; lower first, equal orders sort by id.</param>
    void AddSectorSystem(string id, SectorStage stage, Func<Sector, ISectorSystem> factory, int order = 0);
}

/// <summary>
/// Implemented by a plugin that adds simulation behaviour. Lives in the headless layer on purpose:
/// a mod that only implements this loads and runs in a console host with no window and no graphics
/// device, which is what keeps the simulation testable.
/// </summary>
public interface ISimPlugin : IPlugin
{
    void ConfigureSim(ISimRegistry registry);
}
