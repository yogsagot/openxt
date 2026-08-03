using ImGuiNET;
using OpenXt.Game.Modding;
using OpenXt.Sim;
using OpenXt.Sim.Modding;
using OpenXt.Sim.Systems;

namespace OpenXt.SampleMod;

/// <summary>
/// The mod's entry point. One class implements both capabilities to show they compose, but they
/// are separate interfaces on purpose: a mod that only implements <see cref="ISimPlugin"/> never
/// touches MonoGame and runs in a headless host, and one that only implements
/// <see cref="IGamePlugin"/> adds no simulation behaviour at all.
///
/// The loader finds this type because it implements <c>IPlugin</c> (through both interfaces) and
/// has a public parameterless constructor. Nothing is registered by attribute or by naming
/// convention, and nothing is scanned outside the assembly the manifest names.
/// </summary>
public sealed class SamplePlugin : ISimPlugin, IGamePlugin
{
    // One system exists per sector, so the panel reads whichever ones have been created. Building
    // this list at registration time is fine; it is not the tick path.
    private readonly List<IdleDriftSystem> _drifters = [];

    public void ConfigureSim(ISimRegistry registry) =>
        registry.AddSectorSystem(
            "openxt.sample.drift",
            SectorStage.Intent,
            CreateDrift,
            // Before the flight model reads the intent this writes — which is what Intent means, but
            // stating an order makes the dependency explicit if another mod joins the stage.
            order: -10);

    private ISectorSystem CreateDrift(Sector sector)
    {
        IdleDriftSystem system = new(sector);
        _drifters.Add(system);
        return system;
    }

    public void ConfigureGame(IGameRegistry registry) =>
        registry.AddDebugPanel("openxt.sample.panel", new SamplePanel(_drifters));
}

/// <summary>A section in the F1 developer overlay, drawn with the engine's ImGui context.</summary>
internal sealed class SamplePanel(IReadOnlyList<IdleDriftSystem> drifters) : IDebugPanel
{
    public string Title => "Sample Mod";

    public void Draw(GameContext context)
    {
        int flying = 0;
        for (int i = 0; i < drifters.Count; i++)
            flying += drifters[i].Count;

        ImGui.Text($"Game        {context.Rules.Id}");
        ImGui.Text($"Drifting    {flying} ship(s) in {drifters.Count} sector(s)");

        ImGui.Separator();
        ImGui.TextDisabled("This panel, the drift system and the");
        ImGui.TextDisabled("patched ship stats all come from");
        ImGui.TextDisabled("samples/mods/OpenXt.SampleMod.");

        if (context.Ships.TryIndexOf("sample_courier", out int index))
            ImGui.Text($"Added ship  {context.Ships[index].Name} " +
                       $"({context.Ships[index].CruiseSpeed:F0} m/s cruise)");
    }
}
