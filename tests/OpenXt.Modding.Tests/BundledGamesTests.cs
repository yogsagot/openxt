using DefaultEcs;
using OpenXt.Sim;
using OpenXt.Sim.Components;
using OpenXt.Sim.Modding;
using Xunit;

namespace OpenXt.Modding.Tests;

/// <summary>
/// The repository's own game packages, booted headless. Their content is plain data in the working
/// tree — no EGOSOFT install is involved — so a roster that stops loading, or starts producing
/// diagnostics, fails here rather than on someone's screen.
/// </summary>
public class BundledGamesTests
{
    private static string GamesRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "openxt.slnx")))
                return Path.Combine(dir.FullName, "games");
        }

        throw new DirectoryNotFoundException("repository root (openxt.slnx) not found above the test binaries");
    }

    [Theory]
    [InlineData("xbtf", "Argon Prime", 8)]
    [InlineData("xtension", "Home of Light", 9)]
    public void BootsCleanWithItsStations(string gameId, string startSector, int stations)
    {
        ModHost host = ModHost.Load(new ModHostOptions
        {
            SearchRoots = [(GamesRoot(), ModOrigin.Bundled)],
            GameId = gameId,
            Disabled = new HashSet<string>(StringComparer.Ordinal),
            LoadAssemblies = false,
        });

        SimWorld world = SimBootstrap.Start(host);
        using Universe universe = world.Universe;

        Assert.Empty(host.Diagnostics.Entries);
        Assert.Equal(startSector, world.StartSector.Name);
        Assert.Equal(stations, world.StartSector.World.GetEntities().With<StationRef>().AsEnumerable().Count());

        // And it steps with the stations in it.
        for (int i = 0; i < 60; i++)
            universe.Step(1f / 60f);
    }
}
