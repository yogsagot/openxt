using System.Numerics;
using DefaultEcs;
using OpenXt.Sim.Components;
using OpenXt.Sim.Data;
using OpenXt.Sim.Physics;
using OpenXt.Sim.Systems;

namespace OpenXt.Sim;

/// <summary>
/// One simulated locale — the unit the player jumps between. Owns its own ECS world and
/// broadphase so sectors can be simulated, suspended and (later) streamed independently.
///
/// It owns no behaviour of its own: what runs in a tick comes from the <see cref="SectorSystemPlan"/>
/// the loaded packages produced, including the engine's own flight model.
/// </summary>
public sealed class Sector : IDisposable
{
    private readonly EntitySet _all;
    private readonly SectorSystems _systems;

    public string Name { get; }
    public World World { get; }
    public PhysicsWorld Physics { get; }

    /// <summary>The ship catalog this sector spawns from — merged from every loaded package.</summary>
    public ShipCatalog Ships { get; }

    /// <summary>Live entity count. Cached set — never build an EntitySet per frame.</summary>
    public int EntityCount => _all.Count;

    /// <summary>How many systems run in this sector, engine and mods together.</summary>
    public int SystemCount => _systems.Count;

    public Sector(string name, ShipCatalog catalog, SectorSystemPlan systems)
    {
        Name = name;
        Ships = catalog;
        World = new World();
        Physics = new PhysicsWorld();
        _all = World.GetEntities().With<Pose>().AsSet();

        // Last, so a system's constructor can rely on everything above being ready.
        _systems = systems.Instantiate(this);
    }

    public Entity SpawnShip(string definitionId, Vector3 position, Quaternion orientation)
    {
        int index = Ships.IndexOf(definitionId);
        ShipDefinition definition = Ships[index];

        Entity entity = World.CreateEntity();
        entity.Set(new Pose { Position = position, Orientation = orientation });
        entity.Set(new Motion());
        entity.Set(new FlightControl());
        entity.Set(new ShipRef { DefinitionIndex = index });
        entity.Set(new Collider
        {
            Body = Physics.AddShipHull(position, orientation, definition.HullRadius),
            Radius = definition.HullRadius,
        });
        return entity;
    }

    /// <summary>
    /// One fixed simulation tick. Deterministic: no wall-clock, no unseeded randomness.
    ///
    /// The stage order is the tick, and the physics step sits at a fixed point in it rather than
    /// being a slot anything can register into — see <see cref="SectorStage"/>.
    /// </summary>
    public void Step(float dt)
    {
        _systems.Update(this, SectorStage.Intent, dt);
        _systems.Update(this, SectorStage.Movement, dt);

        Physics.Step(dt);

        _systems.Update(this, SectorStage.PostPhysics, dt);
        _systems.Update(this, SectorStage.Late, dt);
    }

    public void Dispose()
    {
        _all.Dispose();
        _systems.Dispose();
        Physics.Dispose();
        World.Dispose();
    }
}
