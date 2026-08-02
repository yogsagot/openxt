using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuUtilities;
using BepuUtilities.Memory;

namespace OpenXt.Sim.Physics;

/// <summary>
/// Thin wrapper over a Bepu <see cref="Simulation"/>. Ships are kinematic bodies whose poses are
/// pushed from the flight model each tick; Bepu supplies the broadphase and the spatial queries
/// (ray casts for targeting, weapons and AI line-of-sight).
/// </summary>
public sealed class PhysicsWorld : IDisposable
{
    private readonly BufferPool _pool = new();
    private readonly ThreadDispatcher _dispatcher;

    public Simulation Simulation { get; }

    public PhysicsWorld(int threadCount = 0)
    {
        if (threadCount <= 0)
            threadCount = Math.Max(1, Environment.ProcessorCount - 1);

        _dispatcher = new ThreadDispatcher(threadCount);
        Simulation = Simulation.Create(
            _pool,
            new NarrowPhaseCallbacks(),
            new PoseIntegratorCallbacks(),
            new SolveDescription(velocityIterationCount: 4, substepCount: 1));
    }

    /// <summary>Registers a ship hull as a kinematic sphere. Cheap, and good enough until hulls get real shapes.</summary>
    public BodyHandle AddShipHull(Vector3 position, Quaternion orientation, float radius)
    {
        TypedIndex shape = Simulation.Shapes.Add(new Sphere(radius));
        return Simulation.Bodies.Add(BodyDescription.CreateKinematic(
            new RigidPose(position, orientation),
            new CollidableDescription(shape, 0.1f),
            new BodyActivityDescription(0.01f)));
    }

    /// <summary>Pushes the authoritative sim pose into the collidable. Call after the flight model runs.</summary>
    public void SyncPose(BodyHandle handle, Vector3 position, Quaternion orientation, Vector3 linearVelocity)
    {
        BodyReference body = Simulation.Bodies[handle];
        body.Pose.Position = position;
        body.Pose.Orientation = orientation;
        body.Velocity.Linear = linearVelocity;
        body.UpdateBounds();
    }

    public void Remove(BodyHandle handle) => Simulation.Bodies.Remove(handle);

    /// <summary>Advances the broadphase. No integration happens here — velocities are ours.</summary>
    public void Step(float dt) => Simulation.Timestep(dt, _dispatcher);

    public void Dispose()
    {
        Simulation.Dispose();
        _dispatcher.Dispose();
        _pool.Clear();
    }
}
