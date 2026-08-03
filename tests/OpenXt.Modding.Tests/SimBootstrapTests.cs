using OpenXt.Sim;
using OpenXt.Sim.Data;
using OpenXt.Sim.Modding;
using Xunit;

namespace OpenXt.Modding.Tests;

/// <summary>
/// The whole path, headless: packages on disk → merged content → a universe that steps. No window,
/// no graphics device, no asset cache — if any of that ever became necessary, these tests would be
/// the first thing to fail, which is the point of having them.
/// </summary>
public class SimBootstrapTests
{
    private const string BaseShips =
        """
        {
          "ships": [
            { "id": "fighter", "name": "Fighter", "mass": 9000, "hullRadius": 15 },
            { "id": "freighter", "name": "Freighter", "mass": 80000, "hullRadius": 50 }
          ]
        }
        """;

    private const string BaseRuleset =
        """
        {
          "id": "testgame",
          "title": "Test Game",
          "assetKey": "testassets",
          "startSector": "Test Sector",
          "playerShip": "fighter",
          "traffic": [ { "ship": "freighter", "x": 100, "z": 200, "yaw": 0.5 } ]
        }
        """;

    private static string Game(PackageTree tree, string ships = BaseShips, string ruleset = BaseRuleset)
    {
        string game = tree.Package(tree.Games, "testgame", PackageTree.Manifest("testgame", ModKind.Game));
        tree.Content(game, "ships/ships.json", ships);
        tree.Content(game, "rules/ruleset.json", ruleset);
        return game;
    }

    [Fact]
    public void StartsTheWorldTheRulesetDescribes()
    {
        using PackageTree tree = new();
        Game(tree);

        SimWorld world = SimBootstrap.Start(tree.Load("testgame"));
        using Universe universe = world.Universe;

        Assert.Equal("Test Sector", world.StartSector.Name);
        Assert.Equal("testassets", universe.Rules.AssetKey);
        Assert.Equal(2, world.StartSector.EntityCount);

        // The engine's own flight system is registered through the plugin API like anything else.
        Assert.Contains(universe.Systems.Registrations, system => system.Id == CoreSimPlugin.FlightSystemId);
    }

    [Fact]
    public void StepsWithoutAnythingGraphical()
    {
        using PackageTree tree = new();
        Game(tree);

        SimWorld world = SimBootstrap.Start(tree.Load("testgame"));
        using Universe universe = world.Universe;

        for (int i = 0; i < 120; i++)
            universe.Step(1f / 60f);

        Assert.Equal(120, universe.Tick);
    }

    [Fact]
    public void ModLayersPatchAddAndRemoveShips()
    {
        using PackageTree tree = new();
        Game(tree);

        string mod = tree.Package(tree.Mods, "tweaks", PackageTree.Manifest(
            "tweaks", requires: """[ { "id": "testgame" } ]"""));

        tree.Content(mod, "ships/ships.json",
            """
            {
              "ships": [
                { "id": "fighter", "mass": 1234 },
                { "id": "freighter", "$remove": true },
                { "id": "courier", "name": "Courier", "cruiseSpeed": 200 }
              ]
            }
            """);

        SimWorld world = SimBootstrap.Start(tree.Load("testgame"));
        using Universe universe = world.Universe;

        ShipCatalog ships = universe.Ships;

        // Patched: the mass changed, the name the base layer gave it did not.
        Assert.True(ships.TryIndexOf("fighter", out int fighter));
        Assert.Equal(1234f, ships[fighter].Mass);
        Assert.Equal("Fighter", ships[fighter].Name);

        Assert.False(ships.TryIndexOf("freighter", out _));

        // Added, and the fields it left out fall back to the definition's defaults rather than to
        // zero — which only holds because ShipDefinition avoids init-only properties.
        Assert.True(ships.TryIndexOf("courier", out int courier));
        Assert.Equal(200f, ships[courier].CruiseSpeed);
        Assert.Equal(12f, ships[courier].HullRadius);
        Assert.Equal(-1, ships[courier].XbtfBodyId);
    }

    [Fact]
    public void ModCanPatchTheRuleset()
    {
        using PackageTree tree = new();
        Game(tree);

        string mod = tree.Package(tree.Mods, "conversion", PackageTree.Manifest(
            "conversion", requires: """[ { "id": "testgame" } ]"""));
        tree.Content(mod, "rules/ruleset.json", """{ "startSector": "Somewhere Else", "playerShip": "freighter" }""");

        SimWorld world = SimBootstrap.Start(tree.Load("testgame"));
        using Universe universe = world.Universe;

        Assert.Equal("Somewhere Else", world.StartSector.Name);
        Assert.Equal("Test Game", universe.Rules.Title);
    }

    [Fact]
    public void TrafficNamingAnUnknownShipIsReportedNotThrown()
    {
        using PackageTree tree = new();
        Game(tree, ruleset:
            """
            {
              "id": "testgame",
              "playerShip": "fighter",
              "traffic": [ { "ship": "ghost" }, { "ship": "freighter" } ]
            }
            """);

        ModHost host = tree.Load("testgame");
        SimWorld world = SimBootstrap.Start(host);
        using Universe universe = world.Universe;

        Assert.Equal(2, world.StartSector.EntityCount);
        Assert.Contains(host.Diagnostics.Entries, entry => entry.Message.Contains("ghost"));
    }

    [Fact]
    public void MissingPlayerShipStopsTheRunWithAReadableMessage()
    {
        using PackageTree tree = new();
        Game(tree, ruleset: """{ "id": "testgame", "playerShip": "nonexistent" }""");

        ModContentException error = Assert.Throws<ModContentException>(
            () => SimBootstrap.Start(tree.Load("testgame")));

        Assert.Contains("nonexistent", error.Message);
    }

    [Fact]
    public void GameWithNoShipsIsRefused()
    {
        using PackageTree tree = new();
        string game = tree.Package(tree.Games, "empty", PackageTree.Manifest("empty", ModKind.Game));
        tree.Content(game, "rules/ruleset.json", """{ "id": "empty", "playerShip": "none" }""");

        Assert.Throws<ModContentException>(() => SimBootstrap.Start(tree.Load("empty")));
    }
}
