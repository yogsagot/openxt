using System.Globalization;
using System.Text;
using System.Text.Json;
using OpenXt.XArchive;

namespace OpenXt.AssetImport;

/// <summary>
/// Dumps the station and ware records compiled into the game executable, with the two things the
/// records do not store worked out here: each station's race, and the ids the rosters use. This is
/// how <c>games/*/data/stations/stations.json</c> and <c>wares/wares.json</c> are produced.
/// </summary>
public static class StationCommands
{
    private static readonly string[] Races = ["argon", "boron", "split", "paranid", "teladi", "xenon"];

    /// <summary>Row order of the five racial variants of one factory, in both games.</summary>
    private static readonly string[] VariantOrder = ["argon", "split", "paranid", "boron", "teladi"];

    /// <summary>
    /// Scenes outside the per-race blocks, assigned from their bodies and the variant row order
    /// (e.g. 4079 is a shield factory on body 442, the Split weapons-factory hull).
    /// </summary>
    private static readonly Dictionary<int, string> SceneRaces = new()
    {
        [4079] = "split", [4080] = "split", [4081] = "split", [4085] = "split",
        [4082] = "paranid", [4083] = "paranid", [4084] = "paranid", [4086] = "paranid",
        [4087] = "pirate", [4088] = "goner",
        [4089] = "argon", [4090] = "teladi", [4091] = "boron", [4092] = "split", [4093] = "paranid",
        [4110] = "xenon",
    };

    public static int RunStations(XInstall install, bool json)
    {
        TextTable names = ShipCommands.ReadEnglish(install) ?? throw new InvalidDataException("no English text table");
        List<Station> stations = Resolve(StationTable.Read(install), names, out List<string> warnings);

        if (json)
            WriteStations(stations);
        else
        {
            Console.WriteLine($"{install.DisplayName}: {stations.Count} stations from {Path.GetFileName(install.ExePath)}");
            Console.WriteLine();
            foreach (Station s in stations)
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{s.Record.BodyId,5} {s.Record.SceneId,5} {s.Record.TextId,5}  {s.Race,-8} {s.Kind,-16} {s.Id,-40} {s.Product}"));
        }

        foreach (string warning in warnings)
            Console.Error.WriteLine($"warning: {warning}");

        return 0;
    }

    public static int RunWares(XInstall install, bool json)
    {
        TextTable names = ShipCommands.ReadEnglish(install) ?? throw new InvalidDataException("no English text table");
        List<Station> stations = Resolve(StationTable.Read(install), names, out _);
        IReadOnlyList<WareRecord> table = WareTable.Read(install);

        // The ware table covers bulk goods; factories make the rest. Both, by text id, in order.
        SortedSet<int> wares = [];
        foreach (WareRecord ware in table)
            wares.Add(ware.TextId);
        foreach (Station station in stations)
            if (station.ProductTextId is { } product)
                wares.Add(product);

        if (!json)
        {
            Console.WriteLine($"{install.DisplayName}: {wares.Count} wares ({table.Count} in the ware table)");
            Console.WriteLine();
        }

        using Stream stdout = Console.OpenStandardOutput();
        using Utf8JsonWriter writer = new(stdout, new JsonWriterOptions { Indented = true });
        if (json)
            writer.WriteStartArray();

        foreach (int textId in wares)
        {
            string name = Clean(names[textId]) ?? $"Ware {textId}";
            string id = WareId(textId, names);

            if (!json)
            {
                Console.WriteLine($"{textId,5}  {id,-28} {name}");
                continue;
            }

            writer.WriteStartObject();
            writer.WriteString("id", id);
            writer.WriteString("name", name);
            writer.WriteNumber("xbtfTextId", textId);
            writer.WriteEndObject();
        }

        if (json)
            writer.WriteEndArray();

        return 0;
    }

    private sealed record Station(
        StationRecord Record, string Id, string Name, string Race, string Kind, string? Product, int? ProductTextId);

