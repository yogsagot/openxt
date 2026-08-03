using OpenXt.Sim.Data;
using OpenXt.Sim.Systems;

namespace OpenXt.Sim;

/// <summary>
/// Root of all persistent world state and the save/load anchor. Runs headless: this type and
/// everything it owns must stay usable with no graphics device and no window, which is what
/// makes the simulation testable.
///
/// It holds what the loaded packages produced — the merged ship catalog, the running game's
/// ruleset, and the ordered system plan every sector is built from — so nothing below this line
/// needs to know that mods exist.
/// </summary>
public sealed class Universe : IDisposable
{
    private readonly List<Sector> _sectors = [];

    /// <summary>Simulation ticks elapsed. With a fixed timestep this is the authoritative clock.</summary>
    public long Tick { get; private set; }

    public ShipCatalog Ships { get; }

    /// <summary>The running game's rules and start state.</summary>
    public GameRuleset Rules { get; }

    /// <summary>The systems every sector runs, in their settled order.</summary>
    public SectorSystemPlan Systems { get; }

    public IReadOnlyList<Sector> Sectors => _sectors;

    public Universe(ShipCatalog ships, GameRuleset rules, SectorSystemPlan systems)
    {
        Ships = ships;
        Rules = rules;
        Systems = systems;
    }

    public Sector CreateSector(string name)
    {
        Sector sector = new(name, Ships, Systems);
        _sectors.Add(sector);
        return sector;
    }

    /// <summary>Advances every active sector by one fixed tick.</summary>
    public void Step(float dt)
    {
        for (int i = 0; i < _sectors.Count; i++)
            _sectors[i].Step(dt);

        Tick++;
    }

    public void Dispose()
    {
        foreach (Sector sector in _sectors)
            sector.Dispose();
        _sectors.Clear();
    }
}
