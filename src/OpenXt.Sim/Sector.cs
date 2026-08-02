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
/// </summary>
public sealed class Sector : IDisposable
{
    private readonly ShipCatalog _catalog;
    private readonly FlightSystem _flight;
    private readonly EntitySet _all;

    public string Name { get; }
    public World World { get; }
    public PhysicsWorld Physics { get; }

    /// <summary>Live entity count. Cached set — never build an EntitySet per frame.</summary>
    public int EntityCount => _all.Count;

    public Sector(string name, ShipCatalog catalog)
    {
        Name = name;
        _catalog = catalog;
        World = new World();
        Physics = new PhysicsWorld();
        _flight = new FlightSystem(World, catalog, Physics);
        _all = World.GetEntities().With<Pose>().AsSet();
    }

    public Entity SpawnShip(string definitionId, Vector3 position, Quaternion orientation)
    {
        int index = _catalog.IndexOf(definitionId);
        ShipDefinition definition = _catalog[index];

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

    /// <summary>One fixed simulation tick. Deterministic: no wall-clock, no unseeded randomness.</summary>
    public void Step(float dt)
    {
        _flight.Update(dt);
        Physics.Step(dt);
    }

    public void Dispose()
    {
        _all.Dispose();
        _flight.Dispose();
        Physics.Dispose();
        World.Dispose();
    }
}
