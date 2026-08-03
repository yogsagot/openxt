namespace OpenXt.Sim.Systems;

/// <summary>
/// One registered system: what it is, when it runs, and how to build it for a sector.
/// </summary>
/// <param name="Id">Qualified name, conventionally <c>package.system</c>. Shown in diagnostics.</param>
/// <param name="Stage">Which part of the tick it belongs to.</param>
/// <param name="Order">Tie-break inside a stage; lower runs first. Equal orders sort by id.</param>
/// <param name="Factory">Creates the instance for one sector.</param>
public sealed record SectorSystemRegistration(
    string Id,
    SectorStage Stage,
    int Order,
    Func<Sector, ISectorSystem> Factory);

/// <summary>
/// The registered systems, sorted once into the order every sector will run them in.
///
/// The sort is (stage, order, ordinal id) — fully determined by the registration set, never by
/// registration timing or dictionary iteration. Two machines running the same package set step the
/// same simulation in the same order, which is the precondition for a deterministic fixed-step loop
/// and for any future save that replays it.
/// </summary>
public sealed class SectorSystemPlan
{
    private readonly SectorSystemRegistration[] _registrations;

    public SectorSystemPlan(IEnumerable<SectorSystemRegistration> registrations)
    {
        List<SectorSystemRegistration> sorted = [.. registrations];
        sorted.Sort(static (a, b) =>
        {
            int result = a.Stage.CompareTo(b.Stage);
            if (result != 0)
                return result;

            result = a.Order.CompareTo(b.Order);
            return result != 0 ? result : string.CompareOrdinal(a.Id, b.Id);
        });

        _registrations = [.. sorted];
    }

    /// <summary>An empty plan — a sector with no behaviour at all. Mostly useful in tests.</summary>
    public static readonly SectorSystemPlan Empty = new([]);

    public IReadOnlyList<SectorSystemRegistration> Registrations => _registrations;

    /// <summary>Instantiates every system for one sector.</summary>
    public SectorSystems Instantiate(Sector sector) => new(this, sector);
}

/// <summary>
/// One sector's live systems, pre-partitioned per stage at construction so a tick is four indexed
/// loops over arrays — no filtering, no allocation, no delegate churn in the hot path.
/// </summary>
public sealed class SectorSystems : IDisposable
{
    private readonly ISectorSystem[][] _byStage;
    private readonly ISectorSystem[] _all;

    internal SectorSystems(SectorSystemPlan plan, Sector sector)
    {
        int stageCount = Enum.GetValues<SectorStage>().Length;
        List<ISectorSystem>[] staged = new List<ISectorSystem>[stageCount];
        for (int i = 0; i < staged.Length; i++)
            staged[i] = [];

        List<ISectorSystem> all = [];

        foreach (SectorSystemRegistration registration in plan.Registrations)
        {
            ISectorSystem system = registration.Factory(sector);
            staged[(int)registration.Stage].Add(system);
            all.Add(system);
        }

        _byStage = new ISectorSystem[stageCount][];
        for (int i = 0; i < staged.Length; i++)
            _byStage[i] = [.. staged[i]];

        _all = [.. all];
    }

    public int Count => _all.Length;

    public void Update(Sector sector, SectorStage stage, float dt)
    {
        ISectorSystem[] systems = _byStage[(int)stage];
        for (int i = 0; i < systems.Length; i++)
            systems[i].Update(sector, dt);
    }

    public void Dispose()
    {
        for (int i = 0; i < _all.Length; i++)
            (_all[i] as IDisposable)?.Dispose();
    }
}
