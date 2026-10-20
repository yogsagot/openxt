using System.Text.Json;

namespace OpenXt.Assets;

/// <summary>
/// A converted scene: the bodies that make up one multi-part object (a station, mostly) and where
/// each sits. Written by the importer, read by the game; positions are already in metres and
/// rotations already quaternions, so the game does no conversion.
///
/// JSON, unlike meshes: a scene is a few dozen numbers, and readable cache files are worth more
/// here than the bytes. Properties are <c>set</c>, never <c>init</c> — the same source-generator
/// trap documented on <see cref="CacheManifest"/>.
/// </summary>
public sealed record OxScene
{
    public int SceneId { get; set; }
    public string? Title { get; set; }

    /// <summary>Bounding radius of the structural parts about the scene origin, metres.</summary>
    public float BoundingRadius { get; set; }

    public OxScenePart[] Parts { get; set; } = [];

    public void WriteFile(string path)
    {
        using FileStream stream = File.Create(path);
        JsonSerializer.Serialize(stream, this, AssetJsonContext.Default.OxScene);
    }

    public static OxScene? ReadFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return JsonSerializer.Deserialize(stream, AssetJsonContext.Default.OxScene);
    }
}

/// <summary>One placed body, in the scene's frame.</summary>
public sealed record OxScenePart
{
    public int BodyId { get; set; }

    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    /// <summary>Rotation quaternion.</summary>
    public float QX { get; set; }
    public float QY { get; set; }
    public float QZ { get; set; }
    public float QW { get; set; } = 1f;

    /// <summary>Uniform scale the body is drawn at within the scene; see the importer's scene scale.</summary>
    public float Scale { get; set; } = 1f;

    /// <summary>
    /// A light, glow, neon sign or logo: a flat sprite-like body meant to be drawn additively and
    /// often camera-facing. Drawn as solid geometry they read as floating panels, so the renderer
    /// skips them until it has a glow pass.
    /// </summary>
    public bool Effect { get; set; }
}
