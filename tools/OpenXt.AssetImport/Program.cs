using Assimp;
using OpenXt.AssetImport;

// Offline model inspector / importer. Run it over a source mesh to see what Assimp makes of it
// before wiring a real conversion step:
//
//     dotnet run --project tools/OpenXt.AssetImport -- path/to/ship.obj
//
// This exists so the import path is a separate, non-shipping tool from day one. The game binary
// must never take a dependency on Assimp.

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: openxt-import <model-file> [more-model-files...]");
    return 1;
}

NativeLoaderShim.Install();

using AssimpContext importer = new();

const PostProcessSteps Steps =
    PostProcessSteps.Triangulate |
    PostProcessSteps.GenerateSmoothNormals |
    PostProcessSteps.CalculateTangentSpace |
    PostProcessSteps.JoinIdenticalVertices |
    PostProcessSteps.ImproveCacheLocality |
    PostProcessSteps.FlipWindingOrder;

int failures = 0;

foreach (string path in args)
{
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"{path}: not found");
        failures++;
        continue;
    }

    try
    {
        Scene scene = importer.ImportFile(path, Steps);

        int vertices = 0;
        int faces = 0;
        foreach (Mesh mesh in scene.Meshes)
        {
            vertices += mesh.VertexCount;
            faces += mesh.FaceCount;
        }

        Console.WriteLine($"{Path.GetFileName(path)}");
        Console.WriteLine($"  meshes     {scene.MeshCount}");
        Console.WriteLine($"  vertices   {vertices:N0}");
        Console.WriteLine($"  faces      {faces:N0}");
        Console.WriteLine($"  materials  {scene.MaterialCount}");
        Console.WriteLine($"  bounds     {Bounds(scene)}");
    }
    catch (AssimpException ex)
    {
        Console.Error.WriteLine($"{path}: {ex.Message}");
        failures++;
    }
}

return failures == 0 ? 0 : 1;

static string Bounds(Scene scene)
{
    Vector3D min = new(float.MaxValue);
    Vector3D max = new(float.MinValue);

    foreach (Mesh mesh in scene.Meshes)
    foreach (Vector3D v in mesh.Vertices)
    {
        min = new Vector3D(MathF.Min(min.X, v.X), MathF.Min(min.Y, v.Y), MathF.Min(min.Z, v.Z));
        max = new Vector3D(MathF.Max(max.X, v.X), MathF.Max(max.Y, v.Y), MathF.Max(max.Z, v.Z));
    }

    return scene.MeshCount == 0 ? "(none)" : $"{min.X:F2},{min.Y:F2},{min.Z:F2} .. {max.X:F2},{max.Y:F2},{max.Z:F2}";
}
