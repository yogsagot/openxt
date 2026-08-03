using Microsoft.Xna.Framework;
using OpenXt.Modding;

namespace OpenXt.Game.Modding;

/// <summary>The camera for the frame being drawn, in MonoGame's types.</summary>
public readonly record struct RenderView(Matrix View, Matrix Projection);

/// <summary>
/// Per-frame work in the presentation layer: input, camera, interpolation, anything tied to the
/// display rather than the simulation.
///
/// This is <b>not</b> the place for game rules. It runs once per rendered frame, at whatever rate
/// the display manages, so anything that affects the world belongs in an
/// <see cref="Sim.Systems.ISectorSystem"/> inside the fixed-step tick instead.
/// </summary>
public interface IFrameSystem
{
    void Update(GameContext context, float dt);
}

/// <summary>Draws into the world, after the engine has drawn the sector and before the debug UI.</summary>
public interface IWorldRenderer
{
    void Draw(GameContext context, in RenderView view);
}

/// <summary>A section a package contributes to the F1 developer overlay.</summary>
public interface IDebugPanel
{
    string Title { get; }

    void Draw(GameContext context);
}

/// <summary>What a package registers with the presentation layer.</summary>
public interface IGameRegistry
{
    /// <summary>Everything the plugin can reach: device, world, assets, the package list.</summary>
    GameContext Context { get; }

    void AddFrameSystem(string id, IFrameSystem system);

    void AddWorldRenderer(string id, IWorldRenderer renderer);

    void AddDebugPanel(string id, IDebugPanel panel);
}

/// <summary>
/// Implemented by a plugin that wants to draw or respond to frames. Configured after the graphics
/// device and the asset cache exist, so a plugin may create GPU resources here.
///
/// A package can implement this, <see cref="Sim.Modding.ISimPlugin"/>, or both — they are separate
/// interfaces precisely so a mod that only changes simulation behaviour never references MonoGame
/// and still runs in a headless host.
/// </summary>
public interface IGamePlugin : IPlugin
{
    void ConfigureGame(IGameRegistry registry);
}
