using DefaultEcs;
using OpenXt.Sim.Components;
using OpenXt.Sim.Data;
using OpenXt.Sim.Flight;
using OpenXt.Sim.Physics;

namespace OpenXt.Sim.Systems;

/// <summary>
/// Runs the flight model over every ship, then mirrors the resulting poses into the physics
/// broadphase. Hot path: no LINQ, no closures, no boxing.
/// </summary>
public sealed class FlightSystem : IDisposable
{
    private readonly EntitySet _ships;
    private readonly ShipCatalog _catalog;
    private readonly PhysicsWorld _physics;

    public FlightSystem(World world, ShipCatalog catalog, PhysicsWorld physics)
    {
        _catalog = catalog;
        _physics = physics;
        _ships = world.GetEntities()
            .With<Pose>()
            .With<Motion>()
            .With<FlightControl>()
            .With<ShipRef>()
            .AsSet();
    }

    public void Update(float dt)
    {
        ReadOnlySpan<Entity> entities = _ships.GetEntities();
        for (int i = 0; i < entities.Length; i++)
        {
            Entity entity = entities[i];

            ref Pose pose = ref entity.Get<Pose>();
            ref Motion motion = ref entity.Get<Motion>();
            ref FlightControl control = ref entity.Get<FlightControl>();
            ShipDefinition definition = _catalog[entity.Get<ShipRef>().DefinitionIndex];

            FlightModel.Integrate(definition, in control, ref pose, ref motion, dt);

            if (entity.Has<Collider>())
                _physics.SyncPose(entity.Get<Collider>().Body, pose.Position, pose.Orientation, motion.Linear);
        }
    }

    public void Dispose() => _ships.Dispose();
}
