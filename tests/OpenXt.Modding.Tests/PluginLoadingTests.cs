using System.Reflection;
using OpenXt.Sim;
using OpenXt.Sim.Modding;
using Xunit;

namespace OpenXt.Modding.Tests;

/// <summary>
/// The code path: a package declares an assembly, the loader loads it into its own context, finds
/// the <see cref="IPlugin"/> types and constructs them.
///
/// The assembly it loads is this test assembly, copied into the package. That is unusual but it is
/// the real path — a file on disk named by a manifest — and it means the test needs no second
/// project to build a plugin. The copy is a distinct assembly identity from the one running the
/// test, so its plugin types are distinct types too; assertions go through the registrations they
/// produce rather than through any shared static.
/// </summary>
public class PluginLoadingTests
{
    private const string Ships =
        """{ "ships": [ { "id": "ship", "name": "Ship" } ] }""";

    private const string Ruleset =
        """{ "id": "codegame", "playerShip": "ship" }""";

    private static ModHost LoadWithPluginAssembly(PackageTree tree, bool assemblies = true)
    {
        string game = tree.Package(tree.Games, "codegame", PackageTree.Manifest("codegame", ModKind.Game));
        tree.Content(game, "ships/ships.json", Ships);
        tree.Content(game, "rules/ruleset.json", Ruleset);

        string source = Assembly.GetExecutingAssembly().Location;
        string mod = tree.Package(tree.Mods, "codemod", PackageTree.Manifest(
            "codemod",
            requires: """[ { "id": "codegame" } ]""",
            extra: $"\"assembly\": \"{Path.GetFileName(source)}\""));

        File.Copy(source, Path.Combine(mod, Path.GetFileName(source)));

        return tree.Load("codegame", assemblies);
    }

    [Fact]
    public void LoadsPluginsFromTheDeclaredAssembly()
    {
        using PackageTree tree = new();
        ModHost host = LoadWithPluginAssembly(tree);

        Assert.NotEmpty(host.PluginsOf<ISimPlugin>());
    }

    [Fact]
    public void PluginRegistrationsReachTheSimulation()
    {
        using PackageTree tree = new();
        ModHost host = LoadWithPluginAssembly(tree);

        SimWorld world = SimBootstrap.Start(host);
        using Universe universe = world.Universe;

        Assert.Contains(universe.Systems.Registrations, system => system.Id == CountingSimPlugin.SystemId);

        // A plugin that throws part-way through takes its own registrations with it, and says so.
        Assert.DoesNotContain(universe.Systems.Registrations, system => system.Id == DoomedSimPlugin.SystemId);
        Assert.Contains(host.Diagnostics.Entries, entry =>
            entry.Severity == ModSeverity.Error && entry.Message.Contains("broken on purpose"));
    }

    [Fact]
    public void SafeModeLoadsContentButNoCode()
    {
        using PackageTree tree = new();
        ModHost host = LoadWithPluginAssembly(tree, assemblies: false);

        Assert.Contains(host.Packages, package => package.Id == "codemod");
        Assert.Empty(host.Plugins);
        Assert.False(host.AssembliesAllowed);

        using Universe universe = SimBootstrap.CreateUniverse(host);
        Assert.DoesNotContain(universe.Systems.Registrations, system => system.Id == CountingSimPlugin.SystemId);
    }

    [Fact]
    public void MissingAssemblyIsReportedAndTheGameStillRuns()
    {
        using PackageTree tree = new();
        string game = tree.Package(tree.Games, "codegame", PackageTree.Manifest("codegame", ModKind.Game));
        tree.Content(game, "ships/ships.json", Ships);
        tree.Content(game, "rules/ruleset.json", Ruleset);

        tree.Package(tree.Mods, "broken", PackageTree.Manifest(
            "broken", extra: "\"assembly\": \"NotThere.dll\""));

        ModHost host = tree.Load("codegame", assemblies: true);
        using Universe universe = SimBootstrap.CreateUniverse(host);

        Assert.True(host.IsLoaded);
        Assert.Contains(host.Diagnostics.Entries, entry =>
            entry.PackageId == "broken" && entry.Severity == ModSeverity.Error);
    }
}
