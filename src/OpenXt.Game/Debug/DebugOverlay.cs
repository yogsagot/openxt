using System.Numerics;
using DefaultEcs;
using ImGuiNET;
using OpenXt.Game.Modding;
using OpenXt.Modding;
using OpenXt.Sim;
using OpenXt.Sim.Components;
using OpenXt.Sim.Data;

namespace OpenXt.Game.Debug;

/// <summary>
/// Developer overlay. Not the game HUD — the shipping HUD is drawn by the renderer, in the game's
/// own visual language. Toggle with F1.
/// </summary>
public sealed class DebugOverlay
{
    private static readonly Vector4 Warning = new(1f, 0.75f, 0.3f, 1f);
    private static readonly Vector4 Bad = new(1f, 0.4f, 0.3f, 1f);

    private bool _visible = true;

    public void Draw(GameContext context, GameRegistry plugins, FixedStepClock clock)
    {
        if (ImGui.IsKeyPressed(ImGuiKey.F1, repeat: false))
            _visible = !_visible;

        if (!_visible)
            return;

        DrawSimWindow(context, clock);
        DrawModsWindow(context, plugins);
    }

    private static void DrawSimWindow(GameContext context, FixedStepClock clock)
    {
        Universe universe = context.Universe;
        Entity player = context.Player;

        ImGui.SetNextWindowSize(new Vector2(340f, 0f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowPos(new Vector2(16f, 16f), ImGuiCond.FirstUseEver);

        if (ImGui.Begin("OpenXT — sim"))
        {
            ImGui.Text($"Game        {universe.Rules.DisplayTitle}");
            ImGui.Text($"Tick        {universe.Tick}");
            ImGui.Text($"Step        {clock.Step * 1000f:F2} ms  ({1f / clock.Step:F0} Hz)");
            ImGui.Text($"Frame       {ImGui.GetIO().Framerate:F0} fps");

            if (clock.DroppedSteps > 0)
                ImGui.TextColored(Bad, $"Dropped     {clock.DroppedSteps} steps");

            ImGui.Separator();

            foreach (Sector sector in universe.Sectors)
            {
                ImGui.Text($"{sector.Name}: {sector.EntityCount} entities, "
                           + $"{sector.Physics.Simulation.Bodies.ActiveSet.Count} active bodies, "
                           + $"{sector.SystemCount} systems");
            }

            ImGui.Separator();

            ShipDefinition ship = universe.Ships[player.Get<ShipRef>().DefinitionIndex];

            // The archive's own name for the ship, when the asset cache is present; otherwise ours.
            string displayName = context.Assets.Text(ship.XbtfTextId) ?? ship.Name;
            ImGui.Text($"Ship        {displayName}");

            if (context.Assets.Problem is { } problem)
            {
                ImGui.TextColored(Warning, "No asset cache");
                ImGui.TextWrapped(problem);
            }
            else if (context.Assets.Manifest is { } manifest)
            {
                ImGui.TextDisabled($"Assets      {manifest.Game}: {manifest.MeshCount} meshes, "
                                   + $"{manifest.TextureCount} textures");
            }

            ImGui.Separator();

            Pose pose = player.Get<Pose>();
            Motion motion = player.Get<Motion>();
            ImGui.Text($"Position    {pose.Position.X,8:F0} {pose.Position.Y,8:F0} {pose.Position.Z,8:F0}");
            ImGui.Text($"Speed       {motion.Linear.Length(),8:F1} m/s");
            ImGui.Text($"Angular     {motion.Angular.X,6:F2} {motion.Angular.Y,6:F2} {motion.Angular.Z,6:F2}");

            // Input is only read while the window has focus; MonoGame's keyboard state is global,
            // so this line is the quickest way to tell a stuck key from a physics problem.
            FlightControl control = player.Get<FlightControl>();
            ImGui.Text($"Focus       {(context.IsFocused ? "yes" : "no")}");
            ImGui.Text($"Thrust      {control.Thrust.X,6:F2} {control.Thrust.Y,6:F2} {control.Thrust.Z,6:F2}");
            ImGui.Text($"Turn        {control.Turn.X,6:F2} {control.Turn.Y,6:F2} {control.Turn.Z,6:F2}");

            ImGui.Separator();
            ImGui.TextDisabled("W/S thrust  A/D strafe  R/F lift");
            ImGui.TextDisabled("arrows pitch/yaw  Q/E roll");
            ImGui.TextDisabled("F1 overlay  Esc quit");
        }

        ImGui.End();
    }

    /// <summary>
    /// What is actually loaded, and what refused to. With content coming from a stack of packages,
    /// "which mod is doing this" and "why did my mod not load" are the two questions that come up
    /// constantly — so both are one keypress away rather than buried in the console.
    /// </summary>
    private static void DrawModsWindow(GameContext context, GameRegistry plugins)
    {
        ModHost mods = context.Mods;

        ImGui.SetNextWindowSize(new Vector2(400f, 0f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowPos(new Vector2(16f, 470f), ImGuiCond.FirstUseEver);

        if (ImGui.Begin("OpenXT — packages"))
        {
            if (!mods.AssembliesAllowed)
                ImGui.TextColored(Warning, "safe mode: package code is not loaded");

            foreach (ModPackage package in mods.Packages)
            {
                string kind = package.Kind.ToString().ToLowerInvariant();
                ImGui.Text($"{package.Id}  {package.Version}");
                ImGui.SameLine();
                ImGui.TextDisabled($"[{kind}{(package.HasAssembly ? ", code" : "")}]");
            }

            ImGui.Separator();
            ImGui.TextDisabled($"{mods.Plugins.Count} plugin(s), {plugins.HookCount} frame hook(s)");

            if (mods.Diagnostics.Entries.Count > 0)
            {
                ImGui.Separator();

                foreach (ModDiagnostic diagnostic in mods.Diagnostics.Entries)
                {
                    Vector4 colour = diagnostic.Severity switch
                    {
                        ModSeverity.Error => Bad,
                        ModSeverity.Warning => Warning,
                        _ => new Vector4(0.7f, 0.7f, 0.7f, 1f),
                    };

                    ImGui.TextColored(colour, $"{diagnostic.PackageId}:");
                    ImGui.SameLine();
                    ImGui.TextWrapped(diagnostic.Message);
                }
            }
        }

        ImGui.End();

        // Panels contributed by packages, each in its own window so a mod cannot disturb the
        // engine's own overlay layout.
        foreach ((string id, IDebugPanel panel) in plugins.Panels())
        {
            ImGui.SetNextWindowSize(new Vector2(340f, 0f), ImGuiCond.FirstUseEver);

            if (ImGui.Begin(panel.Title))
                plugins.DrawPanel(id);

            ImGui.End();
        }
    }
}
