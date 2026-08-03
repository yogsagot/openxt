using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenXt.Sim.Data;

/// <summary>
/// Data-driven ship stats. Adding a ship is a data change, never a code change —
/// nothing in the sim may hardcode these values.
///
/// The properties are <c>set</c>, not <c>init</c>, and that is load-bearing: System.Text.Json's
/// source generator constructs a type with init-only properties without running its constructor,
/// which silently drops every property initializer below. A ship added by a mod that omits
/// <c>hullRadius</c> would get 0 instead of 12, and an omitted <c>xbtfBodyId</c> would get 0 —
/// body 0 — instead of -1, "no model". Defaults only survive on settable properties.
/// </summary>
public sealed record ShipDefinition
{
    public required string Id { get; set; }
    public required string Name { get; set; }

    /// <summary>Kilograms.</summary>
    public float Mass { get; set; } = 10_000f;

    /// <summary>Main engine force, newtons.</summary>
    public float MainThrust { get; set; } = 250_000f;

    /// <summary>Manoeuvring thruster force for strafe/lift, newtons.</summary>
    public float ManeuverThrust { get; set; } = 60_000f;

    /// <summary>Peak angular acceleration, radians per second squared (pitch, yaw, roll).</summary>
    public float PitchRate { get; set; } = 1.2f;
    public float YawRate { get; set; } = 1.0f;
    public float RollRate { get; set; } = 2.0f;

    /// <summary>Speed the flight computer holds when assist is on, metres per second.</summary>
    public float CruiseSpeed { get; set; } = 130f;

    /// <summary>Collision sphere radius, metres.</summary>
    public float HullRadius { get; set; } = 12f;

    /// <summary>
    /// Body number in the original XBTF archive, resolving to a mesh in the converted asset cache.
    /// -1 means "no model", which is normal: the game runs with debug shapes when no cache exists.
    ///
    /// This and <see cref="XbtfTextId"/> are plain ints so the simulation stays headless, but the
    /// simulation must never read them — they exist only for the rendering layer to resolve a mesh
    /// and a display name. Nothing here affects flight behaviour.
    /// </summary>
    public int XbtfBodyId { get; set; } = -1;

    /// <summary>Text id in the archive's language tables; the description is conventionally id + 1.</summary>
    public int XbtfTextId { get; set; } = -1;
}

public sealed record ShipCatalogFile
{
    public required IReadOnlyList<ShipDefinition> Ships { get; set; }
}

/// <summary>Source-generated JSON context — no reflection-based serialization anywhere in the sim.</summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(ShipCatalogFile))]
[JsonSerializable(typeof(GameRuleset))]
public partial class SimJsonContext : JsonSerializerContext;

/// <summary>Immutable, index-addressable ship table loaded once at startup.</summary>
public sealed class ShipCatalog
{
    private readonly ShipDefinition[] _definitions;
    private readonly Dictionary<string, int> _byId;

    private ShipCatalog(ShipDefinition[] definitions)
    {
        _definitions = definitions;
        _byId = new Dictionary<string, int>(definitions.Length, StringComparer.Ordinal);
        for (int i = 0; i < definitions.Length; i++)
            _byId[definitions[i].Id] = i;
    }

    public int Count => _definitions.Length;

    public ShipDefinition this[int index] => _definitions[index];

    public int IndexOf(string id) =>
        _byId.TryGetValue(id, out int index)
            ? index
            : throw new KeyNotFoundException($"No ship definition with id '{id}'.");

    /// <summary>
    /// Non-throwing lookup. Ship ids now come from mod-authored content, so "this package names a
    /// ship nobody defines" is an ordinary data mistake to report, not an exception to propagate.
    /// </summary>
    public bool TryIndexOf(string id, out int index) => _byId.TryGetValue(id, out index);

    /// <summary>Builds a catalog from definitions already in memory — merged content, or a test.</summary>
    public static ShipCatalog Create(IReadOnlyList<ShipDefinition> definitions) => new([.. definitions]);

    public static ShipCatalog Load(Stream json)
    {
        ShipCatalogFile file = JsonSerializer.Deserialize(json, SimJsonContext.Default.ShipCatalogFile)
                               ?? throw new InvalidDataException("Ship catalog was empty.");
        return new ShipCatalog(file.Ships.ToArray());
    }

    public static ShipCatalog LoadFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Load(stream);
    }
}
