using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenXt.Sim.Data;

/// <summary>
/// Data-driven ship stats. Adding a ship is a data change, never a code change —
/// nothing in the sim may hardcode these values.
/// </summary>
public sealed record ShipDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    /// <summary>Kilograms.</summary>
    public float Mass { get; init; } = 10_000f;

    /// <summary>Main engine force, newtons.</summary>
    public float MainThrust { get; init; } = 250_000f;

    /// <summary>Manoeuvring thruster force for strafe/lift, newtons.</summary>
    public float ManeuverThrust { get; init; } = 60_000f;

    /// <summary>Peak angular acceleration, radians per second squared (pitch, yaw, roll).</summary>
    public float PitchRate { get; init; } = 1.2f;
    public float YawRate { get; init; } = 1.0f;
    public float RollRate { get; init; } = 2.0f;

    /// <summary>Speed the flight computer holds when assist is on, metres per second.</summary>
    public float CruiseSpeed { get; init; } = 130f;

    /// <summary>Collision sphere radius, metres.</summary>
    public float HullRadius { get; init; } = 12f;
}

public sealed record ShipCatalogFile
{
    public required IReadOnlyList<ShipDefinition> Ships { get; init; }
}

/// <summary>Source-generated JSON context — no reflection-based serialization anywhere in the sim.</summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(ShipCatalogFile))]
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
