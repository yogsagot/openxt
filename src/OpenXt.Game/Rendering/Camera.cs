using Microsoft.Xna.Framework;

namespace OpenXt.Game.Rendering;

/// <summary>
/// Render-side camera. Converts from the simulation's System.Numerics world into MonoGame matrices —
/// this is the boundary where the two vector types are allowed to meet.
/// </summary>
public struct Camera
{
    public Vector3 Position;
    public Quaternion Orientation;
    public float FieldOfView = MathHelper.ToRadians(65f);

    /// <summary>Space is big: the far plane has to be, too. Depth precision comes from the near plane.</summary>
    public float NearPlane = 1f;
    public float FarPlane = 500_000f;

    public Camera() { }

    /// <summary>
    /// The simulation's forward axis is +Z (see <c>FlightControl.Thrust</c>, where positive Z is
    /// main thrust), not MonoGame's -Z. The world is the sim's, so the camera follows the sim's
    /// convention; MonoGame's Forward constant would aim it backwards out of the ship's nose.
    /// </summary>
    public static readonly Vector3 SimForward = Vector3.Backward;

    public readonly Matrix View
    {
        get
        {
            Vector3 forward = Vector3.Transform(SimForward, Orientation);
            Vector3 up = Vector3.Transform(Vector3.Up, Orientation);
            return Matrix.CreateLookAt(Position, Position + forward, up);
        }
    }

    public readonly Matrix Projection(float aspectRatio) =>
        Matrix.CreatePerspectiveFieldOfView(FieldOfView, aspectRatio, NearPlane, FarPlane);

    public static Vector3 ToXna(System.Numerics.Vector3 value) => new(value.X, value.Y, value.Z);

    public static Quaternion ToXna(System.Numerics.Quaternion value) => new(value.X, value.Y, value.Z, value.W);
}
