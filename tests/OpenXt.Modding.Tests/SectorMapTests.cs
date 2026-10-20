using System.Numerics;
using DefaultEcs;
using OpenXt.Sim;
using OpenXt.Sim.Components;
using OpenXt.Sim.Data;
using OpenXt.Sim.Modding;
using Xunit;

namespace OpenXt.Modding.Tests;

/// <summary>The universe map loads like other content, checks itself, and names the start sector.</summary>
public class SectorMapTests
{
    private const string Ships = """{ "ships": [ { "id": "fighter", "name": "Fighter" } ] }""";

    private const string Stations =
        """{ "stations": [ { "id": "argon_shipyard", "name": "Argon Shipyard", "kind": "shipyard" } ] }""";

    private const string Map =
        """
        { "sectors": [
            { "id": "argon_prime", "name": "Argon Prime", "x": 2, "y": 4, "race": "argon",
              "gates": [ "east" ], "stations": [ { "station": "argon_shipyard", "x": 500, "z": -1500 } ] },
            { "id": "the_wall", "name": "The Wall", "x": 3, "y": 4, "race": "argon", "gates": [ "west" ] }
        ] }
        """;

    private static string Game(PackageTree tree, string start, string? map = Map)
    {
        string game = tree.Package(tree.Games, "testgame", PackageTree.Manifest("testgame", ModKind.Game));
        tree.Content(game, "ships/ships.json", Ships);
        tree.Content(game, "stations/stations.json", Stations);
        tree.Content(game, "rules/ruleset.json", $$"""{ "id": "testgame", "playerShip": "fighter", "startSector": "{{start}}" }""");
        if (map is not null)
            tree.Content(game, "sectors/sectors.json", map);
        return game;
    }

    private static string Mod(PackageTree tree, string sectors)
    {
        string mod = tree.Package(tree.Mods, "tweaks", PackageTree.Manifest(
            "tweaks", requires: """[ { "id": "testgame" } ]"""));
        tree.Content(mod, "sectors/sectors.json", sectors);
        return mod;
    }

    [Fact]
    public void StartsInTheSectorTheRulesetNamesById()
    {
        using PackageTree tree = new();
        Game(tree, "argon_prime");

        ModHost host = tree.Load("testgame");
        SimWorld world = SimBootstrap.Start(host);
        using Universe universe = world.Universe;

        Assert.Equal("Argon Prime", world.StartSector.Name);
        Assert.Equal(2, universe.SectorMap.Count);
        Assert.Empty(host.Diagnostics.Entries);
    }

    [Fact]
    public void TheStartSectorsStationsArePlaced()
    {
        using PackageTree tree = new();
        Game(tree, "argon_prime");

        SimWorld world = SimBootstrap.Start(tree.Load("testgame"));
        using Universe universe = world.Universe;

        Entity station = Assert.Single(world.StartSector.World.GetEntities().With<StationRef>().AsEnumerable());
        Assert.Equal("argon_shipyard", universe.Stations[station.Get<StationRef>().DefinitionIndex].Id);
        Assert.Equal(new Vector3(500f, 0f, -1500f), station.Get<Pose>().Position);

        // A station is placed, not flown: the flight model never sees it.
        Assert.False(station.Has<Motion>());

        // It still steps: stations sit in the physics world without disturbing the tick.
        for (int i = 0; i < 60; i++)
            universe.Step(1f / 60f);
        Assert.Equal(new Vector3(500f, 0f, -1500f), station.Get<Pose>().Position);
    }

    [Fact]
    public void AnUnknownStationIsNotSpawned()
    {
        using PackageTree tree = new();
        Game(tree, "argon_prime");
        Mod(tree, """{ "sectors": [ { "id": "argon_prime", "stations": [ { "station": "nonexistent_factory" } ] } ] }""");

        SimWorld world = SimBootstrap.Start(tree.Load("testgame"));
        using Universe universe = world.Universe;

        Assert.Empty(world.StartSector.World.GetEntities().With<StationRef>().AsEnumerable());
    }

    [Fact]
    public void WithoutAMapTheStartSectorIsJustAName()
    {
        using PackageTree tree = new();
        Game(tree, "Somewhere", map: null);

        ModHost host = tree.Load("testgame");
        SimWorld world = SimBootstrap.Start(host);
        using Universe universe = world.Universe;

        Assert.Equal("Somewhere", world.StartSector.Name);
        Assert.Equal(0, universe.SectorMap.Count);
        Assert.Empty(host.Diagnostics.Entries);
    }

    [Fact]
    public void AStartSectorMissingFromTheMapIsReported()
    {
        using PackageTree tree = new();
        Game(tree, "nowhere");

        ModHost host = tree.Load("testgame");
        using Universe universe = SimBootstrap.Start(host).Universe;

        Assert.Contains(host.Diagnostics.Entries, e => e.Message.Contains("nowhere"));
    }

    [Fact]
    public void OneWayGatesAndUnknownStationsAreReportedNotThrown()
    {
        using PackageTree tree = new();
        Game(tree, "argon_prime");
        Mod(tree,
            """
            { "sectors": [
                { "id": "the_wall", "gates": [] },
                { "id": "argon_prime", "stations": [ { "station": "argon_shipyard" }, { "station": "nonexistent_factory" } ] }
            ] }
            """);

        ModHost host = tree.Load("testgame");
        using Universe universe = SimBootstrap.CreateUniverse(host);

        Assert.Contains(host.Diagnostics.Entries, e =>
            e.Severity == ModSeverity.Warning && e.Message.Contains("none back"));
        Assert.Contains(host.Diagnostics.Entries, e =>
            e.Severity == ModSeverity.Warning && e.Message.Contains("nonexistent_factory"));
    }

    [Fact]
    public void AGateIntoAnEmptyCellIsReported()
    {
        using PackageTree tree = new();
        Game(tree, "argon_prime");
        Mod(tree, """{ "sectors": [ { "id": "the_wall", "gates": [ "west", "north" ] } ] }""");

        ModHost host = tree.Load("testgame");
        using Universe universe = SimBootstrap.CreateUniverse(host);

        Assert.Contains(host.Diagnostics.Entries, e => e.Message.Contains("empty cell"));
    }
}
