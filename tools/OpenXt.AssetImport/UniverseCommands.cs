using System.Text.Json;
using OpenXt.XArchive;

namespace OpenXt.AssetImport;

/// <summary>
/// The universe as the game's own script builds it: every sector's cell, gates, owner and stations,
/// read from the editor data compiled into <c>l/001.obj</c> (see <see cref="ScriptUniverse"/>). This
/// is the in-game source for what <c>games/*/data/sectors/sectors.json</c> records. Stations are
/// named by the ids <c>stations --json</c> gives them, so the two line up.
/// </summary>
public static class UniverseCommands
{
    private static readonly SectorGates[] Sides =
        [SectorGates.North, SectorGates.South, SectorGates.West, SectorGates.East];

    public static int Run(XInstall install, bool json, bool objects)
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

        TextTable names = ShipCommands.ReadEnglish(install) ?? throw new InvalidDataException("no English text table");
        Naming naming = new(install, universe, names);

        if (json)
            WriteJson(install.Game, universe, naming);
        else
            WriteTable(install, universe, naming, objects);

        return 0;
    }

    /// <summary>The text id that names the sector at a cell: <c>2YX</c> in XBTF, <c>102YYXX</c> in X-Tension.</summary>
    public static int SectorTextId(XGame game, int x, int y) =>
        game == XGame.BeyondTheFrontier ? 200 + y * 10 + x : 1_020_000 + y * 100 + x;

    /// <summary>Turns the script's numbers into the ids the rosters use.</summary>
    private sealed class Naming
    {
        private readonly TextTable _names;
        private readonly Dictionary<int, string> _races = [];
        private readonly IReadOnlyList<(string Id, string Race, string Kind, int TextId, int? ProductTextId)> _roster;

        public Naming(XInstall install, ScriptUniverse universe, TextTable names)
        {
            _names = names;
            Game = install.Game;
            _roster = StationCommands.Roster(install, names);

            foreach ((int index, int textId) in universe.Races)
                if (names[textId] is { } name)
                    _races[index] = name.Trim().ToLowerInvariant();
        }

        public XGame Game { get; }

        public string? Text(int textId) => _names[textId]?.Trim();

        public string? Race(int? index) => index is { } i ? _races.GetValueOrDefault(i) : null;

        public string? Ware(ScriptWare ware) =>
            ScriptWares.TextId(Game, ware) is { } textId ? StationCommands.WareId(textId, _names) : null;

        /// <summary>
        /// The roster id for a station: by text id and race, then by text id alone when only one station
        /// has it, then — for factories, whose XBTF records may be numbered from 5000 — by race and product.
        /// </summary>
        public string? Station(ScriptObject item)
        {
            if (item.StationTextId is not { } textId)
                return null;

            string? race = Race(item.Race);
            var byText = _roster.Where(s => s.TextId == textId).ToList();

            if (byText.FirstOrDefault(s => s.Race == race) is { Id: not null } exact)
                return exact.Id;
            if (byText.Count == 1)
                return byText[0].Id;

            if (item.ProductTextId is { } product
                && _roster.FirstOrDefault(s => s.Race == race && s.ProductTextId == product) is { Id: not null } made)
            {
                return made.Id;
            }

            return null;
        }
    }

    private static void WriteTable(XInstall install, ScriptUniverse universe, Naming naming, bool objects)
    {
        Console.WriteLine($"{universe.Sectors.Count} sectors  ({install.DisplayName})");
        Console.WriteLine();
        Console.WriteLine("   x   y  text id  owner    stations  ships  gates              name");

        foreach (ScriptSector sector in universe.Sectors.OrderBy(s => s.Y).ThenBy(s => s.X))
        {
            int textId = SectorTextId(install.Game, sector.X, sector.Y);
            Console.WriteLine($"{sector.X,4}{sector.Y,4}  {textId,7}  {naming.Race(sector.Owner) ?? "-",-8} " +
                              $"{sector.Objects.Count(o => o.IsStation),8}  " +
                              $"{sector.Objects.Count(o => o.Type == ScriptObjectType.Ship),5}  " +
                              $"{string.Join(",", GateNames(sector.Gates)),-17}  {naming.Text(textId) ?? "-"}");

            if (!objects)
                continue;

            foreach (ScriptObject item in sector.Objects.Where(o => o.IsStation))
            {
                Console.WriteLine($"{"",12}{item.East,10}{item.Up,10}{item.North,10}  " +
                                  $"{naming.Station(item) ?? $"? {naming.Text(item.StationTextId!.Value) ?? "-"} ({item.StationTextId})"}");
            }
        }

        if (universe.ExtraGates.Count == 0)
            return;

        Console.WriteLine();
        Console.WriteLine("Gates the script adds elsewhere, when the plot reaches them:");
        foreach (ScriptGate gate in universe.ExtraGates)
        {
            int textId = SectorTextId(install.Game, gate.X, gate.Y);
            Console.WriteLine($"{gate.X,4}{gate.Y,4}  {GateNames(gate.Side).Single(),-6} " +
                              $"{naming.Text(textId) ?? "-",-24} in {gate.Function}");
        }
    }

    private static void WriteJson(XGame game, ScriptUniverse universe, Naming naming)
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
            if (naming.Text(textId) is { } name)
                writer.WriteString("textName", name);
            writer.WriteNumber("x", sector.X);
            writer.WriteNumber("y", sector.Y);
            if (naming.Race(sector.Owner) is { } owner)
                writer.WriteString("race", owner);
            WriteGates(writer, "gates", sector.Gates);
            if (sector.Background is { } background)
                writer.WriteNumber("background", background);

            writer.WriteStartArray("stations");
            foreach (ScriptObject item in sector.Objects.Where(o => o.IsStation))
                WriteStation(writer, item, naming);
            writer.WriteEndArray();

            writer.WriteStartObject("others");
            foreach (IGrouping<ScriptObjectType, ScriptObject> group in sector.Objects.Where(o => !o.IsStation)
                         .GroupBy(o => o.Type).OrderBy(g => g.Key))
            {
                writer.WriteNumber(group.Key.ToString().ToLowerInvariant(), group.Count());
            }

            writer.WriteEndObject();
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

    private static void WriteStation(Utf8JsonWriter writer, ScriptObject item, Naming naming)
    {
        writer.WriteStartObject();
        if (naming.Station(item) is { } id)
            writer.WriteString("station", id);
        writer.WriteNumber("xbtfTextId", item.StationTextId!.Value);
        if (naming.Text(item.StationTextId.Value) is { } name)
            writer.WriteString("textName", name);
        writer.WriteString("type", item.Type.ToString());
        if (naming.Race(item.Race) is { } race)
            writer.WriteString("race", race);
        else
            writer.WriteNumber("raceIndex", item.Race);

        // The game's own units, unscaled: east, up, north.
        writer.WriteNumber("east", item.East);
        writer.WriteNumber("up", item.Up);
        writer.WriteNumber("north", item.North);

        WriteWares(writer, "products", item.Products, naming);
        WriteWares(writer, "resources", item.Resources, naming);
        WriteWares(writer, "secondaryResources", item.SecondaryResources, naming);
        WriteWares(writer, "tradeables", item.Tradeables, naming);
        if (item.Docked.Count > 0)
            writer.WriteNumber("docked", item.Docked.Count);
        writer.WriteEndObject();
    }

    /// <summary>Wares by id where the category is decoded, otherwise as the script's [main type, index].</summary>
    private static void WriteWares(Utf8JsonWriter writer, string name, IReadOnlyList<ScriptWare> wares, Naming naming)
    {
        if (wares.Count == 0)
            return;

        writer.WriteStartArray(name);
        foreach (ScriptWare ware in wares)
        {
            if (naming.Ware(ware) is { } id)
            {
                writer.WriteStringValue(id);
                continue;
            }

            writer.WriteStartArray();
            writer.WriteNumberValue(ware.MainType);
            writer.WriteNumberValue(ware.SubType);
            writer.WriteEndArray();
        }

        writer.WriteEndArray();
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
