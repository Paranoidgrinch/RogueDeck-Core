namespace RogueDeck.Run;

// WHAT THE WORST AND THE BEST COMPLETE ROUTE THROUGH A MAP ARE WORTH, for any numeric weight per room.
//
// A branching act's routes are not enumerable: Act IV of Bureaucrats & Broomsticks has 8 436 of them, and
// branchier maps are exponentially worse. But the two questions worth asking of a route — what is the LEAST
// this map can ask of a player, and the MOST — are a reverse-topological dynamic program in O(V+E):
//
//     best(node) = weight(node) + min (or max) over its successors
//
// with a leaf worth only itself, and the answer the min (or max) over the entry rooms. Every successor is solved
// before the room that depends on it, so one backwards pass settles the whole graph.
//
// This generalizes the count DP that MapConstraintValidator has always used for per-path minimums — "how many
// elites does the thinnest route hold" is this with a weight of 1 per elite and 0 per everything else — and that
// checker now runs on top of it rather than beside it, so there is one implementation of the traversal and not
// two that can drift. The generalization exists because the strategic map generator replaces exact per-role
// promises with an aggregate: a route must carry enough CHALLENGE (PathPressure), where an elite is worth more
// than a fight and a campfire is worth nothing, and no single role is promised at all.
//
// Assumes a DAG — which every generator's output is; check topology with RunMapValidator otherwise.
public static class WeightedPathEvaluator
{
    // The least a complete entry→leaf route through `map` is worth. `weight` is asked once per room; a room with
    // no role recorded is worth nothing (the generator's role map is the authority on what a room IS, and a node
    // it never placed cannot be scored).
    public static double MinimumPathScore(
        RunMap map, IReadOnlyDictionary<NodeId, MapNodeKind> roles, Func<NodeId, MapNodeKind, double> weight) =>
        Score(map, roles, weight, minimum: true);

    // The most a complete entry→leaf route is worth — the mirror image.
    public static double MaximumPathScore(
        RunMap map, IReadOnlyDictionary<NodeId, MapNodeKind> roles, Func<NodeId, MapNodeKind, double> weight) =>
        Score(map, roles, weight, minimum: false);

    private static double Score(
        RunMap map, IReadOnlyDictionary<NodeId, MapNodeKind> roles, Func<NodeId, MapNodeKind, double> weight,
        bool minimum)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(weight);

        var order = TopologicalOrder(map);
        var best = new Dictionary<NodeId, double>(order.Count);

        for (var index = order.Count - 1; index >= 0; index--)
        {
            var id = order[index];
            var self = roles.TryGetValue(id, out var kind) ? weight(id, kind) : 0d;

            var onward = minimum ? double.PositiveInfinity : double.NegativeInfinity;
            foreach (var successor in map.SuccessorIds(id))
                onward = minimum ? Math.Min(onward, best[successor]) : Math.Max(onward, best[successor]);

            best[id] = double.IsInfinity(onward) ? self : self + onward; // no successors ⇒ a leaf, worth itself
        }

        var answer = minimum ? double.PositiveInfinity : double.NegativeInfinity;
        foreach (var entry in EntryNodes(map))
            answer = minimum
                ? Math.Min(answer, best.GetValueOrDefault(entry))
                : Math.Max(answer, best.GetValueOrDefault(entry));

        // A map with no entry rooms at all scores nothing rather than infinity: an empty question has an empty
        // answer, and handing a caller ±∞ to compare against a threshold would silently pass or fail everything.
        return double.IsInfinity(answer) ? 0d : answer;
    }

    // Where a walk may begin: the map's declared entries, or every room nothing leads into.
    internal static IEnumerable<NodeId> EntryNodes(RunMap map) =>
        map.EntryNodeIds.Count > 0 ? map.EntryNodeIds : map.RootIds();

    // Kahn's algorithm: rooms ordered so that every room precedes its successors. Shared with
    // MapConstraintValidator — the order a backwards pass needs is the same order whatever it is summing.
    internal static IReadOnlyList<NodeId> TopologicalOrder(RunMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
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
