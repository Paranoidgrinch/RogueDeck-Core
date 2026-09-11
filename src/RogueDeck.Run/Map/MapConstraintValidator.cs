namespace RogueDeck.Run;

// Checks a generated map against its spec's constraints. It works on the generator's ROLE annotation (GeneratedMap),
// because a Combat and an Elite node share the same node TYPE — the role is what the per-path minimums count. For
// each kind it computes, by a reverse-topological DP, the MINIMUM number of that kind on any entry→boss path (the
// worst path), and compares it to the per-path minimum; likewise the worst-path enemy (Combat+Elite) count. Map-wide
// minimums compare plain totals. Returns human-readable problems (empty = all satisfied). O(V+E) per kind — no path
// enumeration, so it stays cheap on wide graphs. Assumes a DAG (the generator's output always is; validate topology
// with RunMapValidator otherwise).
public static class MapConstraintValidator
{
    public static IReadOnlyList<string> Validate(GeneratedMap generated, MapGenerationSpec spec)
    {
        ArgumentNullException.ThrowIfNull(generated);
        ArgumentNullException.ThrowIfNull(spec);

        var map = generated.Map;
        var roles = generated.Roles;
        var problems = new List<string>();

        foreach (var (kind, min) in spec.PerPathMinimums)
        {
            if (min <= 0)
                continue;
            var kindLocal = kind;
            var worst = MinCountOnAnyPath(map, roles, k => k == kindLocal);
            if (worst < min)
                problems.Add($"Every path should hold at least {min} {kind} node(s), but some path has only {worst}.");
        }

        foreach (var (kind, max) in spec.PerPathMaximums)
        {
            var kindLocal = kind;
            var richest = MaxCountOnAnyPath(map, roles, k => k == kindLocal);
            if (richest > max)
                problems.Add($"No path should hold more than {max} {kind} node(s), but some path holds {richest}.");
        }

        if (spec.MinEnemiesPerPath > 0)
        {
            var worst = MinCountOnAnyPath(map, roles, IsEnemy);
            if (worst < spec.MinEnemiesPerPath)
                problems.Add(
                    $"Every path should hold at least {spec.MinEnemiesPerPath} enemy node(s), but some path has only {worst}.");
        }

        foreach (var (kind, min) in spec.MapWideMinimums)
        {
            if (min <= 0)
                continue;
            var kindLocal = kind;
            var total = roles.Values.Count(k => k == kindLocal);
            if (total < min)
                problems.Add($"The map should hold at least {min} {kind} node(s) in total, but has {total}.");
        }

        return problems;
    }

    // The fewest `matches`-role nodes on any entry→boss path (the worst path). Public so the generator can size how
    // many guarantee gates a kind still needs after crediting what the varied rows already provide.
    public static int WorstPathCount(
        RunMap map, IReadOnlyDictionary<NodeId, MapNodeKind> roles, Func<MapNodeKind, bool> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);
        return WorstPathCount(map, roles, (_, kind) => matches(kind));
    }

    // The same, when WHICH node it is matters as well as its kind (a treasure that may still flip into a mimic
    // cannot be counted toward a treasure guarantee, but the one on a guarantee row can).
    public static int WorstPathCount(
        RunMap map, IReadOnlyDictionary<NodeId, MapNodeKind> roles, Func<NodeId, MapNodeKind, bool> matches)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(matches);
        return MinCountOnAnyPath(map, roles, matches);
    }

    // The MOST `matches`-role nodes on any entry→boss path (the richest path). Public so the generator can see
    // where a per-path ceiling is broken and rewrite the offending nodes.
    public static int RichestPathCount(
        RunMap map, IReadOnlyDictionary<NodeId, MapNodeKind> roles, Func<MapNodeKind, bool> matches)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(matches);
        return MaxCountOnAnyPath(map, roles, matches);
    }

    // Whether a role counts as an enemy for the min-enemies constraint.
    public static bool IsEnemyRole(MapNodeKind kind) => IsEnemy(kind);

    private static bool IsEnemy(MapNodeKind kind) =>
        kind is MapNodeKind.Combat or MapNodeKind.MultiCombat or MapNodeKind.Elite;

    // ── The traversal itself lives in WeightedPathEvaluator ──────────────────────────────────────────────
    // Counting how many Elites the thinnest route holds IS a weighted path score with a weight of 1 per Elite,
    // so this checker runs on top of that one DP rather than keeping a second copy of it. The weights are whole
    // numbers and the sums stay far inside the range a double represents exactly, so the rounding is not an
    // approximation — it is turning an exact integer back into an int.
    //
    // Each call walks the graph again (Validate does so once per constraint rather than once in total). On a map
    // of a few hundred rooms that is a handful of O(V+E) passes and beneath noticing; one implementation of the
    // traversal is worth more than saving them.
    private static int MinCountOnAnyPath(
        RunMap map, IReadOnlyDictionary<NodeId, MapNodeKind> roles, Func<MapNodeKind, bool> matches) =>
        MinCountOnAnyPath(map, roles, (_, kind) => matches(kind));

    private static int MinCountOnAnyPath(
        RunMap map, IReadOnlyDictionary<NodeId, MapNodeKind> roles, Func<NodeId, MapNodeKind, bool> matches) =>
        (int)Math.Round(WeightedPathEvaluator.MinimumPathScore(
            map, roles, (id, kind) => matches(id, kind) ? 1d : 0d));

    private static int MaxCountOnAnyPath(
        RunMap map, IReadOnlyDictionary<NodeId, MapNodeKind> roles, Func<MapNodeKind, bool> matches) =>
        (int)Math.Round(WeightedPathEvaluator.MaximumPathScore(
            map, roles, (_, kind) => matches(kind) ? 1d : 0d));
}
