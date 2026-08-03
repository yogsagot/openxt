using System.Text.Json;
using System.Text.Json.Nodes;
using OpenXt.Modding;

namespace OpenXt.Sim.Data;

/// <summary>Raised when the loaded packages cannot produce a world to run.</summary>
public sealed class ModContentException(string message) : Exception(message);

/// <summary>
/// Builds the simulation's data from the layered content of the loaded packages.
///
/// Every catalog is read the same way: take each layer of one relative path in load order, merge
/// them with <see cref="JsonOverlay"/>, then deserialize the result with the source-generated
/// context. So a mod adds a ship, retunes one, or deletes one purely by shipping a file at the same
/// path — no engine code knows that mod exists.
/// </summary>
public static class ContentCatalogs
{
    /// <summary>Ship catalog path, relative to a package's content root.</summary>
    public const string ShipsPath = "ships/ships.json";

    /// <summary>Ruleset path, relative to a package's content root.</summary>
    public const string RulesetPath = "rules/ruleset.json";

    public static ShipCatalog LoadShips(ModContent content)
    {
        IReadOnlyList<string> layers = content.Layers(ShipsPath);

        if (layers.Count == 0)
            throw new ModContentException(
                $"No package provides {ShipsPath}. A game package must define at least one ship.");

        ShipCatalogFile file = Deserialize(layers, SimJsonContext.Default.ShipCatalogFile, ShipsPath);

        if (file.Ships.Count == 0)
            throw new ModContentException($"The merged {ShipsPath} contains no ships.");

        return ShipCatalog.Create(file.Ships);
    }

    public static GameRuleset LoadRuleset(ModContent content)
    {
        IReadOnlyList<string> layers = content.Layers(RulesetPath);

        if (layers.Count == 0)
            throw new ModContentException(
                $"No package provides {RulesetPath}. A game package must define its ruleset.");

        return Deserialize(layers, SimJsonContext.Default.GameRuleset, RulesetPath);
    }

    private static T Deserialize<T>(
        IReadOnlyList<string> layers,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        string path)
    {
        JsonNode? merged;

        try
        {
            merged = JsonOverlay.MergeFiles(layers);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            throw new ModContentException($"{path} could not be read: {ex.Message}");
        }

        if (merged is null)
            throw new ModContentException($"{path} merged to nothing.");

        try
        {
            return merged.Deserialize(typeInfo)
                   ?? throw new ModContentException($"{path} merged to null.");
        }
        catch (JsonException ex)
        {
            // Name the layers: with several packages contributing, "which file is wrong" is the
            // only question worth answering here.
            throw new ModContentException(
                $"{path} is invalid after merging {layers.Count} layer(s) " +
                $"({string.Join(", ", layers)}): {ex.Message}");
        }
    }
}
