using OpenXt.Sim.Data;

namespace OpenXt.Sim;

/// <summary>
/// Root of all persistent world state and the save/load anchor. Runs headless: this type and
/// everything it owns must stay usable with no graphics device and no window, which is what
/// makes the simulation testable.
/// </summary>
public sealed class Universe : IDisposable
{
    private readonly List<Sector> _sectors = [];

    /// <summary>Simulation ticks elapsed. With a fixed timestep this is the authoritative clock.</summary>
    public long Tick { get; private set; }

    public ShipCatalog Ships { get; }

    public IReadOnlyList<Sector> Sectors => _sectors;

    public Universe(ShipCatalog ships) => Ships = ships;

    public Sector CreateSector(string name)
    {
        Sector sector = new(name, Ships);
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
