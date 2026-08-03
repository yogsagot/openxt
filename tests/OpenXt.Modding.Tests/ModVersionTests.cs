using Xunit;

namespace OpenXt.Modding.Tests;

public class ModVersionTests
{
    [Theory]
    [InlineData("1", 1, 0, 0)]
    [InlineData("1.2", 1, 2, 0)]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("  0.0.9 ", 0, 0, 9)]
    public void ParsesMissingComponentsAsZero(string text, int major, int minor, int patch)
    {
        Assert.True(ModVersion.TryParse(text, out ModVersion version));
        Assert.Equal(new ModVersion(major, minor, patch), version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1.2.3.4")]
    [InlineData("1.x")]
    [InlineData("-1.0")]
    [InlineData("1..2")]
    public void RejectsNonsense(string text) => Assert.False(ModVersion.TryParse(text, out _));

    [Fact]
    public void ComparesComponentWise()
    {
        Assert.True(ModVersion.Parse("1.2.0") < ModVersion.Parse("1.10.0"));
        Assert.True(ModVersion.Parse("2.0.0") > ModVersion.Parse("1.99.99"));
        Assert.True(ModVersion.Parse("1.2.3") >= ModVersion.Parse("1.2.3"));
    }

    [Fact]
    public void BareRangeIsCaret()
    {
        ModVersionRange range = ModVersionRange.Parse("1.2");

        Assert.True(range.Allows(ModVersion.Parse("1.2.0")));
        Assert.True(range.Allows(ModVersion.Parse("1.9.4")));
        Assert.False(range.Allows(ModVersion.Parse("1.1.9")));
        Assert.False(range.Allows(ModVersion.Parse("2.0.0")));
    }

    [Fact]
    public void ExplicitRangeBoundsBothEnds()
    {
        ModVersionRange range = ModVersionRange.Parse(">=1.2 <1.5");

        Assert.True(range.Allows(ModVersion.Parse("1.2.0")));
        Assert.True(range.Allows(ModVersion.Parse("1.4.9")));
        Assert.False(range.Allows(ModVersion.Parse("1.5.0")));
        Assert.False(range.Allows(ModVersion.Parse("1.1.0")));
    }

    [Fact]
    public void AbsentRangeAllowsAnything()
    {
        Assert.True(ModVersionRange.TryParse(null, out ModVersionRange range));
        Assert.True(range.Allows(ModVersion.Zero));
        Assert.True(range.Allows(ModVersion.Parse("99.0.0")));
    }
}
