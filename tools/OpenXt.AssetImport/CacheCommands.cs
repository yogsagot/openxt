using System.Numerics;
using OpenXt.Assets;

namespace OpenXt.AssetImport;

/// <summary>
/// Inspection of the converted cache. This is how the scale and handedness constants in
/// <see cref="MeshConverter"/> get calibrated: convert, measure, compare against a known
/// real-world size, adjust --scale.
/// </summary>
public static class CacheCommands
{
    public static int MeshInfo(string gameKey, IReadOnlyList<string> bodyIds)
    {
        string manifestPath = AssetCachePaths.Manifest(gameKey);
        CacheManifest? manifest = CacheManifest.ReadFile(manifestPath);

        if (manifest is null)
        {
            Console.Error.WriteLine($"No cache for '{gameKey}'. Run 'openxt-import import' first.");
            return 1;
        }

        Console.WriteLine($"cache {AssetCachePaths.ForGame(gameKey)}  " +
                          $"({manifest.MeshCount:N0} meshes, {manifest.MetresPerUnit:0.######} m/unit)");
        Console.WriteLine();

        // No ids: list everything, which is how you find the body backing a given ship.
        if (bodyIds.Count == 0)
            return ListAll(gameKey);

        foreach (string raw in bodyIds)
        {
            if (!int.TryParse(raw, out int bodyId))
            {
                Console.Error.WriteLine($"'{raw}' is not a body id");
                continue;
            }

            string path = AssetCachePaths.MeshFile(gameKey, bodyId);
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"body {bodyId}: not in the cache");
                continue;
            }

            OxMesh mesh = OxMesh.ReadFile(path);

            Console.WriteLine($"body {bodyId}  {mesh.Title ?? "(untitled)"}");
            Console.WriteLine($"  bounding radius {mesh.BoundingRadius:F2} m");

            for (int i = 0; i < mesh.Lods.Length; i++)
            {
                OxLod lod = mesh.Lods[i];
                (Vector3 min, Vector3 max) = Bounds(lod);
                Vector3 size = max - min;

                Console.WriteLine(
                    $"  lod {i}: {lod.Vertices.Length,6:N0} verts  {lod.TriangleCount,6:N0} tris  " +
                    $"{lod.Submeshes.Length,3} submeshes  " +
                    $"size {size.X:F1} x {size.Y:F1} x {size.Z:F1} m");

                if (i == 0)
                {
                    IEnumerable<string> textures = lod.Submeshes
                        .Select(s => s.TextureId)
                        .Distinct()
                        .Order()
                        .Select(id => id < 0 ? "none" : id.ToString());

                    Console.WriteLine($"         textures: {string.Join(", ", textures)}");
                }
            }

            Console.WriteLine();
        }

        return 0;
    }

    private static int ListAll(string gameKey)
    {
        string directory = AssetCachePaths.Meshes(gameKey);
        string[] files = Directory.GetFiles(directory, "*.oxmesh");
        Array.Sort(files, StringComparer.Ordinal);

        Console.WriteLine($"{"id",5}  {"lods",4} {"verts",7} {"tris",7}  {"longest",8}  title");

        foreach (string file in files)
        {
            OxMesh mesh = OxMesh.ReadFile(file);
            OxLod? best = mesh.Lods.FirstOrDefault();
            if (best is null)
                continue;

            (Vector3 min, Vector3 max) = Bounds(best);
            Vector3 size = max - min;
            float longest = MathF.Max(size.X, MathF.Max(size.Y, size.Z));

            Console.WriteLine(
                $"{mesh.BodyId,5}  {mesh.Lods.Length,4} {best.Vertices.Length,7:N0} " +
                $"{best.TriangleCount,7:N0}  {longest,7:F1}m  {mesh.Title}");
        }

        Console.WriteLine();
        Console.WriteLine($"{files.Length:N0} meshes");
        return 0;
    }

    private static (Vector3 Min, Vector3 Max) Bounds(OxLod lod)
    {
        Vector3 min = new(float.MaxValue);
        Vector3 max = new(float.MinValue);

        foreach (OxVertex vertex in lod.Vertices)
        {
            min = Vector3.Min(min, vertex.Position);
            max = Vector3.Max(max, vertex.Position);
        }

        return lod.Vertices.Length == 0 ? (Vector3.Zero, Vector3.Zero) : (min, max);
    }
}
