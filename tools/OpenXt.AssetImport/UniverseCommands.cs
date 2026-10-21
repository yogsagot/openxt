using System.Text.Json;
using OpenXt.XArchive;

namespace OpenXt.AssetImport;

/// <summary>
/// The universe as the game's own script builds it: every sector's cell and gates, read from the
/// editor data compiled into <c>l/001.obj</c> (see <see cref="ScriptUniverse"/>). This is the
/// in-game source for what <c>games/*/data/sectors/sectors.json</c> records.
/// </summary>
public static class UniverseCommands
{
    private static readonly SectorGates[] Sides =
        [SectorGates.North, SectorGates.South, SectorGates.West, SectorGates.East];

    public static int Run(XInstall install, bool json)
    {
        using CatArchive archive = install.OpenArchive();

        if (!archive.TryGetEntry("l/001.obj", out CatEntry entry))
        {
            Console.Error.WriteLine($"l/001.obj is not in {Path.GetFileName(archive.CatPath)}.");
            return 1;
        }

        ScriptUniverse? universe = ScriptUniverse.Read(ScriptFile.Parse(archive.Read(entry)));
        if (universe is null)
        {
            Console.Error.WriteLine($"l/001.obj has no {ScriptUniverse.BuilderClassMethod} editor data.");
            return 1;
        }

        TextTable? names = ShipCommands.ReadEnglish(install);

        if (json)
            WriteJson(install.Game, universe, names);
        else
            WriteTable(install, universe, names);

        return 0;
    }

    /// <summary>The text id that names the sector at a cell: <c>2YX</c> in XBTF, <c>102YYXX</c> in X-Tension.</summary>
    public static int SectorTextId(XGame game, int x, int y) =>
        game == XGame.BeyondTheFrontier ? 200 + y * 10 + x : 1_020_000 + y * 100 + x;

    private static void WriteTable(XInstall install, ScriptUniverse universe, TextTable? names)
    {
        Console.WriteLine($"{universe.Sectors.Count} sectors  ({install.DisplayName})");
        Console.WriteLine();
        Console.WriteLine("   x   y  text id  objects  gates              name");

        foreach (ScriptSector sector in universe.Sectors.OrderBy(s => s.Y).ThenBy(s => s.X))
        {
            int textId = SectorTextId(install.Game, sector.X, sector.Y);
            Console.WriteLine($"{sector.X,4}{sector.Y,4}  {textId,7}  {sector.Objects.Count,7}  " +
                              $"{string.Join(",", GateNames(sector.Gates)),-17}  {names?[textId] ?? "-"}");
        }

        if (universe.ExtraGates.Count == 0)
            return;

        Console.WriteLine();
        Console.WriteLine("Gates the script adds elsewhere, when the plot reaches them:");
        foreach (ScriptGate gate in universe.ExtraGates)
        {
            int textId = SectorTextId(install.Game, gate.X, gate.Y);
            Console.WriteLine($"{gate.X,4}{gate.Y,4}  {GateNames(gate.Side).Single(),-6} " +
                              $"{names?[textId] ?? "-",-24} in {gate.Function}");
        }
    }

    private static void WriteJson(XGame game, ScriptUniverse universe, TextTable? names)
    {
        using Stream stdout = Console.OpenStandardOutput();
        using Utf8JsonWriter writer = new(stdout, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();
        writer.WriteStartArray("sectors");
        foreach (ScriptSector sector in universe.Sectors.OrderBy(s => s.Y).ThenBy(s => s.X))
        {
            int textId = SectorTextId(game, sector.X, sector.Y);
            writer.WriteStartObject();
            writer.WriteNumber("xbtfTextId", textId);
            if (names?[textId] is { } name)
                writer.WriteString("textName", name);
            writer.WriteNumber("x", sector.X);
            writer.WriteNumber("y", sector.Y);
            WriteGates(writer, "gates", sector.Gates);
            writer.WriteNumber("objects", sector.Objects.Count);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteStartArray("scriptedGates");
        foreach (ScriptGate gate in universe.ExtraGates)
        {
            writer.WriteStartObject();
            writer.WriteNumber("x", gate.X);
            writer.WriteNumber("y", gate.Y);
            writer.WriteString("side", GateNames(gate.Side).Single());
            writer.WriteString("function", gate.Function);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteGates(Utf8JsonWriter writer, string name, SectorGates gates)
    {
        writer.WriteStartArray(name);
        foreach (string side in GateNames(gates))
            writer.WriteStringValue(side);
        writer.WriteEndArray();
    }

    /// <summary>Lower-case side names in the order <c>sectors.json</c> lists them.</summary>
    private static IEnumerable<string> GateNames(SectorGates gates) =>
        Sides.Where(side => gates.HasFlag(side)).Select(side => side.ToString().ToLowerInvariant());
}
