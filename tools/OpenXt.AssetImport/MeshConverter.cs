using System.Numerics;
using OpenXt.Assets;
using OpenXt.XArchive;

namespace OpenXt.AssetImport;

/// <summary>
/// Turns a parsed <c>.pbd</c> body into OpenXT's render-ready <see cref="OxMesh"/>.
///
/// Three things happen here that the archive format leaves to the reader:
///
/// * <b>Vertex splitting.</b> The archive stores one position pool plus per-corner UVs, so a
///   position shared by faces with different UVs becomes several render vertices.
/// * <b>Normals.</b> The archive carries none. We accumulate face normals per vertex and
///   normalise, which gives smooth shading across welded corners and hard edges where the UV split
///   already separated them.
/// * <b>Coordinate conversion.</b> Scale and handedness — see <see cref="DefaultMetresPerUnit"/>
///   and <see cref="ConvertPosition"/>.
/// </summary>
public sealed class MeshConverter(float metresPerUnit)
{
    /// <summary>
    /// Metres per archive unit.
    ///
    /// Anchored on the Argon M3 body, whose longest axis is 24,197 units: at 1/500 that is 48.4 m,
    /// a sensible heavy fighter, and 1/500 is the unit convention the later X games use.
    ///
    /// Two caveats worth knowing before trusting it:
    ///
    /// * It is an anchor, not a measurement. The archive offers no ground truth — the standalone
    ///   "Automatic Object Size" line looked like a candidate but does not correlate with geometry
    ///   at all (ratios from 0.05 to 2,911 across the 530 bodies), so it is a gameplay value.
    /// * Bodies are not authored at a common scale. The Teladi trading station body is *smaller*
    ///   than the M3 body, so per-object scale must come from the scene layer that instances them.
    ///   Until scenes are imported, one global factor is the best available approximation.
    ///
    /// The importer takes --scale so this can be corrected without a rebuild, and records the value
    /// actually used in the cache manifest.
    /// </summary>
    public const float DefaultMetresPerUnit = 1f / 500f;

    private readonly float _scale = metresPerUnit;

    public OxMesh Convert(int bodyId, BodFile file)
    {
        List<OxLod> lods = [];
        float radius = 0f;

        foreach (BodBody body in file.Bodies)
        {
            OxLod? lod = ConvertBody(file, body, ref radius);
            if (lod is not null)
                lods.Add(lod);
        }

        return new OxMesh
        {
            BodyId = bodyId,
            Title = file.Title,
            BoundingRadius = radius,
            Lods = lods.ToArray(),
        };
    }

    private OxLod? ConvertBody(BodFile file, BodBody body, ref float radius)
    {
        if (body.Vertices.Count == 0 || body.FaceCount == 0)
            return null;

        List<OxVertex> vertices = [];
        List<Vector3> accumulatedNormals = [];
        Dictionary<(int Position, float U, float V), int> welded = [];

        // Faces are grouped by material so the renderer binds a texture once per submesh.
        Dictionary<int, List<int>> indicesByMaterial = [];

        foreach (BodPart part in body.Parts)
        foreach (BodFace face in part.Faces)
        {
            if (!indicesByMaterial.TryGetValue(face.MaterialIndex, out List<int>? indices))
            {
                indices = [];
                indicesByMaterial[face.MaterialIndex] = indices;
            }

            foreach ((int a, int b, int c) in face.Triangulate())
            {
                int i0 = Weld(face, a);
                int i1 = Weld(face, b);
                int i2 = Weld(face, c);

                if (i0 < 0 || i1 < 0 || i2 < 0)
                    continue;

                // Winding is preserved: positions are not mirrored, so the archive's own front
                // faces stay front faces. See ConvertPosition.
                indices.Add(i0);
                indices.Add(i1);
                indices.Add(i2);

                AccumulateNormal(vertices, accumulatedNormals, i0, i1, i2);
            }
        }

        if (vertices.Count == 0)
            return null;

        OxVertex[] finalVertices = new OxVertex[vertices.Count];
        for (int i = 0; i < vertices.Count; i++)
        {
            Vector3 normal = accumulatedNormals[i];
            normal = normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : Vector3.UnitY;
            finalVertices[i] = vertices[i] with { Normal = normal };

            float distance = vertices[i].Position.Length();
            if (distance > radius)
                radius = distance;
        }

        List<OxSubmesh> submeshes = [];
        foreach ((int materialIndex, List<int> indices) in indicesByMaterial.OrderBy(p => p.Key))
        {
            if (indices.Count == 0)
                continue;

            BodMaterial? material = file.FindMaterial(materialIndex);

            submeshes.Add(new OxSubmesh
            {
                MaterialIndex = materialIndex,
                // A negative material index means "no material"; so does a material we cannot find.
                TextureId = material?.TextureId ?? -1,
                Diffuse = material?.Diffuse ?? Vector3.One,
                Indices = indices.ToArray(),
            });
        }

        return submeshes.Count == 0
            ? null
            : new OxLod { Vertices = finalVertices, Submeshes = submeshes.ToArray() };

        int Weld(BodFace face, int corner)
        {
            int positionIndex = face.VertexIndices[corner];
            if ((uint)positionIndex >= (uint)body.Vertices.Count)
                return -1;

            Vector2 uv = face.Uvs is { } uvs && corner < uvs.Length ? uvs[corner] : Vector2.Zero;

            (int, float, float) key = (positionIndex, uv.X, uv.Y);
            if (welded.TryGetValue(key, out int existing))
                return existing;

            int index = vertices.Count;
            vertices.Add(new OxVertex(ConvertPosition(body.Vertices[positionIndex]), Vector3.Zero, uv));
            accumulatedNormals.Add(Vector3.Zero);
            welded[key] = index;
            return index;
        }
    }

    /// <summary>
    /// Archive units to metres. Axes are passed through unchanged.
    ///
    /// Both the X games and OpenXT's simulation use the same left-handed frame — +X right, +Y up,
    /// +Z forward (see <c>FlightControl.Thrust</c>, where positive Z is main thrust) — so no
    /// handedness conversion belongs here. Mirroring Z would flip every model relative to the
    /// direction its ship actually flies. The renderer, not the importer, reconciles this with
    /// MonoGame's right-handed matrix helpers.
    /// </summary>
    private Vector3 ConvertPosition(Vector3 source) => source * _scale;

    private static void AccumulateNormal(
        List<OxVertex> vertices, List<Vector3> normals, int i0, int i1, int i2)
    {
        Vector3 edge1 = vertices[i1].Position - vertices[i0].Position;
        Vector3 edge2 = vertices[i2].Position - vertices[i0].Position;
        Vector3 normal = Vector3.Cross(edge1, edge2);

        // Degenerate triangles contribute nothing rather than poisoning the average with NaN.
        if (normal.LengthSquared() <= 1e-20f)
            return;

        normals[i0] += normal;
        normals[i1] += normal;
        normals[i2] += normal;
    }
}
