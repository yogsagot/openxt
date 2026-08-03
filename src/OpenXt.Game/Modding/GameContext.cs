using DefaultEcs;
using Microsoft.Xna.Framework.Graphics;
using OpenXt.Game.Assets;
using OpenXt.Modding;
using OpenXt.Sim;
using OpenXt.Sim.Data;

namespace OpenXt.Game.Modding;

/// <summary>
/// The presentation layer's surface for plugins: the device to draw with, the world to draw, and
/// the packages that produced it.
///
/// Deliberately a handful of properties rather than a service container. What a mod can reach is
/// then visible in one file, and widening it is a decision someone makes on purpose.
/// </summary>
public sealed class GameContext
{
    public required GraphicsDevice GraphicsDevice { get; init; }

    /// <summary>The MonoGame host, for window and timing questions.</summary>
    public required Microsoft.Xna.Framework.Game Host { get; init; }

    /// <summary>The loaded packages, their content stack and their diagnostics.</summary>
    public required ModHost Mods { get; init; }

    public required Universe Universe { get; init; }

    public required AssetCache Assets { get; init; }

    /// <summary>The sector being simulated and drawn. Will change when jumping between sectors.</summary>
    public Sector Sector { get; internal set; } = null!;

    /// <summary>The entity the local player is flying.</summary>
    public Entity Player { get; internal set; }

    public ShipCatalog Ships => Universe.Ships;

    public GameRuleset Rules => Universe.Rules;

    /// <summary>True while the window has focus; input is only read then.</summary>
    public bool IsFocused { get; internal set; }
}
