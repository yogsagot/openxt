using System.Text;
using Xunit;

namespace OpenXt.Modding.Tests;

public class ModManifestTests
{
    private static ModManifest Read(string json) =>
        ModManifest.Read(new MemoryStream(Encoding.UTF8.GetBytes(json)))!;

    /// <summary>
    /// Regression guard for a trap that costs nothing to hit and everything to debug: System.Text
    /// Json's source generator builds a type with init-only properties without running its
    /// constructor, silently dropping every property initializer. With <c>init</c> instead of
    /// <c>set</c>, a manifest without a "kind" would load as <see cref="ModKind.Game"/> — the zero
    /// value — and every mod in the folder would claim to be a game.
    /// </summary>
    [Fact]
    public void OmittedFieldsKeepTheirDefaults()
    {
        ModManifest manifest = Read("""{ "id": "a.mod" }""");

        Assert.Equal(ModKind.Mod, manifest.Kind);
        Assert.Equal("data", manifest.Content);
        Assert.Equal(ModApi.Version, manifest.ApiVersion);
        Assert.Equal(ModVersion.Zero, manifest.SemanticVersion);
        Assert.Null(manifest.Assembly);
    }

    [Fact]
    public void ReadsEveryDeclaredField()
    {
        ModManifest manifest = Read("""
            {
              "id": "some.mod",
              "name": "Some Mod",
              "version": "2.1.0",
              "kind": "library",
              "apiVersion": 1,
              "assembly": "Some.dll",
              "content": "content",
              "requires": [ { "id": "xbtf", "version": ">=0.1 <2.0" }, { "id": "opt", "optional": true } ],
              "loadAfter": [ "other.mod" ]
            }
            """);

        Assert.Equal("Some Mod", manifest.DisplayName);
        Assert.Equal(new ModVersion(2, 1, 0), manifest.SemanticVersion);
        Assert.Equal(ModKind.Library, manifest.Kind);
        Assert.Equal("content", manifest.Content);
        Assert.Equal(2, manifest.Requires!.Count);
        Assert.True(manifest.Requires[1].Optional);
        Assert.Equal("other.mod", Assert.Single(manifest.LoadAfter!));
        Assert.Null(manifest.Validate());
    }

    [Theory]
    [InlineData("Upper.Case")]
    [InlineData(".leading")]
    [InlineData("has space")]
    [InlineData("slash/es")]
    public void RejectsMalformedIds(string id) => Assert.False(ModManifest.IsValidId(id));

    [Theory]
    [InlineData("openxt.sample")]
    [InlineData("xbtf")]
    [InlineData("a-b_c.2")]
    public void AcceptsWellFormedIds(string id) => Assert.True(ModManifest.IsValidId(id));

    [Fact]
    public void RejectsUnsupportedApiVersion()
    {
        ModManifest manifest = Read($$"""{ "id": "future", "apiVersion": {{ModApi.Version + 1}} }""");
        Assert.Contains("apiVersion", manifest.Validate());
    }

    [Fact]
    public void RejectsPathsThatLeaveThePackage()
    {
        Assert.Contains("assembly path", Read("""{ "id": "a", "assembly": "../evil.dll" }""").Validate());
        Assert.Contains("content path", Read("""{ "id": "a", "content": "../../etc" }""").Validate());
    }

    [Fact]
    public void RejectsUnreadableDependencyRange()
    {
        ModManifest manifest = Read("""{ "id": "a", "requires": [ { "id": "b", "version": "not-a-range" } ] }""");
        Assert.Contains("version range", manifest.Validate());
    }
}
