using System.Text;
using Xunit;

namespace OpenXt.XArchive.Tests;

public sealed class PckStreamTests
{
    [Theory]
    [InlineData(0x00)]
    [InlineData(0x5A)]
    [InlineData(0xC8)]
    [InlineData(0xFF)]
    public void RoundTripsForAnyKey(byte key)
    {
        const string content = "/ Argon M3\r\nMATERIAL3: 0;23;\r\n";
        byte[] packed = SyntheticArchive.Pck(content, key);

        Assert.True(PckStream.IsPck(packed));
        Assert.Equal(content, Encoding.Latin1.GetString(PckStream.Decode(packed)));
    }

    [Fact]
    public void RejectsContentThatIsNotPacked()
    {
        byte[] plain = "just some text, not a pck at all"u8.ToArray();

        Assert.False(PckStream.IsPck(plain));
        Assert.Throws<InvalidDataException>(() => PckStream.Decode(plain));
    }

    [Fact]
    public void IsPckHandlesShortInput()
    {
        Assert.False(PckStream.IsPck([]));
        Assert.False(PckStream.IsPck([0xC8]));
    }
}

public sealed class TextTableTests
{
    /// <summary>XBTF writes a zero-padded id and a tab.</summary>
    [Fact]
    public void ParsesTabSeparatedTable()
    {
        TextTable table = TextTable.Parse("000001\t\"Entering \"\r\n003131\t\"Argon Elite\"\r\n");

        Assert.Equal(2, table.Count);
        Assert.Equal("Entering ", table[1]);
        Assert.Equal("Argon Elite", table[3131]);
    }

    /// <summary>X-Tension writes a bare id and spaces.</summary>
    [Fact]
    public void ParsesSpaceSeparatedTable()
    {
        TextTable table = TextTable.Parse("1  \"Entering\"\r\n29  \"destroyed\"\r\n");

        Assert.Equal(2, table.Count);
        Assert.Equal("Entering", table[1]);
        Assert.Equal("destroyed", table[29]);
    }

    [Fact]
    public void UnknownIdIsNull()
    {
        TextTable table = TextTable.Parse("1\t\"one\"\r\n");

        Assert.Null(table[999]);
    }

    [Fact]
    public void SkipsLinesThatAreNotEntries()
    {
        TextTable table = TextTable.Parse("not an entry\r\n\r\n7\t\"seven\"\r\n12345\r\n");

        Assert.Equal(1, table.Count);
        Assert.Equal("seven", table[7]);
    }

    [Fact]
    public void OverlayReplacesMatchingIds()
    {
        TextTable baseTable = TextTable.Parse("1\t\"old\"\r\n2\t\"kept\"\r\n");
        baseTable.Overlay(TextTable.Parse("1\t\"new\"\r\n3\t\"added\"\r\n"));

        Assert.Equal("new", baseTable[1]);
        Assert.Equal("kept", baseTable[2]);
        Assert.Equal("added", baseTable[3]);
    }

    [Fact]
    public void ParsesHighLatin1Bytes()
    {
        byte[] bytes = Encoding.Latin1.GetBytes("3\t\"Autopilot beschädigt!\"\r\n");

        Assert.Equal("Autopilot beschädigt!", TextTable.Parse(bytes)[3]);
    }
}
