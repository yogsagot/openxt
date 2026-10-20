namespace OpenXt.Sim.Data;

/// <summary>
/// A station type: a shipyard, dock, trading station or factory variant. Data-driven like
/// <see cref="ShipDefinition"/> — adding one is a data change — and with the same rule: properties
/// are <c>set</c>, never <c>init</c>, or the source generator drops their initializers.
///
/// Placement is not here. A definition says what a station is; which sector holds one, and
/// where, belongs to the universe map, which is not imported yet.
/// </summary>
public sealed record StationDefinition : IDefinition
{
    public required string Id { get; set; }
    public required string Name { get; set; }

    /// <summary>argon, boron, split, paranid, teladi, xenon, pirate or goner.</summary>
    public string Race { get; set; } = "";

    /// <summary>
    /// shipyard, equipment_dock, trading_station, factory, mine, power_plant, pirate_base,
    /// goner_temple or xenon_station.
    /// </summary>
    public string Kind { get; set; } = "";

    /// <summary>Main body in the converted asset cache; -1 for none. Rendering only.</summary>
    public int XbtfBodyId { get; set; } = -1;

    /// <summary>
    /// The station's scene in the original archive (<c>v/0NNNN.pbd</c> in XBTF, <c>cut/0NNNN.pbd</c>
    /// in X-Tension), the multi-part model that places its bodies and lights; -1 for none.
    /// Rendering only, and unused until scenes are imported.
    /// </summary>
    public int XbtfSceneId { get; set; } = -1;

    /// <summary>Text id in the archive's language tables; the description is conventionally id + 1.</summary>
    public int XbtfTextId { get; set; } = -1;

    // The original game's figures. Null means the game does not say; see the roster headers for
    // where each comes from.

    /// <summary>Ware id of what the station produces, for factories, mines and power plants.</summary>
    public string? Product { get; set; }

    /// <summary>Inferred from the station record; unconfirmed.</summary>
    public int? Hull { get; set; }

    /// <summary>Purchase price in credits.</summary>
    public int? Price { get; set; }

    /// <summary>Product units made per production cycle.</summary>
    public int? Output { get; set; }

    /// <summary>Wares consumed per production cycle.</summary>
    public List<StationInput>? Inputs { get; set; }

    /// <summary>Length of one production cycle in seconds, where it is fixed.</summary>
    public int? CycleSeconds { get; set; }
}

/// <summary>One input of a production cycle.</summary>
public sealed record StationInput
{
    public required string Ware { get; set; }
    public int Amount { get; set; }
}

public sealed record StationCatalogFile
{
    public required IReadOnlyList<StationDefinition> Stations { get; set; }
}
