namespace OpenXt.Sim.Data;

/// <summary>Anything a catalog holds: a definition addressed by a stable, content-authored id.</summary>
public interface IDefinition
{
    string Id { get; }
}

/// <summary>
/// Immutable, index-addressable table of definitions loaded once at startup — the shape of
/// <see cref="ShipCatalog"/>, for the content types that need nothing more.
/// </summary>
public sealed class DefinitionCatalog<T> where T : class, IDefinition
{
    private readonly T[] _definitions;
    private readonly Dictionary<string, int> _byId;

    private DefinitionCatalog(T[] definitions)
    {
        _definitions = definitions;
        _byId = new Dictionary<string, int>(definitions.Length, StringComparer.Ordinal);
        for (int i = 0; i < definitions.Length; i++)
            _byId[definitions[i].Id] = i;
    }

    /// <summary>A catalog with nothing in it, for a game that defines none of these.</summary>
    public static DefinitionCatalog<T> Empty { get; } = new([]);

    public int Count => _definitions.Length;

    public T this[int index] => _definitions[index];

    public IReadOnlyList<T> All => _definitions;

    public int IndexOf(string id) =>
        _byId.TryGetValue(id, out int index)
            ? index
            : throw new KeyNotFoundException($"No {typeof(T).Name} with id '{id}'.");

    /// <summary>Non-throwing lookup: content naming an id nobody defines is a data mistake to report.</summary>
    public bool TryIndexOf(string id, out int index) => _byId.TryGetValue(id, out index);

    public bool Contains(string id) => _byId.ContainsKey(id);

    /// <summary>Builds a catalog from definitions already in memory — merged content, or a test.</summary>
    public static DefinitionCatalog<T> Create(IReadOnlyList<T> definitions) => new([.. definitions]);
}
