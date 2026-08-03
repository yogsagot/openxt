using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenXt.Game.Assets;

namespace OpenXt.Game.Rendering;

/// <summary>
/// Draws converted meshes with a <see cref="BasicEffect"/>.
///
/// Deliberately plain: this is the first thing that puts real geometry on screen, not the shipping
/// renderer. The one non-obvious setting is wrapped texture addressing — the archive's UVs
/// routinely fall outside [0,1] and clamping visibly smears the hull textures.
/// </summary>
public sealed class MeshRenderer : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly BasicEffect _effect;

    public MeshRenderer(GraphicsDevice device)
    {
        _device = device;

        _effect = new BasicEffect(device)
        {
            TextureEnabled = true,
            VertexColorEnabled = false,
            LightingEnabled = true,
            PreferPerPixelLighting = true,
        };

        // A key light plus a dim fill, so hull panelling reads without a lighting rig.
        _effect.DirectionalLight0.Enabled = true;
        _effect.DirectionalLight0.Direction = Vector3.Normalize(new Vector3(-0.4f, -0.7f, -0.6f));
        _effect.DirectionalLight0.DiffuseColor = new Vector3(1.0f, 0.97f, 0.9f);
        _effect.DirectionalLight0.SpecularColor = new Vector3(0.2f);

        _effect.DirectionalLight1.Enabled = true;
        _effect.DirectionalLight1.Direction = Vector3.Normalize(new Vector3(0.6f, 0.3f, 0.5f));
        _effect.DirectionalLight1.DiffuseColor = new Vector3(0.18f, 0.20f, 0.28f);

        _effect.DirectionalLight2.Enabled = false;
        _effect.AmbientLightColor = new Vector3(0.10f, 0.11f, 0.14f);
        _effect.SpecularPower = 24f;
    }

    public void Begin(Matrix view, Matrix projection)
    {
        _effect.View = view;
        _effect.Projection = projection;

        _device.DepthStencilState = DepthStencilState.Default;
        // The archive's front faces are counter-clockwise: the signed volume of every closed body
        // sampled comes out positive, so its winding yields outward normals under a right-handed
        // cross product. XNA's default (CullCounterClockwise) assumes clockwise front faces and
        // would cull the visible side of every hull, so cull the clockwise ones instead.
        _device.RasterizerState = RasterizerState.CullClockwise;
        _device.BlendState = BlendState.Opaque;

        // The archive's UVs run outside [0,1] by design; clamping would smear the edge texels.
        _device.SamplerStates[0] = SamplerState.LinearWrap;
    }

    public void Draw(GpuMesh mesh, Matrix world)
    {
        _effect.World = world;
        _device.SetVertexBuffer(mesh.Vertices);
        _device.Indices = mesh.Indices;

        foreach (GpuSubmesh submesh in mesh.Submeshes)
        {
            if (submesh.PrimitiveCount == 0)
                continue;

            _effect.Texture = submesh.Texture;
            _effect.DiffuseColor = submesh.Diffuse;

            foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                _device.DrawIndexedPrimitives(
                    PrimitiveType.TriangleList, 0, submesh.StartIndex, submesh.PrimitiveCount);
            }
        }
    }

    public void Dispose() => _effect.Dispose();
}
