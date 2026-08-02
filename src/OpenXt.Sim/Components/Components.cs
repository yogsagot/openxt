using System.Numerics;
using BepuPhysics;

namespace OpenXt.Sim.Components;

/// <summary>World-space placement. System.Numerics only — no MonoGame types cross this boundary.</summary>
public struct Pose
{
    public Vector3 Position;
    public Quaternion Orientation;
}

/// <summary>Linear and angular velocity, in metres and radians per second.</summary>
public struct Motion
{
    public Vector3 Linear;
    public Vector3 Angular;
}

/// <summary>
/// Normalised pilot/AI intent, each component in [-1, 1]. The flight model turns this into
/// forces; nothing else should write directly to <see cref="Motion"/>.
/// </summary>
public struct FlightControl
{
    /// <summary>X = strafe, Y = lift, Z = main thrust (positive is forward).</summary>
    public Vector3 Thrust;

    /// <summary>X = pitch, Y = yaw, Z = roll.</summary>
    public Vector3 Turn;
}

/// <summary>Index into <see cref="Data.ShipCatalog"/>; ship stats are never stored per entity.</summary>
public struct ShipRef
{
    public int DefinitionIndex;
}

/// <summary>Link to the Bepu collidable that mirrors this entity's pose for collision queries.</summary>
public struct Collider
{
    public BodyHandle Body;
    public float Radius;
}

/// <summary>Present on the entity the local player is flying.</summary>
public struct PlayerControlled;
