using System.Numerics;
using OpenXt.Assets;
using OpenXt.AssetImport;
using Xunit;

namespace OpenXt.XArchive.Tests;

/// <summary>
/// Both games author bodies nose at +Z, OpenXT's forward, and the two archives store shared bodies
/// identically. A converter that mirrors Z draws every ship backwards, so pin the pass-through.
/// </summary>
public sealed class MeshConverterTests
{
    /// <summary>One triangle leaning towards +Z, so a Z mirror would visibly change it.</summary>
    private const string Triangle = """
        / Wedge
        MATERIAL: 0;1; 10;20;30;  40;50;60;  70;80;90;
        1000; / Automatic Object Size
        0; 0; 0; /0
        500; 0; 500; /1
        0; 500; 500; /2
        -1;  -1;   -1;
        /----- Part 0: (0 / -1 / 2)"Hull"
        0; 0; 1; 2; -1;
        -99; 0000000000000000;
        """;

    [Fact]
    public void KeepsTheArchiveAxesAndWinding()
    {
        OxLod lod = new MeshConverter(1f / 500f).Convert(0, BodParser.Parse(Triangle)).Lods[0];
        OxVertex[] v = lod.Vertices;
        int[] i = lod.Submeshes[0].Indices;

        Assert.Equal(new Vector3(1f, 0f, 1f), v[1].Position);
        Assert.Equal(new Vector3(0f, 1f, 1f), v[2].Position);

        // The normal follows the archive's own winding.
        Vector3 face = Vector3.Cross(v[i[1]].Position - v[i[0]].Position, v[i[2]].Position - v[i[0]].Position);
        Assert.True(Vector3.Dot(face, v[0].Normal) > 0f);
    }
}
