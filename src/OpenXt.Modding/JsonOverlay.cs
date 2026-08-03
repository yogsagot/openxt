using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenXt.Modding;

/// <summary>
/// Merges the layers of a JSON content file into one document. This is how a mod changes the game's
/// data without shipping a copy of it, and how two mods that touch the same file coexist.
///
/// The rules, in full:
/// <list type="bullet">
///   <item><b>Objects merge</b>, key by key, recursively. A later layer that mentions
///   <c>mass</c> changes the mass and leaves everything else alone.</item>
///   <item><b>Arrays are replaced</b> — except arrays of objects carrying an <c>id</c>, which
///   <b>merge by id</b>. That single exception is what makes catalogs moddable: a later layer
///   patches the ships it names and appends the ones it invents.</item>
///   <item>Inside a merged array, <c>"$remove": true</c> deletes the entry and
///   <c>"$replace": true</c> substitutes it wholesale instead of patching field by field.</item>
///   <item><c>null</c> in a later layer clears the value rather than being ignored — deleting a
///   field has to be expressible.</item>
/// </list>
/// The merged tree is then handed to a source-generated deserializer, so the reflection-free rule
/// still holds: only the merge itself is dynamic, and it only ever sees JSON.
/// </summary>
public static class JsonOverlay
{
    /// <summary>Marks an array entry for deletion.</summary>
    public const string RemoveKey = "$remove";

    /// <summary>Marks an array entry as a wholesale replacement rather than a patch.</summary>
    public const string ReplaceKey = "$replace";

    private const string IdKey = "id";

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Reads and merges every file in order. Returns null when there are no layers at all — an
    /// absent optional catalog is a normal state, not an error.
    /// </summary>
    public static JsonNode? MergeFiles(IReadOnlyList<string> paths)
    {
        JsonNode? result = null;

        foreach (string path in paths)
        {
            using FileStream stream = File.OpenRead(path);
            JsonNode? layer = JsonNode.Parse(stream, nodeOptions: null, DocumentOptions);

            result = result is null ? layer : Merge(result, layer);
        }

        return result;
    }

    /// <summary>Merges parsed documents in order. Exposed for tests and for in-memory content.</summary>
    public static JsonNode? MergeAll(IEnumerable<JsonNode?> layers)
    {
        JsonNode? result = null;
        bool first = true;

        foreach (JsonNode? layer in layers)
        {
            if (first)
            {
                result = layer?.DeepClone();
                first = false;
                continue;
            }

            result = Merge(result, layer);
        }

        return result;
    }

    /// <summary>Applies <paramref name="overlay"/> on top of <paramref name="baseNode"/>.</summary>
    public static JsonNode? Merge(JsonNode? baseNode, JsonNode? overlay)
    {
        if (overlay is null)
            return null;

        if (baseNode is JsonObject baseObject && overlay is JsonObject overlayObject)
            return MergeObject(baseObject, overlayObject);

        if (baseNode is JsonArray baseArray && overlay is JsonArray overlayArray && IsKeyed(baseArray))
            return MergeKeyedArray(baseArray, overlayArray);

        return overlay.DeepClone();
    }

    private static JsonObject MergeObject(JsonObject baseObject, JsonObject overlayObject)
    {
        JsonObject result = (JsonObject)baseObject.DeepClone();

        foreach (KeyValuePair<string, JsonNode?> entry in overlayObject)
        {
            if (entry.Key is RemoveKey or ReplaceKey)
                continue;

            result[entry.Key] = result.TryGetPropertyValue(entry.Key, out JsonNode? existing)
                ? Merge(existing, entry.Value)
                : entry.Value?.DeepClone();
        }

        return result;
    }

    /// <summary>
    /// An array is treated as a catalog when every element is an object with a non-empty
    /// <c>id</c>. Anything else — a list of numbers, a list of anonymous objects — is a value, and
    /// a later layer replaces it outright.
    /// </summary>
    private static bool IsKeyed(JsonArray array)
    {
        if (array.Count == 0)
            return false;

        foreach (JsonNode? element in array)
            if (element is not JsonObject item || IdOf(item) is null)
                return false;

        return true;
    }

    private static string? IdOf(JsonObject item) =>
        item.TryGetPropertyValue(IdKey, out JsonNode? id) && id is JsonValue value
            ? value.TryGetValue(out string? text) && !string.IsNullOrEmpty(text) ? text : null
            : null;

    private static JsonArray MergeKeyedArray(JsonArray baseArray, JsonArray overlayArray)
    {
        // Insertion order is preserved: patched entries stay where they were, new entries append.
        List<string> order = [];
        Dictionary<string, JsonObject> byId = new(StringComparer.Ordinal);

        foreach (JsonNode? element in baseArray)
        {
            if (element is not JsonObject item || IdOf(item) is not { } id)
                continue;

            if (byId.TryAdd(id, (JsonObject)item.DeepClone()))
                order.Add(id);
        }

        foreach (JsonNode? element in overlayArray)
        {
            // A non-conforming entry in the overlay is dropped rather than corrupting the catalog;
            // the deserializer downstream would only fail more obscurely.
            if (element is not JsonObject item || IdOf(item) is not { } id)
                continue;

            if (IsFlagSet(item, RemoveKey))
            {
                byId.Remove(id);
                order.Remove(id);
                continue;
            }

            if (!byId.TryGetValue(id, out JsonObject? existing) || IsFlagSet(item, ReplaceKey))
            {
                JsonObject replacement = (JsonObject)item.DeepClone();
                replacement.Remove(ReplaceKey);

                if (!byId.ContainsKey(id))
                    order.Add(id);

                byId[id] = replacement;
                continue;
            }

            byId[id] = MergeObject(existing, item);
        }

        JsonArray result = [];
        foreach (string id in order)
            result.Add(byId[id]);

        return result;
    }

    private static bool IsFlagSet(JsonObject item, string key) =>
        item.TryGetPropertyValue(key, out JsonNode? flag)
        && flag is JsonValue value
        && value.TryGetValue(out bool set)
        && set;
}
