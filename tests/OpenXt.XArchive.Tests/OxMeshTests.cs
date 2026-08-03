using System.Numerics;
using OpenXt.Assets;
using Xunit;

namespace OpenXt.XArchive.Tests;

/// <summary>
/// The cache format is written by the importer and read by the game. If these two ever disagree,
/// every converted asset silently becomes garbage, so the round trip is worth pinning down.
/// </summary>
public sealed class OxMeshTests
{
    private static OxMesh Sample() => new()
    {
        BodyId = 19,
        Title = "Argon M5",
        BoundingRadius = 33.5f,
        Lods =
        [
            new OxLod
            {
                Vertices =
                [
                    new OxVertex(new Vector3(1f, 2f, 3f), Vector3.UnitY, new Vector2(0.25f, 0.75f)),
                    new OxVertex(new Vector3(4f, 5f, 6f), Vector3.UnitX, new Vector2(-1.4f, 1.002f)),
                    new OxVertex(new Vector3(7f, 8f, 9f), Vector3.UnitZ, Vector2.Zero),
                ],
                Submeshes =
                [
                    new OxSubmesh
                    {
                        MaterialIndex = 2,
                        TextureId = 23,
                        Diffuse = new Vector3(0.4f, 0.7f, 0.7f),
                        Indices = [0, 1, 2],
                    },
                ],
            },
        ],
    };

    [Fact]
    public void RoundTripsThroughAStream()
    {
        using MemoryStream stream = new();
        Sample().Write(stream);
        stream.Position = 0;

        OxMesh read = OxMesh.Read(stream);

        Assert.Equal(19, read.BodyId);
        Assert.Equal("Argon M5", read.Title);
        Assert.Equal(33.5f, read.BoundingRadius);

        OxLod lod = Assert.Single(read.Lods);
        Assert.Equal(3, lod.Vertices.Length);
        Assert.Equal(1, lod.TriangleCount);

        // UVs outside [0,1] are normal in this data and must survive verbatim.
        Assert.Equal(new Vector2(-1.4f, 1.002f), lod.Vertices[1].TexCoord);
        Assert.Equal(Vector3.UnitX, lod.Vertices[1].Normal);

        OxSubmesh submesh = Assert.Single(lod.Submeshes);
        Assert.Equal(23, submesh.TextureId);
        Assert.Equal(2, submesh.MaterialIndex);
        Assert.Equal([0, 1, 2], submesh.Indices);
    }

    [Fact]
    public void RejectsForeignData()
    {
        using MemoryStream stream = new("not an oxmesh at all, truly"u8.ToArray());

        Assert.Throws<InvalidDataException>(() => OxMesh.Read(stream));
    }

    [Fact]
    public void ManifestKnowsWhenItIsStale()
    {
        CacheManifest current = new()
        {
            Game = "xbtf",
            SourceRoot = "/somewhere",
            CatSha256 = "a",
            DatSha256 = "b",
        };

        Assert.True(current.IsCurrent);
        Assert.False((current with { ImporterVersion = 0 }).IsCurrent);
        Assert.False((current with { MeshVersion = 999 }).IsCurrent);
    }

    [Fact]
    public void CachePathsStayUnderTheConfiguredRoot()
    {
        string root = AssetCachePaths.Root;

        Assert.StartsWith(root, AssetCachePaths.ForGame("xbtf"));
        Assert.StartsWith(root, AssetCachePaths.MeshFile("xbtf", 19));
        Assert.EndsWith("00019.oxmesh", AssetCachePaths.MeshFile("xbtf", 19));
        Assert.EndsWith("023.jpg", AssetCachePaths.TextureFile("xbtf", 23));

        // The cache must never live inside the repository: EGOSOFT data cannot be committed.
        Assert.False(AssetCachePaths.Root.Contains("/development/openxt", StringComparison.Ordinal));
    }
}
