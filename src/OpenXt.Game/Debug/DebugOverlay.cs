using DefaultEcs;
using ImGuiNET;
using OpenXt.Sim;
using OpenXt.Sim.Components;

namespace OpenXt.Game.Debug;

/// <summary>
/// Developer overlay. Not the game HUD — the shipping HUD is drawn by the renderer, in the game's
/// own visual language. Toggle with F1.
/// </summary>
public sealed class DebugOverlay
{
    private bool _visible = true;

    public void Draw(Universe universe, FixedStepClock clock, Entity player)
    {
        if (ImGui.IsKeyPressed(ImGuiKey.F1, repeat: false))
            _visible = !_visible;

        if (!_visible)
            return;

        ImGui.SetNextWindowSize(new System.Numerics.Vector2(340f, 0f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowPos(new System.Numerics.Vector2(16f, 16f), ImGuiCond.FirstUseEver);

        if (ImGui.Begin("OpenXT — sim"))
        {
            ImGui.Text($"Tick        {universe.Tick}");
            ImGui.Text($"Step        {clock.Step * 1000f:F2} ms  ({1f / clock.Step:F0} Hz)");
            ImGui.Text($"Frame       {ImGui.GetIO().Framerate:F0} fps");

            if (clock.DroppedSteps > 0)
                ImGui.TextColored(new System.Numerics.Vector4(1f, 0.4f, 0.3f, 1f),
                    $"Dropped     {clock.DroppedSteps} steps");

            ImGui.Separator();

            foreach (Sector sector in universe.Sectors)
            {
                ImGui.Text($"{sector.Name}: {sector.EntityCount} entities, "
                           + $"{sector.Physics.Simulation.Bodies.ActiveSet.Count} active bodies");
            }

            ImGui.Separator();

            Pose pose = player.Get<Pose>();
            Motion motion = player.Get<Motion>();
            ImGui.Text($"Position    {pose.Position.X,8:F0} {pose.Position.Y,8:F0} {pose.Position.Z,8:F0}");
            ImGui.Text($"Speed       {motion.Linear.Length(),8:F1} m/s");
            ImGui.Text($"Angular     {motion.Angular.X,6:F2} {motion.Angular.Y,6:F2} {motion.Angular.Z,6:F2}");

            ImGui.Separator();
            ImGui.TextDisabled("W/S thrust  A/D strafe  R/F lift");
            ImGui.TextDisabled("arrows pitch/yaw  Q/E roll");
            ImGui.TextDisabled("F1 overlay  Esc quit");
        }

        ImGui.End();
    }
}