    private static List<Station> Resolve(IReadOnlyList<StationRecord> records, TextTable names, out List<string> warnings)
    {
        warnings = [];
        List<Station> stations = [];
        HashSet<string> ids = [];

        for (int i = 0; i < records.Count; i++)
        {
            StationRecord r = records[i];
            string text = Clean(names[r.TextId]) ?? "";

            // Props filed among the stations, not stations.
            if (r.TextId == 3751)
                continue;

            string? kind = KindOf(r, text);
            if (kind is null)
            {
                warnings.Add($"record {r.TextId} '{text}' (body {r.BodyId}) has no known kind; skipped");
                continue;
            }

            string? race = RaceOf(records, i, text, warnings);
            if (race is null)
            {
                warnings.Add($"record {r.TextId} '{text}' (scene {r.SceneId}) has no recoverable race; skipped");
                continue;
            }

            int? productText = kind is "factory" or "power_plant" or "mine" ? r.ProductTextId : null;
            string? product = productText is { } p ? WareId(p, names) : null;
            (string idStem, string name) = Naming(r, race, kind, text, names);

            string id = $"{race}_{idStem}".Replace($"{race}_{race}_", $"{race}_", StringComparison.Ordinal);
            if (race is "pirate" or "goner")
                id = idStem;

            string unique = id;
            for (int n = 2; !ids.Add(unique); n++)
                unique = $"{id}_{n}";

            if (unique != id)
                warnings.Add($"id '{id}' repeats (body {r.BodyId}, scene {r.SceneId}); this one is '{unique}'");

            stations.Add(new Station(r, unique, name, race, kind, product, productText));
        }

        return stations;
    }

    /// <summary>
    /// The kind code, except that the Goner Temple and Pirate Base are named first: both games give
    /// them codes 1 and 2, which otherwise mean factories.
    /// </summary>
    private static string? KindOf(StationRecord r, string text) => r.Kind switch
    {
        _ when text.Contains("Goner", StringComparison.OrdinalIgnoreCase) => "goner_temple",
        _ when text.Contains("Pirate", StringComparison.OrdinalIgnoreCase) => "pirate_base",
        1 or 2 or 3 => "factory",
        4 => "power_plant",
        5 => "mine",
        0 when text.Contains("Shipyard", StringComparison.OrdinalIgnoreCase) => "shipyard",
        0 when text.Contains("Equipment", StringComparison.OrdinalIgnoreCase) => "equipment_dock",
        0 when text.Contains("Trading", StringComparison.OrdinalIgnoreCase) => "trading_station",
        0 when text.Contains("Xenon", StringComparison.OrdinalIgnoreCase) => "xenon_station",
        _ => null,
    };

    /// <summary>
    /// Race is not stored. In order of preference: the name, for docks, shipyards and trading
    /// stations; the scene id, which falls in per-race blocks; and for the late X-Tension factories
    /// beyond those blocks, the row order of their five variants. Where row order and the scene
    /// disagree, the scene wins and the conflict is reported.
    /// </summary>
    private static string? RaceOf(IReadOnlyList<StationRecord> records, int index, string text, List<string> warnings)
    {
        StationRecord r = records[index];

        if (r.Kind == 0 || text.Contains("Goner", StringComparison.Ordinal) || text.Contains("Pirate", StringComparison.Ordinal))
        {
            if (text.Contains("Pirate", StringComparison.Ordinal)) return "pirate";
            if (text.Contains("Goner", StringComparison.Ordinal)) return "goner";
            foreach (string race in Races)
                if (text.Contains(race, StringComparison.OrdinalIgnoreCase))
                    return race;
        }

        string? byScene = RaceOfScene(r.SceneId);
        string? byRow = RaceOfRow(records, index);

        if (byScene is not null && byRow is not null && byScene != byRow)
            warnings.Add($"'{text}' scene {r.SceneId} says {byScene}, row order says {byRow}; using the scene");

        if (byScene is not null)
            return byScene;
        if (byRow is not null)
            return byRow;

        // Body 323, titled "Xenon Powerplant", is the one sceneless factory.
        return r.BodyId == 323 ? "xenon" : null;
    }

