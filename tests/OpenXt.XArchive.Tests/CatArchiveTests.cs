using System.Text;
using Xunit;

namespace OpenXt.XArchive.Tests;

public sealed class CatArchiveTests
{
    [Fact]
    public void ReadsEntriesAndContent()
    {
        using SyntheticArchive archive = SyntheticArchive.Create(
            ("t/44001.txt", "1\t\"Hello\""u8.ToArray()),
            ("tex/true/023.jpg", [0xFF, 0xD8, 0xFF, 0xE0]));

        using CatArchive cat = archive.Open();

        Assert.Equal(2, cat.Entries.Count);
        Assert.Equal("t/44001.txt", cat.Entries[0].Path);
        Assert.Equal(0, cat.Entries[0].Offset);
        Assert.Equal(9, cat.Entries[0].Size);
        Assert.Equal(9, cat.Entries[1].Offset);

        Assert.Equal("1\t\"Hello\"", Encoding.Latin1.GetString(cat.Read("t/44001.txt")));
        Assert.Equal([0xFF, 0xD8, 0xFF, 0xE0], cat.Read("tex/true/023.jpg"));
    }

    [Fact]
    public void PathLookupIgnoresCase()
    {
        using SyntheticArchive archive = SyntheticArchive.Create(("f/SMALL.SIZ", [1, 2, 3]));
        using CatArchive cat = archive.Open();

        Assert.True(cat.Contains("f/small.siz"));
        Assert.Equal([1, 2, 3], cat.Read("f/Small.Siz"));
    }

    [Fact]
    public void PathsMayContainSpaces()
    {
        using SyntheticArchive archive = SyntheticArchive.Create(("some dir/a file.txt", [7, 7]));
        using CatArchive cat = archive.Open();

        Assert.Equal("some dir/a file.txt", cat.Entries[0].Path);
        Assert.Equal(2, cat.Entries[0].Size);
    }

    /// <summary>
    /// X-Tension's index is an append log: a later line replaces an earlier one of the same name.
    /// The superseded entry still occupies its bytes, so offsets must keep counting it — getting
    /// this wrong shifts every subsequent entry and corrupts the whole archive.
    /// </summary>
    [Fact]
    public void DuplicateNamesResolveLastWinsWithoutDisturbingOffsets()
    {
        using SyntheticArchive archive = SyntheticArchive.Create(
            ("l/001.xxx", "old-version"u8.ToArray()),
            ("v/00000.pbd", "middle"u8.ToArray()),
            ("l/001.xxx", "new"u8.ToArray()));

        using CatArchive cat = archive.Open();

        Assert.Equal(3, cat.AllEntries.Count);
        Assert.Equal(2, cat.Entries.Count);

        // Last write wins for content...
        Assert.Equal("new", Encoding.Latin1.GetString(cat.Read("l/001.xxx")));

        // ...but the entry that it replaced still consumed its 11 bytes.
        Assert.Equal(11, cat.AllEntries[1].Offset);
        Assert.Equal("middle", Encoding.Latin1.GetString(cat.Read("v/00000.pbd")));
    }

    /// <summary>
    /// Entry sizes must tile the .dat exactly. This is the only integrity check the format allows,
    /// and it is what catches a mis-parsed index before anything downstream reads garbage.
    /// </summary>
    [Fact]
    public void RejectsArchiveWhoseSizesDoNotMatchTheBlob()
    {
        using SyntheticArchive archive = SyntheticArchive.Create(("a.txt", [1, 2, 3, 4]));

        string datPath = Path.ChangeExtension(archive.CatPath, ".dat");
        File.WriteAllBytes(datPath, [0, 0]);

        InvalidDataException error = Assert.Throws<InvalidDataException>(archive.Open);
        Assert.Contains("did not decode correctly", error.Message);
    }

    [Fact]
    public void MissingEntryThrows()
    {
        using SyntheticArchive archive = SyntheticArchive.Create(("a.txt", [1]));
        using CatArchive cat = archive.Open();

        Assert.Throws<FileNotFoundException>(() => cat.Read("nope.txt"));
    }
}
