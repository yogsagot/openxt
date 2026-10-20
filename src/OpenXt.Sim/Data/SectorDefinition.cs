namespace OpenXt.Sim.Data;

/// <summary>
/// One cell of the universe map: a sector, its owner and its gates. Properties are <c>set</c>,
/// never <c>init</c>; see <see cref="ShipDefinition"/>.
///
/// Gates are directions, not targets: the games lay sectors on a grid and only ever gate a sector
/// to the neighbouring cell, so the direction says everything and cannot point at a sector that is
/// not adjacent.
/// </summary>
public sealed record SectorDefinition : IDefinition
{
    public const string North = "north";
    public const string South = "south";
    public const string East = "east";
    public const string West = "west";

    public required string Id { get; set; }
    public required string Name { get; set; }

    /// <summary>Text id in the archive's language tables, which also encodes the cell.</summary>
    public int XbtfTextId { get; set; } = -1;

    /// <summary>Grid cell, 1-based as the games number it: x runs east, y runs south.</summary>
    public int X { get; set; }
    public int Y { get; set; }

    /// <summary>argon, boron, split, paranid, teladi, xenon or pirate.</summary>
    public string Race { get; set; } = "";

    /// <summary>The directions this sector has gates in: north, south, east, west.</summary>
    public List<string> Gates { get; set; } = [];

    /// <summary>Station definition ids in this sector, repeated once per station.</summary>
    public List<string>? Stations { get; set; }

    /// <summary>The neighbouring cell a gate direction leads to; null for an unknown direction.</summary>
    public static (int X, int Y)? Step(int x, int y, string direction) => direction switch
    {
        North => (x, y - 1),
        South => (x, y + 1),
        East => (x + 1, y),
        West => (x - 1, y),
        _ => null,
    };

    public static string? Opposite(string direction) => direction switch
    {
        North => South,
        South => North,
        East => West,
        West => East,
        _ => null,
    };
}

public sealed record SectorCatalogFile
{
    public required IReadOnlyList<SectorDefinition> Sectors { get; set; }
}
