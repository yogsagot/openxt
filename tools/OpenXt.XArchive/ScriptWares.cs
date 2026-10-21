namespace OpenXt.XArchive;

/// <summary>
/// Names the wares in the editor data. A <see cref="ScriptWare"/> is the engine's main type and an
/// index within it; the engine resolves it through a native (<c>SA_GetTypeTextID</c>) whose tables
/// are not decoded. These were established instead from the data, both ways round: X-Tension's
/// factories against the recipes in its station roster, and XBTF's against elite-games.ru's map of
/// which stations stand in which sector. Every confirmed index follows text-id order within its
/// category, and the two games number the categories one apart (XBTF's are X-Tension's plus one).
/// Technology goes past the five factory goods listed here — trading stations list indices up to
/// 40 — and those are left unnamed.
/// </summary>
public static class ScriptWares
{
    private static readonly int[] Lasers = [2613, 2623, 2633, 2643, 2653, 2663, 2673, 2683, 2693];
    private static readonly int[] Shields = [2713, 2723, 2733, 2743];
    private static readonly int[] Missiles = [2813, 2823, 2833, 2843, 2853];
    private static readonly int[] Energy = [2013];

    /// <summary>Wheat, beef, plankton, BoGas, Scruffin, Chelts, Soja, Maja, Sunrise, Teladianium, swamp plant.</summary>
    private static readonly int[] Bio = [2143, 2153, 2243, 2253, 2343, 2353, 2443, 2453, 2543, 2553, 2563];

    /// <summary>Cloth Rimes, Meatsteak, Space Fuel, Stott, BoFu, Massom, Rastar, Majaglit, Soja husk, Nostrop, spaceweed.</summary>
    private static readonly int[] Food = [2173, 2183, 2193, 2273, 2283, 2373, 2383, 2473, 2483, 2573, 2583];

    private static readonly int[] Minerals = [2023, 2033];

    /// <summary>Warheads, crystals, quantum tubes, microchips, computer components.</summary>
    private static readonly int[] Technology = [2043, 2053, 2063, 2073, 2083];

    /// <summary>X-Tension's main types; XBTF adds one to each.</summary>
    private static readonly Dictionary<int, int[]> TensionCategories = new()
    {
        [8] = Lasers,
        [9] = Shields,
        [10] = Missiles,
        [11] = Energy,
        [13] = Bio,
        [14] = Food,
        [15] = Minerals,
        [16] = Technology,
    };

    /// <summary>The ware's text id, or null if its category or index is not decoded.</summary>
    public static int? TextId(XGame game, ScriptWare ware)
    {
        int mainType = game == XGame.BeyondTheFrontier ? ware.MainType - 1 : ware.MainType;
        return TensionCategories.TryGetValue(mainType, out int[]? wares) && (uint)ware.SubType < (uint)wares.Length
            ? wares[ware.SubType]
            : null;
    }
}
