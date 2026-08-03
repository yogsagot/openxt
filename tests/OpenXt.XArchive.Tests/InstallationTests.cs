using System.Text;
using Xunit;

namespace OpenXt.XArchive.Tests;

/// <summary>
/// Runs against a real installation when one is present and skips otherwise, so CI needs no copy
/// of the games. The expected counts are properties of fixed, published data — if one of these
/// ever fails, the reader regressed, because the archives cannot change.
/// </summary>
public sealed class InstallationTests
{
    public static TheoryData<string, int, long, int, int> KnownArchives => new()
    {
        // game key, index entries (after dedup), .dat bytes, bodies, scenes
        { "xbtf", 1_562, 19_426_720, 1_124, 694 },
        { "xtension", 1_825, 30_205_985, 1_310, 741 },
    };

    [RequiresInstallTheory]
    [MemberData(nameof(KnownArchives))]
    public void DecodesEveryEntry(string gameKey, int entries, long datBytes, int bodies, int scenes)
    {
        XInstall? install = InstallProbe.ByKey(gameKey);
        if (install is null)
            return; // The other game is installed but not this one.

        using CatArchive archive = install.OpenArchive();

        Assert.Equal(entries, archive.Entries.Count);
        Assert.Equal(datBytes, new FileInfo(archive.DatPath).Length);

        int actualBodies = 0;
        int actualScenes = 0;

        foreach (CatEntry entry in archive.Entries)
        {
            if (!entry.Path.EndsWith(".pbd", StringComparison.OrdinalIgnoreCase))
                continue;

            // Every .pbd must unpack; these are fixed data, so a failure is always our bug.
            string text = Encoding.Latin1.GetString(PckStream.Decode(archive.Read(entry)));

            if (text.Contains("VER:", StringComparison.Ordinal))
            {
                actualScenes++;
                continue;
            }

            BodFile model = BodParser.Parse(text);
            actualBodies += model.Bodies.Count;

            // Face indices must address the body's own vertex pool.
            foreach (BodBody body in model.Bodies)
            foreach (BodPart part in body.Parts)
            foreach (BodFace face in part.Faces)
            foreach (int index in face.VertexIndices)
            {
                Assert.InRange(index, 0, body.Vertices.Count - 1);
            }
        }

        Assert.Equal(bodies, actualBodies);
        Assert.Equal(scenes, actualScenes);
    }

    [RequiresInstallFact]
    public void TextTablesParseFromBothGames()
    {
        foreach (XInstall install in XInstall.Discover())
        {
            using CatArchive archive = install.OpenArchive();

            foreach (CatEntry entry in archive.Entries)
            {
                if (!entry.Path.StartsWith("t/", StringComparison.OrdinalIgnoreCase)
                    || !entry.Path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                TextTable table = TextTable.Parse(archive.Read(entry));
                Assert.True(table.Count > 0, $"{install.Key}: {entry.Path} parsed to nothing");
            }
        }
    }
}
