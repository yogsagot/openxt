using Xunit;

namespace OpenXt.Modding.Tests;

public class ModResolverTests
{
    private static string[] Ids(ModHost host)
    {
        string[] ids = new string[host.Packages.Count];
        for (int i = 0; i < ids.Length; i++)
            ids[i] = host.Packages[i].Id;

        return ids;
    }

    [Fact]
    public void LoadsOnlyTheSelectedGame()
    {
        using PackageTree tree = new();
        tree.Package(tree.Games, "xbtf", PackageTree.Manifest("xbtf", ModKind.Game));
        tree.Package(tree.Games, "xtension", PackageTree.Manifest("xtension", ModKind.Game));

        ModHost host = tree.Load("xtension");

        Assert.True(host.IsLoaded);
        Assert.Equal("xtension", host.Game.Id);
        Assert.Equal(["xtension"], Ids(host));
    }

    [Fact]
    public void RefusesToGuessWhenSeveralGamesAreInstalled()
    {
        using PackageTree tree = new();
        tree.Package(tree.Games, "xbtf", PackageTree.Manifest("xbtf", ModKind.Game));
        tree.Package(tree.Games, "xtension", PackageTree.Manifest("xtension", ModKind.Game));

        ModHost host = tree.Load(gameId: null);

        Assert.False(host.IsLoaded);
        Assert.True(host.Diagnostics.HasErrors);
    }

    [Fact]
    public void LibraryLoadsOnlyWhenRequired()
    {
        using PackageTree tree = new();
        tree.Package(tree.Mods, "shared", PackageTree.Manifest("shared", ModKind.Library));
        tree.Package(tree.Games, "solo", PackageTree.Manifest("solo", ModKind.Game));

        Assert.Equal(["solo"], Ids(tree.Load("solo")));

        tree.Package(tree.Games, "solo", PackageTree.Manifest(
            "solo", ModKind.Game, requires: """[ { "id": "shared" } ]"""));

        // Dependencies load before their dependents, which is the whole point of the ordering.
        Assert.Equal(["shared", "solo"], Ids(tree.Load("solo")));
    }

    [Fact]
    public void ModTargetingAnotherGameIsSkippedWithAReason()
    {
        using PackageTree tree = new();
        tree.Package(tree.Games, "xbtf", PackageTree.Manifest("xbtf", ModKind.Game));
        tree.Package(tree.Games, "xtension", PackageTree.Manifest("xtension", ModKind.Game));
        tree.Package(tree.Mods, "for-xbtf", PackageTree.Manifest(
            "for-xbtf", requires: """[ { "id": "xbtf" } ]"""));

        ModHost host = tree.Load("xtension");

        Assert.Equal(["xtension"], Ids(host));
        Assert.Contains(host.Diagnostics.Entries, entry =>
            entry.PackageId == "for-xbtf" && entry.Message.Contains("xtension is running"));
    }

    [Fact]
    public void MissingDependencyDropsTheModAndItsDependents()
    {
        using PackageTree tree = new();
        tree.Package(tree.Games, "game", PackageTree.Manifest("game", ModKind.Game));
        tree.Package(tree.Mods, "needs-absent", PackageTree.Manifest(
            "needs-absent", requires: """[ { "id": "absent" } ]"""));
        tree.Package(tree.Mods, "needs-the-above", PackageTree.Manifest(
            "needs-the-above", requires: """[ { "id": "needs-absent" } ]"""));

        ModHost host = tree.Load("game");

        Assert.Equal(["game"], Ids(host));
        Assert.Contains(host.Diagnostics.Entries, entry => entry.PackageId == "needs-absent");
        Assert.Contains(host.Diagnostics.Entries, entry => entry.PackageId == "needs-the-above");
    }

    [Fact]
    public void VersionMismatchDropsTheMod()
    {
        using PackageTree tree = new();
        tree.Package(tree.Games, "game", PackageTree.Manifest("game", ModKind.Game, version: "2.0.0"));
        tree.Package(tree.Mods, "old", PackageTree.Manifest(
            "old", requires: """[ { "id": "game", "version": "1.0" } ]"""));

        ModHost host = tree.Load("game");

        Assert.Equal(["game"], Ids(host));
        Assert.Contains(host.Diagnostics.Entries, entry =>
            entry.PackageId == "old" && entry.Message.Contains("2.0.0 is installed"));
    }

