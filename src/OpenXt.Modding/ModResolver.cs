namespace OpenXt.Modding;

/// <summary>The packages that will load, in the order they will load, plus the game among them.</summary>
public sealed record ModLoadPlan(ModPackage Game, IReadOnlyList<ModPackage> Packages)
{
    /// <summary>
    /// A stable identity for this exact set of packages: ordered <c>id@version</c> pairs joined.
    /// A save records it, so loading a game that was played with a different mod set can say so
    /// instead of silently producing a broken world.
    /// </summary>
    public string Fingerprint
    {
        get
        {
            string[] parts = new string[Packages.Count];
            for (int i = 0; i < Packages.Count; i++)
                parts[i] = $"{Packages[i].Id}@{Packages[i].Version}";

            return string.Join(' ', parts);
        }
    }
}

/// <summary>
/// Turns a pile of discovered packages into a load order.
///
/// Three rules do the work:
/// <list type="bullet">
///   <item>Exactly one game loads. The others are excluded, along with anything that targets them.</item>
///   <item>A library loads only if something that loads requires it.</item>
///   <item>A package whose required dependency is missing, is the wrong version or has itself been
///   dropped is dropped too, with a reason. Nothing throws: a broken third-party mod costs itself,
///   not the run.</item>
/// </list>
/// The result is deterministic — the topological sort breaks ties by ordinal id — because it feeds
/// a fixed-step simulation whose system order must be reproducible across machines and runs.
/// </summary>
public static class ModResolver
{
    public static ModLoadPlan? Resolve(
        IReadOnlyList<ModPackage> discovered,
        string? gameId,
        IReadOnlySet<string> disabled,
        ModDiagnostics diagnostics)
    {
        Dictionary<string, ModPackage> byId = new(StringComparer.Ordinal);
        foreach (ModPackage package in discovered)
            byId[package.Id] = package;

        ModPackage? game = SelectGame(discovered, gameId, diagnostics);
        if (game is null)
            return null;

        // Seeds: the chosen game plus every enabled mod. Libraries are not seeds — they join only
        // by being required, which is the whole difference between the two kinds.
        List<ModPackage> seeds = [game];
        foreach (ModPackage package in discovered)
        {
            if (package.Kind != ModKind.Mod)
                continue;

            if (disabled.Contains(package.Id))
            {
                diagnostics.Info(package.Id, "disabled.");
                continue;
            }

            seeds.Add(package);
        }

        Dictionary<string, ModPackage> candidates = Closure(seeds, byId, disabled);
        Dictionary<string, string> failures = new(StringComparer.Ordinal);

        // Pass 1: reasons a package cannot load on its own terms.
        foreach (ModPackage package in candidates.Values)
        {
            foreach (ModDependency dependency in package.Manifest.Requires ?? [])
            {
                if (dependency.Optional)
                    continue;

                if (Unsatisfied(dependency, byId, disabled, game) is { } reason)
                {
                    failures.TryAdd(package.Id, reason);
                    break;
                }
            }
        }

        // Pass 2: propagate. Anything that required a dropped package goes with it.
        bool changed = true;
        while (changed)
        {
            changed = false;

            foreach (ModPackage package in candidates.Values)
            {
                if (failures.ContainsKey(package.Id))
                    continue;

                foreach (ModDependency dependency in package.Manifest.Requires ?? [])
                {
                    if (dependency.Optional || !failures.ContainsKey(dependency.Id))
                        continue;

                    failures[package.Id] = $"requires '{dependency.Id}', which was not loaded.";
                    changed = true;
                    break;
                }
            }
        }

        foreach ((string id, string reason) in failures)
        {
            ModSeverity severity = candidates[id].Kind == ModKind.Game ? ModSeverity.Error : ModSeverity.Warning;
            diagnostics.Add(severity, id, $"not loaded — {reason}");
        }

        if (failures.ContainsKey(game.Id))
            return null;

        // Pass 3: re-walk from the survivors so a library pulled in only by a dropped mod is
        // dropped as well, silently — it was never asked for on its own account.
        List<ModPackage> survivingSeeds = [];
        foreach (ModPackage seed in seeds)
            if (!failures.ContainsKey(seed.Id))
                survivingSeeds.Add(seed);

        Dictionary<string, ModPackage> loading = Closure(survivingSeeds, byId, disabled, failures);

        List<ModPackage> ordered = Sort(loading, diagnostics);

        // The game itself can only be missing here if it sat in a dependency cycle, in which case
        // there is no world to start.
        return ordered.Contains(game) ? new ModLoadPlan(game, ordered) : null;
    }

