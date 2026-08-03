using OpenXt.Sim.Data;
using OpenXt.Sim.Systems;

namespace OpenXt.Sim.Modding;

/// <summary>
/// Collects what the loaded plugins register, then freezes it into a <see cref="SectorSystemPlan"/>.
///
/// Ids are unique across all packages. A collision is a real conflict — two mods claiming the same
/// system name — and is reported rather than resolved by load order, because silently keeping one
/// of them is how a mod appears installed but does nothing.
/// </summary>
public sealed class SimRegistry(ShipCatalog ships, GameRuleset rules) : ISimRegistry
{
    private readonly List<SectorSystemRegistration> _systems = [];
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);

    public ShipCatalog Ships { get; } = ships;

    public GameRuleset Rules { get; } = rules;

    public IReadOnlyList<SectorSystemRegistration> Systems => _systems;

    public void AddSectorSystem(string id, SectorStage stage, Func<Sector, ISectorSystem> factory, int order = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(factory);

        if (!_ids.Add(id))
            throw new InvalidOperationException($"A sector system with id '{id}' is already registered.");

        _systems.Add(new SectorSystemRegistration(id, stage, order, factory));
    }

    public SectorSystemPlan Build() => new(_systems);

    /// <summary>How much has been registered so far; paired with <see cref="RollbackTo"/>.</summary>
    internal int Checkpoint => _systems.Count;

    /// <summary>
    /// Discards everything registered since a checkpoint. Used when a plugin throws part-way
    /// through configuring itself: half-registering a mod is worse than not loading it, because
    /// what runs then matches neither what the author wrote nor what the player installed.
    /// </summary>
    internal void RollbackTo(int checkpoint)
    {
        for (int i = _systems.Count - 1; i >= checkpoint; i--)
        {
            _ids.Remove(_systems[i].Id);
            _systems.RemoveAt(i);
        }
    }
}
