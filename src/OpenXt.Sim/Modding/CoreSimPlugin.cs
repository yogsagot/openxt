using OpenXt.Sim.Systems;

namespace OpenXt.Sim.Modding;

/// <summary>
/// The engine's own systems, registered through the public plugin API rather than wired into
/// <see cref="Sector"/> directly.
///
/// This is not ceremony. It means the flight model occupies an ordinary slot in the same ordered
/// pipeline a mod registers into, so a mod can run before or after it, and any API weakness shows
/// up in our own code first. It is always registered first, before any package's plugin.
/// </summary>
public sealed class CoreSimPlugin : ISimPlugin
{
    public const string FlightSystemId = "openxt.flight";

    public void ConfigureSim(ISimRegistry registry) =>
        registry.AddSectorSystem(
            FlightSystemId,
            SectorStage.Movement,
            static sector => new FlightSystem(sector.World, sector.Ships, sector.Physics));
}
