using System.Numerics;
using ImGuiNET;
using OpenXt.Game.Modding;
using OpenXt.Sim.Data;

namespace OpenXt.Game.Debug;

/// <summary>
/// The universe map in the F1 overlay: one cell per sector, filled by owning race, with a tick on
/// each edge that has a gate and the sector being simulated outlined. Developer tooling, like the
/// rest of the overlay — not the in-game galaxy map.
///
/// The layout is built once, the first time the panel draws, so a frame only walks arrays.
/// </summary>
public sealed class SectorMapPanel : IDebugPanel
{
    private const float Cell = 26f;
    private const float Gap = 6f;
    private const float Pitch = Cell + Gap;

    private Entry[]? _entries;
    private Vector2 _size;

    private readonly struct Entry(SectorDefinition sector, Vector2 offset, uint fill, int gates)
    {
        public readonly SectorDefinition Sector = sector;
        public readonly Vector2 Offset = offset;
        public readonly uint Fill = fill;

        /// <summary>Bit 0 north, 1 south, 2 east, 3 west.</summary>
        public readonly int Gates = gates;
    }

    public string Title => "Sector map";

    public void Draw(GameContext context)
    {
        DefinitionCatalog<SectorDefinition> map = context.SectorMap;
        if (map.Count == 0)
        {
            ImGui.TextDisabled("This game defines no sector map.");
            return;
        }

        if (_entries is null)
        {
            _entries = Build(map, out _size);

            // The overlay opens panels narrow, and has already spent ImGui's first-use sizing on
            // that; the X-Tension map is twenty cells wide. Size the window once, here, and leave
            // it to the user after that.
            ImGui.SetWindowSize(_size + ImGui.GetStyle().WindowPadding * 2f + new Vector2(0f, 40f), ImGuiCond.Always);
        }

        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        Vector2 origin = ImGui.GetCursorScreenPos();
        Vector2 mouse = ImGui.GetMousePos();
        string current = context.Sector.Name;

        uint gate = ImGui.GetColorU32(new Vector4(0.9f, 0.9f, 0.9f, 1f));
        uint highlight = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f));
        int hovered = -1;

        for (int i = 0; i < _entries.Length; i++)
        {
            ref readonly Entry e = ref _entries[i];
            Vector2 min = origin + e.Offset;
            Vector2 max = min + new Vector2(Cell);
            Vector2 mid = min + new Vector2(Cell / 2f);

            draw.AddRectFilled(min, max, e.Fill, 3f);

            if ((e.Gates & 1) != 0) draw.AddLine(new Vector2(mid.X, min.Y), new Vector2(mid.X, min.Y - Gap / 2f), gate, 2f);
            if ((e.Gates & 2) != 0) draw.AddLine(new Vector2(mid.X, max.Y), new Vector2(mid.X, max.Y + Gap / 2f), gate, 2f);
            if ((e.Gates & 4) != 0) draw.AddLine(new Vector2(max.X, mid.Y), new Vector2(max.X + Gap / 2f, mid.Y), gate, 2f);
            if ((e.Gates & 8) != 0) draw.AddLine(new Vector2(min.X, mid.Y), new Vector2(min.X - Gap / 2f, mid.Y), gate, 2f);

            if (e.Sector.Name == current)
                draw.AddRect(min - new Vector2(2f), max + new Vector2(2f), highlight, 4f, ImDrawFlags.None, 2f);

            if (mouse.X >= min.X && mouse.X < max.X && mouse.Y >= min.Y && mouse.Y < max.Y)
                hovered = i;
        }

        // Reserve the space the map drew into, so the window sizes to it and hovering works.
        ImGui.Dummy(_size);

        if (hovered >= 0 && ImGui.IsWindowHovered())
        {
            SectorDefinition s = _entries[hovered].Sector;
            ImGui.SetTooltip($"{s.Name}\n{s.Race}, ({s.X},{s.Y})\n{s.Stations?.Count ?? 0} stations, {s.Gates.Count} gates");
        }
    }

    private static Entry[] Build(DefinitionCatalog<SectorDefinition> map, out Vector2 size)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (SectorDefinition s in map.All)
        {
            minX = Math.Min(minX, s.X); maxX = Math.Max(maxX, s.X);
            minY = Math.Min(minY, s.Y); maxY = Math.Max(maxY, s.Y);
        }

        Entry[] entries = new Entry[map.Count];
        for (int i = 0; i < map.Count; i++)
        {
            SectorDefinition s = map[i];
            int gates = 0;
            foreach (string g in s.Gates)
            {
                gates |= g switch
                {
                    SectorDefinition.North => 1,
                    SectorDefinition.South => 2,
                    SectorDefinition.East => 4,
                    SectorDefinition.West => 8,
                    _ => 0,
                };
            }

            Vector2 offset = new(Gap / 2f + (s.X - minX) * Pitch, Gap / 2f + (s.Y - minY) * Pitch);
            entries[i] = new Entry(s, offset, ImGui.GetColorU32(RaceColour(s.Race)), gates);
        }

        size = new Vector2((maxX - minX + 1) * Pitch + Gap, (maxY - minY + 1) * Pitch + Gap);
        return entries;
    }

    /// <summary>Roughly the colours the original maps use for each race.</summary>
    private static Vector4 RaceColour(string race) => race switch
    {
        "argon" => new Vector4(0.00f, 0.60f, 0.67f, 1f),
        "boron" => new Vector4(0.30f, 0.20f, 0.65f, 1f),
        "split" => new Vector4(0.78f, 0.64f, 0.43f, 1f),
        "paranid" => new Vector4(0.55f, 0.53f, 0.05f, 1f),
        "teladi" => new Vector4(0.05f, 0.50f, 0.20f, 1f),
        "xenon" => new Vector4(0.80f, 0.10f, 0.10f, 1f),
        "pirate" => new Vector4(0.90f, 0.40f, 0.10f, 1f),
        _ => new Vector4(0.35f, 0.35f, 0.35f, 1f),
    };
}
