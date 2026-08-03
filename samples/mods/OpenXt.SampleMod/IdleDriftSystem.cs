using DefaultEcs;
using OpenXt.Sim;
using OpenXt.Sim.Components;
using OpenXt.Sim.Systems;

namespace OpenXt.SampleMod;

/// <summary>
/// Gives every unpiloted ship somewhere to be: a little forward thrust and a slow weave.
///
/// Deliberately the crudest possible stand-in for AI, but it is a real simulation system —
/// registered into <see cref="SectorStage.Intent"/>, running inside the fixed step, writing only
/// <see cref="FlightControl"/> and leaving the flight model to do the moving. That is the shape a
/// mod's behaviour should take.
///
/// Determinism: the weave comes from the tick count and the entity's position in the set, never
/// from wall-clock time or an unseeded <c>Random</c>, so two machines running this mod run the same
/// simulation. Allocation: the entity set is built once here, not per tick.
/// </summary>
public sealed class IdleDriftSystem : ISectorSystem, IDisposable
{
    private readonly EntitySet _drifters;
    private long _tick;

    public IdleDriftSystem(Sector sector) =>
        _drifters = sector.World.GetEntities()
            .With<FlightControl>()
            .With<Motion>()
            .Without<PlayerControlled>()
            .AsSet();

    /// <summary>How many ships this mod is currently flying. Read by the debug panel.</summary>
    public int Count => _drifters.Count;

    public void Update(Sector sector, float dt)
    {
        _tick++;
        float time = _tick * dt;

        ReadOnlySpan<Entity> entities = _drifters.GetEntities();
        for (int i = 0; i < entities.Length; i++)
        {
            ref FlightControl control = ref entities[i].Get<FlightControl>();

            float phase = i * 0.7f;
            control.Thrust.Z = 0.25f;
            control.Turn.Y = MathF.Sin(time * 0.2f + phase) * 0.15f;
            control.Turn.X = MathF.Sin(time * 0.11f + phase) * 0.05f;
        }
    }

    public void Dispose() => _drifters.Dispose();
}
