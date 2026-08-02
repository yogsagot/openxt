using System.Numerics;
using OpenXt.Sim.Components;
using OpenXt.Sim.Data;

namespace OpenXt.Sim.Flight;

/// <summary>
/// The custom flight model. Not a rigid-body simulation: thrust and rotation are integrated here
/// so the feel stays authored rather than emergent, exactly like the sims this project descends from.
/// Every constant that shapes the feel comes from <see cref="ShipDefinition"/>.
/// </summary>
public static class FlightModel
{
    /// <summary>How hard the flight computer pulls angular velocity toward the commanded rate, per second.</summary>
    private const float AngularResponse = 6f;

    /// <summary>Assist-on damping pulling velocity toward the commanded direction, per second.</summary>
    private const float AssistResponse = 0.9f;

    public static void Integrate(ShipDefinition ship, in FlightControl control, ref Pose pose, ref Motion motion,
        float dt, bool assist = true)
    {
        IntegrateAngular(ship, control, ref pose, ref motion, dt);
        IntegrateLinear(ship, control, in pose, ref motion, dt, assist);
        pose.Position += motion.Linear * dt;
    }

    private static void IntegrateAngular(ShipDefinition ship, in FlightControl control, ref Pose pose,
        ref Motion motion, float dt)
    {
        Vector3 commanded = new(
            Math.Clamp(control.Turn.X, -1f, 1f) * ship.PitchRate,
            Math.Clamp(control.Turn.Y, -1f, 1f) * ship.YawRate,
            Math.Clamp(control.Turn.Z, -1f, 1f) * ship.RollRate);

        // Exponential approach, framerate-independent: 1 - e^(-k*dt).
        float blend = 1f - MathF.Exp(-AngularResponse * dt);
        motion.Angular += (commanded - motion.Angular) * blend;

        Vector3 delta = motion.Angular * dt;
        if (delta.LengthSquared() > 0f)
        {
            Quaternion spin = Quaternion.CreateFromYawPitchRoll(delta.Y, delta.X, delta.Z);
            pose.Orientation = Quaternion.Normalize(pose.Orientation * spin);
        }
    }

    private static void IntegrateLinear(ShipDefinition ship, in FlightControl control, in Pose pose,
        ref Motion motion, float dt, bool assist)
    {
        Vector3 input = new(
            Math.Clamp(control.Thrust.X, -1f, 1f) * ship.ManeuverThrust,
            Math.Clamp(control.Thrust.Y, -1f, 1f) * ship.ManeuverThrust,
            Math.Clamp(control.Thrust.Z, -1f, 1f) * ship.MainThrust);

        Vector3 worldForce = Vector3.Transform(input, pose.Orientation);
        motion.Linear += worldForce / ship.Mass * dt;

        if (!assist)
            return;

        // Flight assist: bleed off drift that is not commanded and hold the cruise ceiling.
        // Newtonian purists get this switched off; the stock feel is arcade-Newtonian.
        Vector3 desiredDirection = worldForce.LengthSquared() > 0f ? Vector3.Normalize(worldForce) : Vector3.Zero;
        Vector3 desired = desiredDirection * ship.CruiseSpeed * ThrottleMagnitude(control.Thrust);

        float blend = 1f - MathF.Exp(-AssistResponse * dt);
        motion.Linear += (desired - motion.Linear) * blend;
    }

    private static float ThrottleMagnitude(Vector3 thrust)
    {
        float magnitude = thrust.Length();
        return magnitude > 1f ? 1f : magnitude;
    }
}
