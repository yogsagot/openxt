namespace OpenXt.Modding;

/// <summary>
/// Marks a type the loader should instantiate from a package assembly. Deliberately empty.
///
/// The capabilities live one layer up: <c>OpenXt.Sim.Modding.ISimPlugin</c> for simulation
/// behaviour, <c>OpenXt.Game.Modding.IGamePlugin</c> for presentation. A plugin implements
/// whichever it needs, and each layer asks the host for the interface it understands
/// (<see cref="ModHost.PluginsOf{T}"/>). That is what keeps this project free of both ECS and
/// graphics, and what lets a sim-only mod load in a headless host with no window in sight.
///
/// Implementations need a public parameterless constructor and must do nothing expensive in it —
/// construction happens before either layer has configured anything.
/// </summary>
public interface IPlugin;
