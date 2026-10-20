using System.Numerics;
using OpenXt.Assets;
using OpenXt.XArchive;

namespace OpenXt.AssetImport;

/// <summary>What a scene needs to know about a converted body.</summary>
public readonly record struct BodyInfo(int LodCount, int Vertices, float BoundingRadius);

/// <summary>
/// Turns a parsed scene into an <see cref="OxScene"/>: positions to metres with the same scale and
/// axes as <see cref="MeshConverter"/>, rotations to quaternions, each part's body scale, and each
/// part tagged structural or effect.
/// </summary>
public sealed class SceneConverter(float metresPerUnit, float bodyScale = SceneConverter.DefaultBodyScale)
{
    /// <summary>
    /// How much larger a body is drawn inside a scene than on its own. Fitted, not read: at the
    /// positions the scenes give, station parts only meet at roughly four times the size their
    /// body files have alone. At 1x a docking gate floats tens of metres off its station and its
    /// ring (42 m) is narrower than the 48 m fighter meant to fly through it; at 4x the gates sit
    /// on the hulls in both games. Fits per station ranged from about 3.3 to 5.1, so this is the
    /// round middle, not a measurement — the importer takes --scene-scale to correct it without a
    /// rebuild, and records the value in each scene part.
    /// </summary>
    public const float DefaultBodyScale = 4f;

    private readonly float _scale = metresPerUnit;
    private readonly float _bodyScale = bodyScale;

    public OxScene Convert(int sceneId, BodScene scene, IReadOnlyDictionary<int, BodyInfo> bodies)
    {
        List<OxScenePart> parts = [];
        float radius = 0f;

        foreach (ScenePart part in scene.Parts)
        {
            // The camera, and anything placing a body the archive does not have.
            if (part.BodyId < 0 || !bodies.TryGetValue(part.BodyId, out BodyInfo body))
                continue;

            (Vector3 position, float turns, Vector3 axis) = part.RestPose;
            Quaternion rotation = Rotation(turns, axis);
            Vector3 metres = position * _scale;
            bool effect = IsEffect(body);

            parts.Add(new OxScenePart
            {
                BodyId = part.BodyId,
                X = metres.X, Y = metres.Y, Z = metres.Z,
                QX = rotation.X, QY = rotation.Y, QZ = rotation.Z, QW = rotation.W,
                Scale = _bodyScale,
                Effect = effect,
            });

            if (!effect)
                radius = MathF.Max(radius, metres.Length() + body.BoundingRadius * _bodyScale);
        }

        return new OxScene
        {
            SceneId = sceneId,
            Title = scene.Title,
            BoundingRadius = radius,
            Parts = [.. parts],
        };
    }

    /// <summary>
    /// The archive stores a rotation as an angle in whole turns about a unit axis: 0.25 about +X is
    /// a quarter turn. The bodies are converted without mirroring, so the axis is used as found.
    /// </summary>
    public static Quaternion Rotation(float turns, Vector3 axis)
    {
        if (turns == 0f || axis.LengthSquared() < 1e-6f)
            return Quaternion.Identity;

        return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), turns * MathF.Tau);
    }

    /// <summary>
    /// Lights, glows, neon signs and logos. Every one in both games' station scenes has at most
    /// two levels of detail (structure has three or more), or is a sheet of a few dozen vertices
    /// spanning more than 150 m, like the "Chrome" glare.
    /// </summary>
    public static bool IsEffect(BodyInfo body) =>
        body.LodCount <= 2 || (body.Vertices <= 32 && body.BoundingRadius >= 75f);
}
