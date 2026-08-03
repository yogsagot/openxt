using Xunit;

namespace OpenXt.Modding.Tests;

public class ModContentTests
{
    private static ModHost LoadWithLayers(PackageTree tree)
    {
        string game = tree.Package(tree.Games, "game", PackageTree.Manifest("game", ModKind.Game));
        string mod = tree.Package(tree.Mods, "later", PackageTree.Manifest(
            "later", extra: "\"loadAfter\": [ \"game\" ]"));

        tree.Content(game, "text/greeting.txt", "from the game");
        tree.Content(game, "text/only-game.txt", "only the game has this");
        tree.Content(mod, "text/greeting.txt", "from the mod");

        return tree.Load("game");
    }

    [Fact]
    public void FindReturnsTheLastLayer()
    {
        using PackageTree tree = new();
        ModContent content = LoadWithLayers(tree).Content;

        Assert.Equal("from the mod", File.ReadAllText(content.Find("text/greeting.txt")!));
        Assert.Equal("only the game has this", File.ReadAllText(content.Find("text/only-game.txt")!));
        Assert.Null(content.Find("text/absent.txt"));
    }

    [Fact]
    public void LayersComeBackInLoadOrder()
    {
        using PackageTree tree = new();
        ModContent content = LoadWithLayers(tree).Content;

        IReadOnlyList<string> layers = content.Layers("text/greeting.txt");

        Assert.Equal(2, layers.Count);
        Assert.Equal("game", content.Owner(layers[0])!.Id);
        Assert.Equal("later", content.Owner(layers[1])!.Id);
    }

    [Fact]
    public void EnumerateUnionsLayersAndOverridesByPath()
    {
        using PackageTree tree = new();
        IReadOnlyList<string> files = LoadWithLayers(tree).Content.Enumerate("text", "*.txt");

        Assert.Equal(2, files.Count);
        Assert.Contains(files, file => file.EndsWith("only-game.txt", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("text/../../outside.txt")]
    [InlineData("/etc/passwd")]
    public void PathsThatLeaveThePackageAreRejected(string path)
    {
        using PackageTree tree = new();
        ModContent content = LoadWithLayers(tree).Content;

        Assert.Throws<ArgumentException>(() => content.Find(path));
    }
}
