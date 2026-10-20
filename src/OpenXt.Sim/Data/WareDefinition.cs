namespace OpenXt.Sim.Data;

/// <summary>
/// A tradeable ware: energy, minerals, food, high-tech goods and equipment. Equipment uses the same
/// ids the ship roster's equipment limits name (<c>gamma_pac</c>, <c>shield_25mw</c>, <c>hornet</c>).
/// Properties are <c>set</c>, never <c>init</c>; see <see cref="ShipDefinition"/>.
/// </summary>
public sealed record WareDefinition : IDefinition
{
    public required string Id { get; set; }
    public required string Name { get; set; }

    /// <summary>Text id in the archive's language tables; the description is conventionally id + 1.</summary>
    public int XbtfTextId { get; set; } = -1;

    /// <summary>The game's trading range in credits; null where it is not known.</summary>
    public int? PriceMin { get; set; }
    public int? PriceMax { get; set; }
}

public sealed record WareCatalogFile
{
    public required IReadOnlyList<WareDefinition> Wares { get; set; }
}
