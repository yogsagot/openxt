using System.Numerics;
using DefaultEcs;
using OpenXt.Modding;
using OpenXt.Sim.Components;
using OpenXt.Sim.Data;
using OpenXt.Sim.Systems;

namespace OpenXt.Sim.Modding;

/// <summary>A started world: the universe, the sector the player is in, and the player's ship.</summary>
public sealed record SimWorld(Universe Universe, Sector StartSector, Entity Player);

/// <summary>
/// Turns loaded packages into a running simulation: merge the content, let the plugins register,
/// freeze the system order, build the world the ruleset describes.
///
/// Headless by construction — nothing here touches a graphics device, so a console host or a test
/// can start the same world the game does.
/// </summary>
public static class SimBootstrap
{
    /// <summary>
    /// Loads content and plugins into a universe with no sectors yet.
    ///
    /// Throws <see cref="ModContentException"/> only when the packages cannot describe a world at
    /// all (no ships, no ruleset, unreadable JSON). A single misbehaving plugin is reported through
    /// <paramref name="host"/>'s diagnostics and skipped.
    /// </summary>
    public static Universe CreateUniverse(ModHost host)
    {
        ShipCatalog ships = ContentCatalogs.LoadShips(host.Content);
        GameRuleset rules = ContentCatalogs.LoadRuleset(host.Content);

        SimRegistry registry = new(ships, rules);

        // The engine's own systems first, so a mod's `order` is relative to a pipeline that already
        // contains the flight model.
        new CoreSimPlugin().ConfigureSim(registry);

        foreach (ISimPlugin plugin in host.PluginsOf<ISimPlugin>())
        {
            int checkpoint = registry.Checkpoint;

            try
            {
                plugin.ConfigureSim(registry);
            }
            catch (Exception ex)
            {
                // Third-party code: anything it throws is in scope, and none of it is worth the run.
                registry.RollbackTo(checkpoint);
                host.Diagnostics.Error(
                    PackageOf(host, plugin),
                    $"'{plugin.GetType().FullName}' failed while configuring the simulation " +
                    $"and was skipped: {ex.Message}");
            }
        }

        return new Universe(ships, rules, registry.Build());
    }

    /// <summary>
    /// Builds the start sector the ruleset describes: the player's ship, then whatever else it
    /// places. A traffic entry naming a ship nobody defines is a data mistake in some package — it
    /// is reported and skipped, not thrown.
    /// </summary>
    public static SimWorld Start(ModHost host)
    {
        Universe universe = CreateUniverse(host);
        GameRuleset rules = universe.Rules;

        Sector sector = universe.CreateSector(rules.StartSector);

        if (!universe.Ships.TryIndexOf(rules.PlayerShip, out _))
            throw new ModContentException(
                $"The ruleset starts the player in '{rules.PlayerShip}', which no package defines.");

        Entity player = sector.SpawnShip(
            rules.PlayerShip,
            new Vector3(rules.StartX, rules.StartY, rules.StartZ),
            Quaternion.Identity);
        player.Set<PlayerControlled>();

        foreach (SpawnPoint spawn in rules.Traffic ?? [])
        {
            if (!universe.Ships.TryIndexOf(spawn.Ship, out _))
            {
                host.Diagnostics.Warning(
                    host.IsLoaded ? host.Game.Id : "(engine)",
                    $"the start sector places '{spawn.Ship}', which no package defines. Skipped.");
                continue;
            }

            sector.SpawnShip(
                spawn.Ship,
                new Vector3(spawn.X, spawn.Y, spawn.Z),
                Quaternion.CreateFromYawPitchRoll(spawn.Yaw, spawn.Pitch, spawn.Roll));
        }

        return new SimWorld(universe, sector, player);
    }

    /// <summary>
    /// Which package a plugin came from, for a diagnostic that names the mod rather than a type.
    /// Assembly location is the only link back, and it can be empty for a single-file build.
    /// </summary>
    private static string PackageOf(ModHost host, ISimPlugin plugin)
    {
        string? location = plugin.GetType().Assembly.Location;
        if (string.IsNullOrEmpty(location))
            return "(unknown package)";

        string full = Path.GetFullPath(location);

        foreach (ModPackage package in host.Packages)
            if (full.StartsWith(Path.GetFullPath(package.Root) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                return package.Id;

        return "(unknown package)";
    }
}
