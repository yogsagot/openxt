using System.Numerics;
using OpenXt.AssetImport;
using Xunit;

namespace OpenXt.XArchive.Tests;

public sealed class SceneTests
{
    private const string Sample = """
        /===============================================================
        / 3D Scene Information from 3ds file: f:\src\stl1\data\meshes\04031.3ds
        /===============================================================
        VER: 2;
        P 0; B -1;
          { 8;  -82500; 100300; -130600;  -500; 0; 800;  0.000000;  -1;  -1; } // 0
        P 8; B 184;
          { 2;  0; 0; 0;  0.000000; 0.000000; 0.000000; 0.000000;  -1;  -1; } // 0
        P 13; B 79;
          { 2;  0; 40400; 100;  0.250000; 1.000000; -0.000000; -0.000000;  -1;  -1; } // 0
        P 18; B 78;
          { 2;  -100; -34600; -300;  0.000000; 1.000000; 0.000000; 0.000000;  10000;  1; } // 0
          { 18;  0.499750; 0.000000; -1.000000; 0.000000;  10000;  2; } // 1
        P 20; B 72;
          { 34;  0; 12000; -14000;  -1;  -1; } // 0
        """;

    [Fact]
    public void ParsesPartsBodiesAndKeys()
    {
        Assert.True(SceneParser.IsScene(Sample));
        BodScene scene = SceneParser.Parse(Sample);

        Assert.Equal([-1, 184, 79, 78, 72], scene.Parts.Select(p => p.BodyId));
        Assert.Contains("04031.3ds", scene.Title);

        ScenePart gate = scene.Parts[2];
        Assert.Equal(13, gate.Index);
        (Vector3 position, float turns, Vector3 axis) = gate.RestPose;
        Assert.Equal(new Vector3(0, 40400, 100), position);
        Assert.Equal(0.25f, turns);
        Assert.Equal(Vector3.UnitX, axis);

        // A spinning part keeps its rotation keys; its rest pose is the first key.
        Assert.Equal([2, 18], scene.Parts[3].Keys.Select(k => k.Type));

        // A position-only key rests unrotated.
        Assert.Equal((new Vector3(0, 12000, -14000), 0f, Vector3.UnitY), scene.Parts[4].RestPose);
    }

    [Fact]
    public void ConvertsToMetresQuaternionsAndBodyScale()
    {
        Dictionary<int, BodyInfo> bodies = new()
        {
            [184] = new BodyInfo(LodCount: 5, Vertices: 510, BoundingRadius: 27.4f),
            [79] = new BodyInfo(LodCount: 4, Vertices: 507, BoundingRadius: 33.9f),
            [78] = new BodyInfo(LodCount: 2, Vertices: 4, BoundingRadius: 2.8f),
        };

        var scene = new SceneConverter(1f / 500f, bodyScale: 4f).Convert(4031, SceneParser.Parse(Sample), bodies);

        // The camera and the body the archive lacks (72) are dropped.
        Assert.Equal([184, 79, 78], scene.Parts.Select(p => p.BodyId));

        var gate = scene.Parts[1];
        Assert.Equal(80.8f, gate.Y, 3);
        Assert.Equal(4f, gate.Scale);

        // A quarter turn about +X.
        Quaternion expected = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2f);
        Assert.Equal(expected.X, gate.QX, 5);
        Assert.Equal(expected.W, gate.QW, 5);

        // The four-vertex light is an effect; structure is not.
        Assert.False(scene.Parts[0].Effect);
        Assert.True(scene.Parts[2].Effect);

        // Radius covers the structure at its drawn size: the gate's centre plus its scaled body.
        Assert.Equal(new Vector3(0, 80.8f, 0.2f).Length() + 33.9f * 4f, scene.BoundingRadius, 2);
    }

    [Fact]
    public void ZeroTurnsIsIdentityWhateverTheAxis()
    {
        Assert.Equal(Quaternion.Identity, SceneConverter.Rotation(0f, Vector3.Zero));
        Assert.Equal(Quaternion.Identity, SceneConverter.Rotation(0f, Vector3.UnitY));
    }
}
