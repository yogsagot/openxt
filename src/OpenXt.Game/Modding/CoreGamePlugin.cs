using OpenXt.Game.Debug;

namespace OpenXt.Game.Modding;

/// <summary>
/// The engine's own presentation features, registered through <see cref="IGameRegistry"/> like a
/// package's — the game-layer counterpart of <c>CoreSimPlugin</c>, and for the same reason: if the
/// public door is good enough for the engine, it stays good enough for mods.
/// </summary>
public sealed class CoreGamePlugin : IGamePlugin
{
    public const string SectorMapPanelId = "openxt.sector-map";

    public void ConfigureGame(IGameRegistry registry) =>
        registry.AddDebugPanel(SectorMapPanelId, new SectorMapPanel());
}