    [Fact]
    public void CycleIsReportedAndTheRestStillLoads()
    {
        using PackageTree tree = new();
        tree.Package(tree.Games, "game", PackageTree.Manifest("game", ModKind.Game));
        tree.Package(tree.Mods, "innocent", PackageTree.Manifest("innocent"));
        tree.Package(tree.Mods, "a", PackageTree.Manifest("a", requires: """[ { "id": "b" } ]"""));
        tree.Package(tree.Mods, "b", PackageTree.Manifest("b", requires: """[ { "id": "a" } ]"""));

        ModHost host = tree.Load("game");

        Assert.Contains("game", Ids(host));
        Assert.Contains("innocent", Ids(host));
        Assert.DoesNotContain("a", Ids(host));
        Assert.DoesNotContain("b", Ids(host));
        Assert.Contains(host.Diagnostics.Entries, entry => entry.Message.Contains("circular"));
    }

    [Fact]
    public void LoadAfterOrdersWithoutDepending()
    {
        using PackageTree tree = new();
        tree.Package(tree.Games, "game", PackageTree.Manifest("game", ModKind.Game));
        tree.Package(tree.Mods, "aaa", PackageTree.Manifest("aaa", extra: "\"loadAfter\": [ \"zzz\" ]"));
        tree.Package(tree.Mods, "zzz", PackageTree.Manifest("zzz"));

        string[] ids = Ids(tree.Load("game"));

        Assert.True(Array.IndexOf(ids, "zzz") < Array.IndexOf(ids, "aaa"));
    }

    [Fact]
    public void OrderIsStableAcrossRuns()
    {
        using PackageTree tree = new();
        tree.Package(tree.Games, "game", PackageTree.Manifest("game", ModKind.Game));
        for (int i = 0; i < 8; i++)
            tree.Package(tree.Mods, $"mod{i}", PackageTree.Manifest($"mod{i}"));

        // Ordinal tie-break: nothing here depends on anything, so the order must still be the same
        // every time. A fixed-step simulation cannot afford system order to vary by machine.
        Assert.Equal(Ids(tree.Load("game")), Ids(tree.Load("game")));
    }

    [Fact]
    public void DisabledPackagesAreSkipped()
    {
        using PackageTree tree = new();
        tree.Package(tree.Games, "game", PackageTree.Manifest("game", ModKind.Game));
        tree.Package(tree.Mods, "unwanted", PackageTree.Manifest("unwanted"));

        Assert.Equal(["game"], Ids(tree.Load("game", assemblies: false, "unwanted")));
    }

    [Fact]
    public void UserRootOverridesTheBundledCopy()
    {
        using PackageTree tree = new();
        tree.Package(tree.Games, "game", PackageTree.Manifest("game", ModKind.Game));
        tree.Package(tree.Mods, "shared", PackageTree.Manifest("shared", version: "1.0.0"));
        tree.Package(tree.UserMods, "shared", PackageTree.Manifest("shared", version: "2.0.0"));

        ModHost host = tree.Load("game");

        ModPackage shared = host.Packages.Single(package => package.Id == "shared");
        Assert.Equal(new ModVersion(2, 0, 0), shared.Version);
        Assert.Equal(ModOrigin.User, shared.Origin);
    }

    [Fact]
    public void BrokenManifestDoesNotStopTheRest()
    {
        using PackageTree tree = new();
        tree.Package(tree.Games, "game", PackageTree.Manifest("game", ModKind.Game));
        tree.Package(tree.Mods, "broken", "{ not json at all ");

        ModHost host = tree.Load("game");

        Assert.True(host.IsLoaded);
        Assert.Contains(host.Diagnostics.Entries, entry => entry.Severity == ModSeverity.Error);
    }

    [Fact]
    public void FingerprintReflectsThePackageSet()
    {
        using PackageTree tree = new();
        tree.Package(tree.Games, "game", PackageTree.Manifest("game", ModKind.Game));
        string before = tree.Load("game").Fingerprint;

        tree.Package(tree.Mods, "extra", PackageTree.Manifest("extra"));
        string after = tree.Load("game").Fingerprint;

        Assert.NotEqual(before, after);
        Assert.Contains("game@1.0.0", before);
    }
}