    private static string? RaceOfScene(int scene) => scene switch
    {
        >= 4000 and <= 4017 => "teladi",
        >= 4018 and <= 4035 => "argon",
        >= 4036 and <= 4052 => "boron",
        >= 4053 and <= 4065 => "split",
        >= 4066 and <= 4078 => "paranid",
        _ => SceneRaces.GetValueOrDefault(scene),
    };

    /// <summary>The race by position, when the record is one of a run of exactly five variants.</summary>
    private static string? RaceOfRow(IReadOnlyList<StationRecord> records, int index)
    {
        int text = records[index].TextId;
        int first = index;
        while (first > 0 && records[first - 1].TextId == text)
            first--;
        int last = index;
        while (last + 1 < records.Count && records[last + 1].TextId == text)
            last++;

        return last - first + 1 == VariantOrder.Length ? VariantOrder[index - first] : null;
    }

    private static (string IdStem, string Name) Naming(StationRecord r, string race, string kind, string text, TextTable names)
    {
        string Race = char.ToUpperInvariant(race[0]) + race[1..];

        switch (kind)
        {
            case "shipyard": return ("shipyard", $"{Race} Shipyard");
            case "equipment_dock": return ("equipment_dock", $"{Race} Equipment Dock");
            case "trading_station": return ("trading_station", text);
            case "pirate_base": return ("pirate_base", "Pirate Base");
            case "goner_temple": return ("goner_temple", "Goner Temple");
            case "xenon_station": return ("station", "Xenon Station");
        }

        // Factories filed under one generic name per product family are named by their product.
        int product = r.ProductTextId!.Value;
        string productId = WareId(product, names);
        string productName = Clean(names[product]) ?? productId;

        if (product is >= 2613 and <= 2693)
            return ($"{productId}_forge", $"{Race} {productName} Forge");
        if (product is >= 2713 and <= 2743)
            return ($"{productId}_factory", $"{Race} {productName} Factory");
        if (product is >= 2813 and <= 2853)
            return ($"{productId}_factory", $"{Race} {productName} Factory");

        string name = text.Length > 0 ? text : $"{productName} Factory";
        return (Snake(name), $"{Race} {name}");
    }

    /// <summary>Equipment reuses the ship roster's ids; everything else is its English name.</summary>
    private static string WareId(int textId, TextTable names)
    {
        if (textId is >= 2613 and <= 2693 && (textId - 2613) % 10 == 0)
            return ShipTable.Lasers[(textId - 2613) / 10];
        if (textId is >= 2713 and <= 2743 && (textId - 2713) % 10 == 0)
            return ShipTable.Shields[(textId - 2713) / 10];
        if (textId is >= 2813 and <= 2853 && (textId - 2813) % 10 == 0)
            return ShipTable.Missiles[(textId - 2813) / 10];

        return Snake(Clean(names[textId]) ?? $"ware_{textId}");
    }

    private static string? Clean(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string Snake(string text)
    {
        StringBuilder id = new();
        foreach (char c in text.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
                id.Append(c);
            else if (id.Length > 0 && id[^1] != '_')
                id.Append('_');
        }

        return id.ToString().Trim('_');
    }

    private static void WriteStations(List<Station> stations)
    {
        using Stream stdout = Console.OpenStandardOutput();
        using Utf8JsonWriter writer = new(stdout, new JsonWriterOptions { Indented = true });

        writer.WriteStartArray();
        foreach (Station s in stations)
        {
            writer.WriteStartObject();
            writer.WriteString("id", s.Id);
            writer.WriteString("name", s.Name);
            writer.WriteString("race", s.Race);
            writer.WriteString("kind", s.Kind);
            writer.WriteNumber("xbtfBodyId", s.Record.BodyId);
            writer.WriteNumber("xbtfSceneId", s.Record.SceneId);
            writer.WriteNumber("xbtfTextId", s.Record.TextId);
            if (s.Product is not null)
                writer.WriteString("product", s.Product);
            if (s.Record.Hull is { } hull)
                writer.WriteNumber("hull", hull);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }
}
