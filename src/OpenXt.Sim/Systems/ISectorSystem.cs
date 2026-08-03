namespace OpenXt.Sim.Systems;

/// <summary>
/// Where in a tick a system runs. The order of the stages is the order of a tick, and it is fixed:
/// a mod chooses a stage, not a position in the frame.
///
/// The physics step is deliberately not a stage. It runs between <see cref="Movement"/> and
/// <see cref="PostPhysics"/>, owned by <see cref="Sector"/>, so no package can reorder the
/// broadphase out from under everything else that assumes poses are settled.
/// </summary>
public enum SectorStage
{
    /// <summary>Decide what to do: player input already applied, AI, targeting, orders.</summary>
    Intent,

    /// <summary>Turn intent into motion. The flight model runs here.</summary>
    Movement,

    /// <summary>After the broadphase has stepped: collision response, damage, docking.</summary>
    PostPhysics,

    /// <summary>Bookkeeping that wants everything else settled — economy ticks, statistics, cleanup.</summary>
    Late,
}

/// <summary>
/// A unit of simulation behaviour. One is created per sector, so a system may cache that sector's
/// entity sets in its constructor — which is the point, since building an <c>EntitySet</c> per tick
/// would allocate.
///
/// Runs inside the fixed-step loop: no wall-clock, no unseeded randomness, no LINQ or closures in
/// <see cref="Update"/>. Implement <see cref="IDisposable"/> too if there is anything to release;
/// the sector will call it.
/// </summary>
public interface ISectorSystem
{
    void Update(Sector sector, float dt);
}
