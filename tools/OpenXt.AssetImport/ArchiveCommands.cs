using System.Text;
using OpenXt.XArchive;

namespace OpenXt.AssetImport;

/// <summary>Read-only verbs over an installed <c>01.cat</c> / <c>01.dat</c> pair.</summary>
public static class ArchiveCommands
{
    public static int List(XInstall install, string? prefix)
    {
        using CatArchive archive = install.OpenArchive();

        long bytes = 0;
        int shown = 0;

        foreach (CatEntry entry in archive.Entries)
        {
            if (prefix is not null && !entry.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            Console.WriteLine($"{entry.Size,10:N0}  {entry.Path}");
            bytes += entry.Size;
            shown++;
        }

        Console.WriteLine();
        Console.WriteLine($"{shown:N0} entries, {bytes:N0} bytes ({install.DisplayName})");
        return 0;
    }

    /// <summary>
    /// Dumps one entry. PCK-wrapped files are decoded automatically, so <c>cat v/00000.pbd</c>
    /// prints readable model source rather than compressed noise.
    /// </summary>
    public static int Cat(XInstall install, string entryPath, bool raw)
    {
        using CatArchive archive = install.OpenArchive();

        if (!archive.TryGetEntry(entryPath, out CatEntry entry))
        {
            Console.Error.WriteLine($"'{entryPath}' is not in {Path.GetFileName(archive.CatPath)}.");
            return 1;
        }

        byte[] data = archive.Read(entry);

        if (!raw && PckStream.IsPck(data))
            data = PckStream.Decode(data);

        if (IsProbablyText(data))
            Console.Out.Write(Encoding.Latin1.GetString(data));
        else
            using (Stream stdout = Console.OpenStandardOutput())
                stdout.Write(data);

        return 0;
    }

    /// <summary>
    /// Decodes every entry and reports anything that fails. This is the regression test against a
    /// real installation: the archives are fixed data, so a failure here is always our bug.
    /// </summary>
    public static int Verify(XInstall install)
    {
        using CatArchive archive = install.OpenArchive();

        Console.WriteLine($"{install.DisplayName}  ({archive.CatPath})");
        Console.WriteLine($"  entries          {archive.Entries.Count:N0}");

        int pck = 0, bodies = 0, scenes = 0, textures = 0, texts = 0;
        int totalBodies = 0, totalParts = 0, totalVertices = 0, totalFaces = 0;
        List<string> failures = [];
        HashSet<int> referencedTextures = [];
        HashSet<int> presentTextures = [];

        foreach (CatEntry entry in archive.Entries)
        {
            try
            {
                byte[] data = archive.Read(entry);

                if (entry.Path.EndsWith(".pbd", StringComparison.OrdinalIgnoreCase))
                {
                    data = PckStream.Decode(data);
                    pck++;

                    string text = Encoding.Latin1.GetString(data);
                    if (text.Contains("VER:", StringComparison.Ordinal))
                    {
                        scenes++;
                        continue;
                    }

                    BodFile model = BodParser.Parse(text);
                    bodies++;
                    totalBodies += model.Bodies.Count;

                    foreach (BodMaterial material in model.Materials)
                        referencedTextures.Add(material.TextureId);

                    foreach (BodBody body in model.Bodies)
                    {
                        totalParts += body.Parts.Count;
                        totalVertices += body.Vertices.Count;
                        totalFaces += body.FaceCount;

                        // Every face index must address the body's own vertex pool.
                        foreach (BodPart part in body.Parts)
                        foreach (BodFace face in part.Faces)
                        foreach (int index in face.VertexIndices)
                        {
                            if ((uint)index >= (uint)body.Vertices.Count)
                            {
                                failures.Add($"{entry.Path}: vertex index {index} of {body.Vertices.Count}");
                                goto nextEntry;
                            }
                        }
                    }
                }
                else if (entry.Path.StartsWith("tex/", StringComparison.OrdinalIgnoreCase))
                {
                    textures++;
                    if (data.Length < 2 || data[0] != 0xFF || data[1] != 0xD8)
                        failures.Add($"{entry.Path}: not a JPEG");

                    string stem = Path.GetFileNameWithoutExtension(entry.Path);
                    if (entry.Path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(stem, out int id))
                    {
                        presentTextures.Add(id);
                    }
                }
                else if (entry.Path.StartsWith("t/", StringComparison.OrdinalIgnoreCase)
                         && entry.Path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    TextTable table = TextTable.Parse(data);
                    texts++;
                    if (table.Count == 0)
                        failures.Add($"{entry.Path}: no entries parsed");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{entry.Path}: {ex.Message}");
            }

            nextEntry: ;
        }

        Console.WriteLine($"  pck decoded      {pck:N0}");
        Console.WriteLine($"  body files       {bodies:N0}  ->  {totalBodies:N0} bodies, " +
                          $"{totalParts:N0} parts, {totalVertices:N0} vertices, {totalFaces:N0} faces");
        Console.WriteLine($"  scene files      {scenes:N0}  (not parsed yet)");
        Console.WriteLine($"  textures         {textures:N0}");
        Console.WriteLine($"  text tables      {texts:N0}");

        int[] missing = referencedTextures.Where(id => !presentTextures.Contains(id)).Order().ToArray();
        if (missing.Length > 0)
        {
            Console.WriteLine($"  missing textures {missing.Length} referenced but absent: " +
                              string.Join(", ", missing));
        }

        if (failures.Count == 0)
        {
            Console.WriteLine("  failures         none");
            return 0;
        }

        Console.WriteLine($"  failures         {failures.Count:N0}");
        foreach (string failure in failures.Take(25))
            Console.WriteLine($"    {failure}");
        if (failures.Count > 25)
            Console.WriteLine($"    ... and {failures.Count - 25:N0} more");

        return 1;
    }

    private static bool IsProbablyText(ReadOnlySpan<byte> data)
    {
        int limit = Math.Min(data.Length, 512);
        for (int i = 0; i < limit; i++)
        {
            if (data[i] == 0)
                return false;
        }

        return true;
    }
}
