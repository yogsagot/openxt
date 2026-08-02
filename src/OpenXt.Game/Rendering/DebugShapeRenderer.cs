using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace OpenXt.Game.Rendering;

/// <summary>
/// Batched line renderer — placeholder geometry until real meshes land, and permanent tooling for
/// visualising hulls, headings and spatial partitions. Vertices are pooled; nothing allocates per frame.
/// </summary>
public sealed class DebugShapeRenderer : IDisposable
{
    private const int MaxVertices = 32_768;

    private readonly GraphicsDevice _device;
    private readonly BasicEffect _effect;
    private readonly VertexPositionColor[] _vertices = new VertexPositionColor[MaxVertices];
    private int _count;

    public DebugShapeRenderer(GraphicsDevice device)
    {
        _device = device;
        _effect = new BasicEffect(device) { VertexColorEnabled = true, LightingEnabled = false, TextureEnabled = false };
    }

    public void Begin()
    {
        _count = 0;
    }

    public void Line(Vector3 from, Vector3 to, Color color)
    {
        if (_count + 2 > MaxVertices)
            return;

        _vertices[_count++] = new VertexPositionColor(from, color);
        _vertices[_count++] = new VertexPositionColor(to, color);
    }

    /// <summary>Wireframe box in the ship's local frame — stands in for a hull mesh.</summary>
    public void Box(Vector3 center, Quaternion orientation, float halfExtent, Color color)
    {
        Span<Vector3> corners = stackalloc Vector3[8];
        int i = 0;
        for (int x = -1; x <= 1; x += 2)
        for (int y = -1; y <= 1; y += 2)
        for (int z = -1; z <= 1; z += 2)
            corners[i++] = center + Vector3.Transform(new Vector3(x, y, z) * halfExtent, orientation);

        // Corner index bits are (x,y,z); an edge joins corners differing in exactly one bit.
        for (int a = 0; a < 8; a++)
        for (int bit = 1; bit <= 4; bit <<= 1)
        {
            int b = a ^ bit;
            if (b > a)
                Line(corners[a], corners[b], color);
        }
    }

    /// <summary>Heading indicator so orientation is readable before there is any art.</summary>
    public void Axes(Vector3 center, Quaternion orientation, float length)
    {
        Line(center, center + Vector3.Transform(Vector3.Right, orientation) * length, Color.Red);
        Line(center, center + Vector3.Transform(Vector3.Up, orientation) * length, Color.Lime);
        Line(center, center + Vector3.Transform(Vector3.Forward, orientation) * length, Color.DeepSkyBlue);
    }

    public void End(in Matrix view, in Matrix projection)
    {
        if (_count == 0)
            return;

        _device.DepthStencilState = DepthStencilState.Default;
        _device.BlendState = BlendState.Opaque;
        _device.RasterizerState = RasterizerState.CullNone;

        _effect.World = Matrix.Identity;
        _effect.View = view;
        _effect.Projection = projection;

        foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _device.DrawUserPrimitives(PrimitiveType.LineList, _vertices, 0, _count / 2);
        }
    }

    public void Dispose() => _effect.Dispose();
}
