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

    private static bool IsEnemy(MapNodeKind kind) => kind is MapNodeKind.Combat or MapNodeKind.Elite;

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
