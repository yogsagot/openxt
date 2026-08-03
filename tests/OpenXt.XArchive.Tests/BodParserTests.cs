using System.Numerics;
using System.Text;
using Xunit;

namespace OpenXt.XArchive.Tests;

public sealed class BodParserTests
{
    /// <summary>
    /// Exercises every face-record shape the archives actually contain. Across both games the
    /// trailing field count is always one of: 2N+1 (flags then UVs), 2N (UVs only), 1 (flags only)
    /// or 0 (neither), for N of 3 or 4.
    /// </summary>
    private const string Sample = """
        /===============================================================
        / Test Ship
        /===============================================================
        MATERIAL3: 0;23; 51;89;89;  102;178;178;  229;229;229; 0;0;40;30; 0;0;0; 100; 0;0; 0;0;
        MATERIAL: 1;37; 10;20;30;  40;50;60;  70;80;90;
        1000; / Automatic Object Size
        0; 0; 0; /0
        100; 0; 0; /1
        100; 100; 0; /2
        0; 100; 0; /3
        -1;  -1;   -1; / marks end of coords for body
        /----- Part 0: (0 / -1 / 2)"Hull"
        0; 0; 1; 2; -25;	4; 0.0;0.0; 1.0;0.0; 1.0;1.0; /(0)Flags 0x17
        1; 0; 1; 2; -17;	16; /(1) untextured, flags only
        0; 0; 1; 2; -9; 0.0;0.0; 1.0;0.0; 1.0;1.0; /(2) UVs but no flags
        0; 0; 1; 2; -1; /(3) neither
        0; 0; 1; 2; 3; -25;	1; 0.0;0.0; 1.0;0.0; 1.0;1.0; 0.0;1.0; /(4) quad
        -99; 0000000000010001;
        -99; 0000000000000000; /Mark end of body
        """;

    [Fact]
    public void ParsesMaterialsVerticesAndFaces()
    {
        BodFile file = BodParser.Parse(Sample);

        Assert.Equal("Test Ship", file.Title);
        Assert.Equal(2, file.Materials.Count);
        Assert.Equal(23, file.Materials[0].TextureId);
        Assert.Equal(37, file.Materials[1].TextureId);

        BodBody body = Assert.Single(file.Bodies);
        Assert.Equal(1000, body.SizeHint);
        Assert.Equal(4, body.Vertices.Count);
        Assert.Equal(new Vector3(100f, 100f, 0f), body.Vertices[2]);

        BodPart part = Assert.Single(body.Parts);
        Assert.Equal("Hull", part.Name);
        Assert.Equal(5, part.Faces.Count);
    }

    [Fact]
    public void FaceVariantsCarryTheRightFlagsAndUvs()
    {
        BodPart part = BodParser.Parse(Sample).Bodies[0].Parts[0];

        // -25: flags followed by UVs.
        Assert.Equal(4, part.Faces[0].Flags);
        Assert.Equal(3, part.Faces[0].Uvs!.Length);
        Assert.Equal(new Vector2(1f, 1f), part.Faces[0].Uvs![2]);

        // -17: flags, no UVs.
        Assert.Equal(16, part.Faces[1].Flags);
        Assert.Null(part.Faces[1].Uvs);
        Assert.Equal(1, part.Faces[1].MaterialIndex);

        // -9: UVs, no flags.
        Assert.Null(part.Faces[2].Flags);
        Assert.Equal(3, part.Faces[2].Uvs!.Length);

        // -1: neither.
        Assert.Null(part.Faces[3].Flags);
        Assert.Null(part.Faces[3].Uvs);

        // Quad: four indices, four UVs, and two triangles when fanned.
        Assert.True(part.Faces[4].IsQuad);
        Assert.Equal(4, part.Faces[4].VertexIndices.Length);
        Assert.Equal(4, part.Faces[4].Uvs!.Length);
        Assert.Equal(2, part.Faces[4].Triangulate().Count());
    }

