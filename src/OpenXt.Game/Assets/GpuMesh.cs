using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace OpenXt.Game.Assets;

/// <summary>A run of triangles sharing one texture, drawn with a single device call.</summary>
public sealed class GpuSubmesh
{
    public int StartIndex { get; init; }
    public int PrimitiveCount { get; init; }
    public Texture2D? Texture { get; init; }
    public Vector3 Diffuse { get; init; } = Vector3.One;
}

/// <summary>
/// A converted model resident on the GPU. Built from an <c>.oxmesh</c> in the asset cache; only
/// the highest-detail level is uploaded for now, since nothing selects LODs yet.
/// </summary>
public sealed class GpuMesh : IDisposable
{
    public required VertexBuffer Vertices { get; init; }
    public required IndexBuffer Indices { get; init; }
    public required GpuSubmesh[] Submeshes { get; init; }

    public float BoundingRadius { get; init; }
    public string? Title { get; init; }

    public void Dispose()
    {
        Vertices.Dispose();
        Indices.Dispose();
    }
}
