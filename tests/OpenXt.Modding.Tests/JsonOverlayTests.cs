using System.Text.Json.Nodes;
using Xunit;

namespace OpenXt.Modding.Tests;

public class JsonOverlayTests
{
    private static JsonNode Merge(params string[] layers)
    {
        List<JsonNode?> nodes = [];
        foreach (string layer in layers)
            nodes.Add(JsonNode.Parse(layer));

        return JsonOverlay.MergeAll(nodes)!;
    }

    [Fact]
    public void ObjectsMergeKeyByKey()
    {
        JsonNode merged = Merge(
            """{ "a": 1, "b": { "x": 1, "y": 2 } }""",
            """{ "b": { "y": 9 }, "c": 3 }""");

        Assert.Equal(1, (int)merged["a"]!);
        Assert.Equal(1, (int)merged["b"]!["x"]!);
        Assert.Equal(9, (int)merged["b"]!["y"]!);
        Assert.Equal(3, (int)merged["c"]!);
    }

    [Fact]
    public void PlainArraysAreReplaced()
    {
        JsonNode merged = Merge("""{ "a": [1, 2, 3] }""", """{ "a": [9] }""");

        JsonArray array = merged["a"]!.AsArray();
        Assert.Equal(9, (int)Assert.Single(array)!);
    }

    [Fact]
    public void KeyedArraysMergeByIdAndKeepOrder()
    {
        JsonNode merged = Merge(
            """{ "ships": [ { "id": "a", "mass": 1, "name": "A" }, { "id": "b", "mass": 2 } ] }""",
            """{ "ships": [ { "id": "b", "mass": 20 }, { "id": "c", "mass": 3 } ] }""");

        JsonArray ships = merged["ships"]!.AsArray();

        Assert.Equal(3, ships.Count);
        Assert.Equal(["a", "b", "c"], ships.Select(ship => (string)ship!["id"]!));

        // Patched, not replaced: the untouched field survives.
        Assert.Equal(20, (int)ships[1]!["mass"]!);
        Assert.Equal("A", (string)ships[0]!["name"]!);
    }

    [Fact]
    public void RemoveDeletesAnEntry()
    {
        JsonNode merged = Merge(
            """{ "ships": [ { "id": "a" }, { "id": "b" } ] }""",
            $$"""{ "ships": [ { "id": "a", "{{JsonOverlay.RemoveKey}}": true } ] }""");

        Assert.Equal("b", (string)Assert.Single(merged["ships"]!.AsArray())!["id"]!);
    }

    [Fact]
    public void ReplaceSwapsAnEntryWholesale()
    {
        JsonNode merged = Merge(
            """{ "ships": [ { "id": "a", "mass": 1, "name": "A" } ] }""",
            $$"""{ "ships": [ { "id": "a", "mass": 5, "{{JsonOverlay.ReplaceKey}}": true } ] }""");

        JsonNode ship = Assert.Single(merged["ships"]!.AsArray())!;

        Assert.Equal(5, (int)ship["mass"]!);
        Assert.Null(ship["name"]);
        Assert.Null(ship[JsonOverlay.ReplaceKey]);
    }

    [Fact]
    public void NullClearsAValue()
    {
        JsonNode merged = Merge("""{ "title": "old" }""", """{ "title": null }""");
        Assert.Null(merged["title"]);
    }

    [Fact]
    public void ThreeLayersApplyInOrder()
    {
        JsonNode merged = Merge(
            """{ "ships": [ { "id": "a", "mass": 1 } ] }""",
            """{ "ships": [ { "id": "a", "mass": 2 } ] }""",
            """{ "ships": [ { "id": "a", "mass": 3 } ] }""");

        Assert.Equal(3, (int)Assert.Single(merged["ships"]!.AsArray())!["mass"]!);
    }

    [Fact]
    public void MergesFilesFromDisk()
    {
        using PackageTree tree = new();
        string first = tree.Package(tree.Mods, "one", PackageTree.Manifest("one"));
        string second = tree.Package(tree.Mods, "two", PackageTree.Manifest("two"));

        tree.Content(first, "cat.json", """{ "items": [ { "id": "x", "value": 1 } ] }""");
        tree.Content(second, "cat.json", """{ "items": [ { "id": "x", "value": 2 } ] }""");

        JsonNode? merged = JsonOverlay.MergeFiles(
        [
            Path.Combine(first, "data", "cat.json"),
            Path.Combine(second, "data", "cat.json"),
        ]);

        Assert.Equal(2, (int)Assert.Single(merged!["items"]!.AsArray())!["value"]!);
    }
}