    [Fact]
    public void TriangleFansOnce()
    {
        BodFace triangle = BodParser.Parse(Sample).Bodies[0].Parts[0].Faces[0];
        (int A, int B, int C) only = Assert.Single(triangle.Triangulate());
        Assert.Equal((0, 1, 2), only);
    }

    /// <summary>
    /// A body ends at two consecutive -99 lines. An all-zero flag mask does NOT mean the end of a
    /// body — real archives contain parts whose flags are legitimately zero, and treating those as
    /// terminators silently truncates the model.
    /// </summary>
    [Fact]
    public void PartWithZeroFlagsDoesNotEndTheBody()
    {
        const string source = """
            / Two parts
            0; 0; 0;
            10; 0; 0;
            10; 10; 0;
            -1; -1; -1;
            /----- Part 0: (0 / -1 / 0)"First"
            0; 0; 1; 2; -17;	1;
            -99; 0000000000000000;
            /----- Part 1: (0 / -1 / 0)"Second"
            0; 0; 1; 2; -17;	1;
            -99; 0000000000000001;
            -99; 0000000000000000;
            """;

        BodBody body = Assert.Single(BodParser.Parse(source).Bodies);

        Assert.Equal(2, body.Parts.Count);
        Assert.Equal("First", body.Parts[0].Name);
        Assert.Equal("Second", body.Parts[1].Name);
    }

    [Fact]
    public void MultipleBodiesInOneFileAreSeparateLods()
    {
        const string source = """
            / Multi
            0; 0; 0;
            10; 0; 0;
            10; 10; 0;
            -1; -1; -1;
            /----- Part 0: (0 / -1 / 0)"A"
            0; 0; 1; 2; -17;	1;
            -99; 0000000000000001;
            -99; 0000000000000000;
            / ---- Next Body of Bodyarray! ---
            0; 0; 0;
            5; 0; 0;
            5; 5; 0;
            -1; -1; -1;
            /----- Part 0: (0 / -1 / 0)"B"
            0; 0; 1; 2; -17;	1;
            -99; 0000000000000001;
            -99; 0000000000000000;
            """;

        BodFile file = BodParser.Parse(source);

        Assert.Equal(2, file.Bodies.Count);
        Assert.All(file.Bodies, body => Assert.Equal(3, body.Vertices.Count));

        // The separator comment must not be glued onto the title.
        Assert.Equal("Multi", file.Title);
    }

    [Fact]
    public void NegativeMaterialIndexIsAccepted()
    {
        const string source = """
            / Neg
            0; 0; 0;
            1; 0; 0;
            1; 1; 0;
            -1; -1; -1;
            /----- Part 0: (0 / -1 / 0)"P"
            -6; 0; 1; 2; -25;	1; 0.0;0.0; 1.0;0.0; 1.0;1.0;
            -99; 0000000000000001;
            -99; 0000000000000000;
            """;

        BodFace face = BodParser.Parse(source).Bodies[0].Parts[0].Faces[0];

        Assert.Equal(-6, face.MaterialIndex);
        Assert.Equal([0, 1, 2], face.VertexIndices);

        // No material carries that index, so lookup must return null rather than throw.
        Assert.Null(BodParser.Parse(source).FindMaterial(-6));
    }

    [Fact]
    public void SceneFilesAreRejectedRatherThanMisparsed()
    {
        const string scene = """
            / Scene
            VER: 2;
            P 0; B 292;
              { 2;  0; 0; 0;  0.250000; 0.000000; -1.000000; 0.000000;  -1;  -1; } // 0
            """;

        Assert.Throws<InvalidDataException>(() => BodParser.Parse(scene));
    }

    [Fact]
    public void ParsesFromLatin1Bytes()
    {
        byte[] bytes = Encoding.Latin1.GetBytes(Sample);
        Assert.Equal("Test Ship", BodParser.Parse(bytes).Title);
    }
}
