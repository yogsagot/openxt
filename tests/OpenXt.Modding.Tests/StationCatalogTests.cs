using OpenXt.Sim;
using OpenXt.Sim.Data;
using OpenXt.Sim.Modding;
using Xunit;

namespace OpenXt.Modding.Tests;

/// <summary>Stations and wares load through the same layered content as ships, and are optional.</summary>
public class StationCatalogTests
{
    private const string Ships = """{ "ships": [ { "id": "fighter", "name": "Fighter" } ] }""";
    private const string Ruleset = """{ "id": "testgame", "playerShip": "fighter" }""";

    private const string Wares =
        """
        { "wares": [
            { "id": "energy_cells", "name": "Energy Cells", "priceMin": 6, "priceMax": 26 },
            { "id": "ore", "name": "Ore" }
        ] }
        """;

    private const string Stations =
        """
        { "stations": [
            { "id": "argon_ore_mine", "name": "Argon Ore Mine", "race": "argon", "kind": "mine",
              "product": "ore", "inputs": [ { "ware": "energy_cells", "amount": 6 } ] }
        ] }
        """;

    private static string Game(PackageTree tree, bool content = true)
    {
        string game = tree.Package(tree.Games, "testgame", PackageTree.Manifest("testgame", ModKind.Game));
        tree.Content(game, "ships/ships.json", Ships);
        tree.Content(game, "rules/ruleset.json", Ruleset);
        if (content)
        {
            tree.Content(game, "wares/wares.json", Wares);
            tree.Content(game, "stations/stations.json", Stations);
        }

        return game;
    }

    [Fact]
    public void LoadsStationsAndWares()
    {
        using PackageTree tree = new();
        Game(tree);

        ModHost host = tree.Load("testgame");
        using Universe universe = SimBootstrap.CreateUniverse(host);

        StationDefinition mine = universe.Stations[universe.Stations.IndexOf("argon_ore_mine")];
        Assert.Equal("mine", mine.Kind);
        Assert.Equal("ore", mine.Product);
        Assert.Equal(6, Assert.Single(mine.Inputs!).Amount);

        // Omitted fields keep their defaults, not zero.
        Assert.Equal(-1, mine.XbtfSceneId);

        Assert.Equal(26, universe.Wares[universe.Wares.IndexOf("energy_cells")].PriceMax);
        Assert.Empty(host.Diagnostics.Entries);
    }

    [Fact]
    public void AGameWithoutThemGetsEmptyCatalogs()
    {
        using PackageTree tree = new();
        Game(tree, content: false);

        using Universe universe = SimBootstrap.CreateUniverse(tree.Load("testgame"));

        Assert.Equal(0, universe.Stations.Count);
        Assert.Equal(0, universe.Wares.Count);
    }

    [Fact]
    public void ModLayersMergeStationsById()
    {
        using PackageTree tree = new();
        Game(tree);

        string mod = tree.Package(tree.Mods, "tweaks", PackageTree.Manifest(
            "tweaks", requires: """[ { "id": "testgame" } ]"""));
        tree.Content(mod, "stations/stations.json",
            """{ "stations": [ { "id": "argon_ore_mine", "price": 141361 } ] }""");

        using Universe universe = SimBootstrap.CreateUniverse(tree.Load("testgame"));

        StationDefinition mine = universe.Stations[universe.Stations.IndexOf("argon_ore_mine")];
        Assert.Equal(141361, mine.Price);
        Assert.Equal("Argon Ore Mine", mine.Name);
    }

    [Fact]
    public void AnUnknownWareIsReportedNotThrown()
    {
        using PackageTree tree = new();
        Game(tree);

        string mod = tree.Package(tree.Mods, "broken", PackageTree.Manifest(
            "broken", requires: """[ { "id": "testgame" } ]"""));
        tree.Content(mod, "stations/stations.json",
            """{ "stations": [ { "id": "argon_ore_mine", "inputs": [ { "ware": "unobtainium", "amount": 1 } ] } ] }""");

        ModHost host = tree.Load("testgame");
        using Universe universe = SimBootstrap.CreateUniverse(host);

        Assert.Contains(host.Diagnostics.Entries, entry =>
            entry.Severity == ModSeverity.Warning && entry.Message.Contains("unobtainium"));
    }
}
