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

    public readonly Matrix View
    {
        get
        {
            Vector3 forward = Vector3.Transform(Vector3.Forward, Orientation);
            Vector3 up = Vector3.Transform(Vector3.Up, Orientation);
            return Matrix.CreateLookAt(Position, Position + forward, up);
        }
    }

    public readonly Matrix Projection(float aspectRatio) =>
        Matrix.CreatePerspectiveFieldOfView(FieldOfView, aspectRatio, NearPlane, FarPlane);

    public static Vector3 ToXna(System.Numerics.Vector3 value) => new(value.X, value.Y, value.Z);

    public static Quaternion ToXna(System.Numerics.Quaternion value) => new(value.X, value.Y, value.Z, value.W);
}
