using System.Numerics;
using System.Text;

namespace OpenXt.Assets;

/// <summary>A render-ready vertex. Position and normal are in metres; UVs may fall outside [0,1].</summary>
public readonly record struct OxVertex(Vector3 Position, Vector3 Normal, Vector2 TexCoord);

/// <summary>
/// A run of triangles sharing one material, so the renderer can bind a texture once per group.
/// </summary>
public sealed class OxSubmesh
{
    public int MaterialIndex { get; init; }

    /// <summary>Texture number, resolving to a file in the cache. -1 when the face is untextured.</summary>
    public int TextureId { get; init; }

    public Vector3 Diffuse { get; init; } = Vector3.One;

    public required int[] Indices { get; init; }
}

/// <summary>One level of detail: a vertex pool plus the submeshes indexing into it.</summary>
public sealed class OxLod
{
    public required OxVertex[] Vertices { get; init; }
    public required OxSubmesh[] Submeshes { get; init; }

    public int TriangleCount
    {
        get
        {
            int n = 0;
            foreach (OxSubmesh submesh in Submeshes)
                n += submesh.Indices.Length / 3;
            return n;
        }
    }
}

/// <summary>
/// OpenXT's converted mesh. Written by the offline importer, read by the game.
///
/// The format is ours and intentionally dull: a header, then each LOD's vertices and submeshes,
/// little-endian, no compression. It exists so the runtime never parses EGOSOFT's text model
/// format or decompresses anything on the loading path.
/// </summary>
public sealed class OxMesh
{
    private static readonly byte[] MagicBytes = "OXM1"u8.ToArray();

    /// <summary>Bump when the layout changes; the manifest records it so stale caches are caught.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Body number in the source archive, which is also the cache filename.</summary>
    public int BodyId { get; init; }

    /// <summary>The model's comment line, e.g. "Argon M3". Purely informational.</summary>
    public string? Title { get; init; }

    /// <summary>Highest detail first.</summary>
    public required OxLod[] Lods { get; init; }

    /// <summary>Radius of the bounding sphere about the origin, metres. Useful for culling.</summary>
    public float BoundingRadius { get; init; }

    public void Write(Stream stream)
    {
        using BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write(MagicBytes);
        writer.Write(CurrentVersion);
        writer.Write(BodyId);
        writer.Write(Title ?? string.Empty);
        writer.Write(BoundingRadius);
        writer.Write(Lods.Length);

        foreach (OxLod lod in Lods)
        {
            writer.Write(lod.Vertices.Length);
            foreach (OxVertex vertex in lod.Vertices)
            {
                writer.Write(vertex.Position.X);
                writer.Write(vertex.Position.Y);
                writer.Write(vertex.Position.Z);
                writer.Write(vertex.Normal.X);
                writer.Write(vertex.Normal.Y);
                writer.Write(vertex.Normal.Z);
                writer.Write(vertex.TexCoord.X);
                writer.Write(vertex.TexCoord.Y);
            }

            writer.Write(lod.Submeshes.Length);
            foreach (OxSubmesh submesh in lod.Submeshes)
            {
                writer.Write(submesh.MaterialIndex);
                writer.Write(submesh.TextureId);
                writer.Write(submesh.Diffuse.X);
                writer.Write(submesh.Diffuse.Y);
                writer.Write(submesh.Diffuse.Z);
                writer.Write(submesh.Indices.Length);
                foreach (int index in submesh.Indices)
                    writer.Write(index);
            }
        }
    }

    public static OxMesh Read(Stream stream)
    {
        using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: true);

        Span<byte> magic = stackalloc byte[4];
        reader.Read(magic);
        if (!magic.SequenceEqual(MagicBytes))
            throw new InvalidDataException("Not an .oxmesh file.");

        int version = reader.ReadInt32();
        if (version != CurrentVersion)
        {
            throw new InvalidDataException(
                $".oxmesh version {version}, expected {CurrentVersion}. Re-run 'openxt-import import'.");
        }

        int bodyId = reader.ReadInt32();
        string title = reader.ReadString();
        float radius = reader.ReadSingle();
        int lodCount = reader.ReadInt32();

        OxLod[] lods = new OxLod[lodCount];
        for (int l = 0; l < lodCount; l++)
        {
            int vertexCount = reader.ReadInt32();
            OxVertex[] vertices = new OxVertex[vertexCount];
            for (int v = 0; v < vertexCount; v++)
            {
                vertices[v] = new OxVertex(
                    new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                    new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                    new Vector2(reader.ReadSingle(), reader.ReadSingle()));
            }

            int submeshCount = reader.ReadInt32();
            OxSubmesh[] submeshes = new OxSubmesh[submeshCount];
            for (int s = 0; s < submeshCount; s++)
            {
                int materialIndex = reader.ReadInt32();
                int textureId = reader.ReadInt32();
                Vector3 diffuse = new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                int indexCount = reader.ReadInt32();
                int[] indices = new int[indexCount];
                for (int i = 0; i < indexCount; i++)
                    indices[i] = reader.ReadInt32();

                submeshes[s] = new OxSubmesh
                {
                    MaterialIndex = materialIndex,
                    TextureId = textureId,
                    Diffuse = diffuse,
                    Indices = indices,
                };
            }

            lods[l] = new OxLod { Vertices = vertices, Submeshes = submeshes };
        }

        return new OxMesh
        {
            BodyId = bodyId,
            Title = string.IsNullOrEmpty(title) ? null : title,
            BoundingRadius = radius,
            Lods = lods,
        };
    }

    public static OxMesh ReadFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Read(stream);
    }

    public void WriteFile(string path)
    {
        using FileStream stream = File.Create(path);
        Write(stream);
    }
}
