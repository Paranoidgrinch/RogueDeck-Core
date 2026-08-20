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
        var order = TopologicalOrder(map);
        var problems = new List<string>();

        foreach (var (kind, min) in spec.PerPathMinimums)
        {
            if (min <= 0)
                continue;
            var kindLocal = kind;
            var worst = MinCountOnAnyPath(map, order, roles, k => k == kindLocal);
            if (worst < min)
                problems.Add($"Every path should hold at least {min} {kind} node(s), but some path has only {worst}.");
        }

        foreach (var (kind, max) in spec.PerPathMaximums)
        {
            var kindLocal = kind;
            var richest = MaxCountOnAnyPath(map, order, roles, k => k == kindLocal);
            if (richest > max)
                problems.Add($"No path should hold more than {max} {kind} node(s), but some path holds {richest}.");
        }

        if (spec.MinEnemiesPerPath > 0)
        {
            var worst = MinCountOnAnyPath(map, order, roles, IsEnemy);
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
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(matches);
        return MinCountOnAnyPath(map, TopologicalOrder(map), roles, matches);
    }

    // The MOST `matches`-role nodes on any entry→boss path (the richest path). Public so the generator can see
    // where a per-path ceiling is broken and rewrite the offending nodes.
    public static int RichestPathCount(
        RunMap map, IReadOnlyDictionary<NodeId, MapNodeKind> roles, Func<MapNodeKind, bool> matches)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(matches);
        return MaxCountOnAnyPath(map, TopologicalOrder(map), roles, matches);
    }

    // Whether a role counts as an enemy for the min-enemies constraint.
    public static bool IsEnemyRole(MapNodeKind kind) => IsEnemy(kind);

    private static bool IsEnemy(MapNodeKind kind) =>
        kind is MapNodeKind.Combat or MapNodeKind.MultiCombat or MapNodeKind.Elite;

    // The fewest `matches` nodes on any entry→boss path. minPath(node) = self + min over successors; a leaf is just
    // self. The answer is the minimum over entry nodes. Processed in reverse topological order so every successor is
    // solved before the node that depends on it.
    private static int MinCountOnAnyPath(
        RunMap map, IReadOnlyList<NodeId> order, IReadOnlyDictionary<NodeId, MapNodeKind> roles,
        Func<MapNodeKind, bool> matches)
    {
        var minPath = new Dictionary<NodeId, int>();
        for (var i = order.Count - 1; i >= 0; i--)
        {
            var id = order[i];
            var self = roles.TryGetValue(id, out var kind) && matches(kind) ? 1 : 0;

            var best = int.MaxValue;
            foreach (var successor in map.SuccessorIds(id))
                best = Math.Min(best, minPath[successor]);
            minPath[id] = best == int.MaxValue ? self : self + best; // no successors ⇒ leaf
        }

        var answer = int.MaxValue;
        foreach (var entry in EntryNodes(map))
            answer = Math.Min(answer, minPath.TryGetValue(entry, out var value) ? value : 0);
        return answer == int.MaxValue ? 0 : answer;
    }

    // The most `matches` nodes on any entry→boss path — the mirror of MinCountOnAnyPath.
    private static int MaxCountOnAnyPath(
        RunMap map, IReadOnlyList<NodeId> order, IReadOnlyDictionary<NodeId, MapNodeKind> roles,
        Func<MapNodeKind, bool> matches)
    {
        var maxPath = new Dictionary<NodeId, int>();
        for (var i = order.Count - 1; i >= 0; i--)
        {
            var id = order[i];
            var self = roles.TryGetValue(id, out var kind) && matches(kind) ? 1 : 0;

            var best = -1;
            foreach (var successor in map.SuccessorIds(id))
                best = Math.Max(best, maxPath[successor]);
            maxPath[id] = best < 0 ? self : self + best; // no successors ⇒ leaf
        }

        var answer = 0;
        foreach (var entry in EntryNodes(map))
            answer = Math.Max(answer, maxPath.TryGetValue(entry, out var value) ? value : 0);
        return answer;
    }

    private static IEnumerable<NodeId> EntryNodes(RunMap map) =>
        map.EntryNodeIds.Count > 0 ? map.EntryNodeIds : map.RootIds();

    // Kahn's algorithm: nodes ordered so that every node precedes its successors.
    private static IReadOnlyList<NodeId> TopologicalOrder(RunMap map)
    {
        var inDegree = new Dictionary<NodeId, int>();
        foreach (var node in map.Nodes)
            inDegree.TryAdd(node.Id, 0);
        foreach (var node in map.Nodes)
            foreach (var successor in map.SuccessorIds(node.Id))
                inDegree[successor] = inDegree.TryGetValue(successor, out var degree) ? degree + 1 : 1;

        var queue = new Queue<NodeId>();
        foreach (var node in map.Nodes)
            if (inDegree[node.Id] == 0)
                queue.Enqueue(node.Id);

        var order = new List<NodeId>();
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            order.Add(id);
            foreach (var successor in map.SuccessorIds(id))
                if (--inDegree[successor] == 0)
                    queue.Enqueue(successor);
        }
        return order;
    }
}