    private static ModPackage? SelectGame(
        IReadOnlyList<ModPackage> discovered,
        string? gameId,
        ModDiagnostics diagnostics)
    {
        List<ModPackage> games = [];
        foreach (ModPackage package in discovered)
            if (package.Kind == ModKind.Game)
                games.Add(package);

        if (games.Count == 0)
        {
            diagnostics.Error("(engine)", "no game package found. The engine ships no content of its own; " +
                                          "a game lives in games/<id> and is selected with --game.");
            return null;
        }

        if (gameId is null)
        {
            if (games.Count == 1)
                return games[0];

            string[] ids = new string[games.Count];
            for (int i = 0; i < games.Count; i++)
                ids[i] = games[i].Id;

            diagnostics.Error("(engine)", $"several games are installed ({string.Join(", ", ids)}); " +
                                          "choose one with --game <id>.");
            return null;
        }

        foreach (ModPackage candidate in games)
            if (string.Equals(candidate.Id, gameId, StringComparison.Ordinal))
                return candidate;

        diagnostics.Error("(engine)", $"no game package with id '{gameId}'.");
        return null;
    }

    /// <summary>Everything reachable from the seeds through their dependency lists.</summary>
    private static Dictionary<string, ModPackage> Closure(
        IReadOnlyList<ModPackage> seeds,
        Dictionary<string, ModPackage> byId,
        IReadOnlySet<string> disabled,
        Dictionary<string, string>? excluded = null)
    {
        Dictionary<string, ModPackage> reached = new(StringComparer.Ordinal);
        Queue<ModPackage> pending = new(seeds);

        while (pending.Count > 0)
        {
            ModPackage package = pending.Dequeue();

            if (excluded is not null && excluded.ContainsKey(package.Id))
                continue;

            if (!reached.TryAdd(package.Id, package))
                continue;

            foreach (ModDependency dependency in package.Manifest.Requires ?? [])
            {
                if (!byId.TryGetValue(dependency.Id, out ModPackage? target))
                    continue;

                // A game is never pulled in as someone else's dependency — the selected one is
                // already a seed, and any other would be a second game.
                if (target.Kind == ModKind.Game || disabled.Contains(target.Id))
                    continue;

                pending.Enqueue(target);
            }
        }

        return reached;
    }

    private static string? Unsatisfied(
        ModDependency dependency,
        Dictionary<string, ModPackage> byId,
        IReadOnlySet<string> disabled,
        ModPackage game)
    {
        if (!byId.TryGetValue(dependency.Id, out ModPackage? target))
            return $"requires '{dependency.Id}', which is not installed.";

        // The common case for a mod written for the other game: say so plainly rather than
        // reporting the game as a missing dependency.
        if (target.Kind == ModKind.Game && !string.Equals(target.Id, game.Id, StringComparison.Ordinal))
            return $"targets {target.Manifest.DisplayName} ({target.Id}), and {game.Id} is running.";

        if (disabled.Contains(target.Id))
            return $"requires '{dependency.Id}', which is disabled.";

        ModVersionRange range = ModVersionRange.Parse(dependency.Version);
        if (!range.Allows(target.Version))
            return $"requires '{dependency.Id}' {range}, but {target.Version} is installed.";

        return null;
    }

    /// <summary>
    /// Kahn's algorithm over dependency and load-order edges, taking the ordinally smallest ready
    /// id at each step so the order depends only on the package set, never on hash iteration or
    /// filesystem order.
    /// </summary>
    private static List<ModPackage> Sort(Dictionary<string, ModPackage> loading, ModDiagnostics diagnostics)
    {
        Dictionary<string, List<string>> edges = new(StringComparer.Ordinal);
        Dictionary<string, int> incoming = new(StringComparer.Ordinal);

        foreach (string id in loading.Keys)
        {
            edges[id] = [];
            incoming[id] = 0;
        }

        void Edge(string before, string after)
        {
            if (!loading.ContainsKey(before) || !loading.ContainsKey(after) || before == after)
                return;

            if (edges[before].Contains(after))
                return;

            edges[before].Add(after);
            incoming[after]++;
        }

        foreach (ModPackage package in loading.Values)
        {
            foreach (ModDependency dependency in package.Manifest.Requires ?? [])
                Edge(dependency.Id, package.Id);

            foreach (string other in package.Manifest.LoadAfter ?? [])
                Edge(other, package.Id);

            foreach (string other in package.Manifest.LoadBefore ?? [])
                Edge(package.Id, other);
        }

        SortedSet<string> ready = new(StringComparer.Ordinal);
        foreach ((string id, int count) in incoming)
            if (count == 0)
                ready.Add(id);

        List<ModPackage> ordered = new(loading.Count);

        while (ready.Count > 0)
        {
            string id = ready.Min!;
            ready.Remove(id);
            ordered.Add(loading[id]);

            foreach (string next in edges[id])
                if (--incoming[next] == 0)
                    ready.Add(next);
        }

        if (ordered.Count == loading.Count)
            return ordered;

        List<string> cycle = [];
        foreach ((string id, int count) in incoming)
            if (count > 0)
                cycle.Add(id);

        cycle.Sort(StringComparer.Ordinal);
        diagnostics.Error("(engine)", $"circular load order between: {string.Join(", ", cycle)}. " +
                                      "None of them were loaded.");

        // Everything outside the cycle still has a valid order, so keep it — a cycle among three
        // mods should not cost the game the other twenty.
        return ordered;
    }
}
