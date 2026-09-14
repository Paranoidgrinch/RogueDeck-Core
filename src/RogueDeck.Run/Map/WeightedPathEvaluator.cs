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
// The traversal itself knows nothing about RunMap: a graph here is a set of rooms, a successor function and a
// set of entries, because the strategic generator asks the same question of a StrategicTopology — rows, slots and
// edges, with no RunMap anywhere near it — long before S11 realizes one. Two implementations of a DP that must
// agree to the unit is exactly the drift S2 removed from MapConstraintValidator, so the RunMap overloads below
// are wrappers over the general pass and not copies of it.
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

    // BOTH ENDS AND THE ROUTES THEMSELVES, over any DAG, in one backwards pass.
    //
    // A caller that has both numbers has usually earned the right to ask the next question — WHICH route is the
    // thin one — and that answer costs one remembered successor per room rather than a second traversal. It is
    // what turns "this act's worst route is worth 9, and 11 was promised" into something a repair can act on.
    public static PathScores Score(
        IReadOnlyCollection<NodeId> nodes,
        Func<NodeId, IReadOnlyList<NodeId>> successors,
        IReadOnlyCollection<NodeId> entries,
        Func<NodeId, double> weight)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(successors);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(weight);

        var order = TopologicalOrder(nodes, successors);
        var thin = new Dictionary<NodeId, double>(order.Count);
        var rich = new Dictionary<NodeId, double>(order.Count);
        var thinNext = new Dictionary<NodeId, NodeId>();
        var richNext = new Dictionary<NodeId, NodeId>();

        for (var index = order.Count - 1; index >= 0; index--)
        {
            var id = order[index];
            var self = weight(id);
            var least = double.PositiveInfinity;
            var most = double.NegativeInfinity;

            foreach (var successor in successors(id))
            {
                if (!thin.TryGetValue(successor, out var onward))
                    continue; // an edge into a room this graph does not hold, or a cycle: see the header
                if (onward < least)
                {
                    least = onward;
                    thinNext[id] = successor;
                }
                if (rich[successor] > most)
                {
                    most = rich[successor];
                    richNext[id] = successor;
                }
            }

            thin[id] = double.IsInfinity(least) ? self : self + least; // no successors ⇒ a leaf, worth itself
            rich[id] = double.IsInfinity(most) ? self : self + most;
        }

        var lowest = double.PositiveInfinity;
        var highest = double.NegativeInfinity;
        NodeId? thinnest = null;
        NodeId? richest = null;
        foreach (var entry in entries)
        {
            if (thin.TryGetValue(entry, out var least) && least < lowest)
            {
                lowest = least;
                thinnest = entry;
            }
            if (rich.TryGetValue(entry, out var most) && most > highest)
            {
                highest = most;
                richest = entry;
            }
        }

        // A map with no entry rooms at all scores nothing rather than infinity: an empty question has an empty
        // answer, and handing a caller ±∞ to compare against a threshold would silently pass or fail everything.
        return new PathScores
        {
            Minimum = double.IsInfinity(lowest) ? 0d : lowest,
            Maximum = double.IsInfinity(highest) ? 0d : highest,
            ThinnestRoute = Walk(thinnest, thinNext),
            RichestRoute = Walk(richest, richNext),
        };
    }

    // The rooms of one extreme route, from its entry to the leaf the DP chose. Ties take the successor met first,
    // which is the order the graph lists its edges in — deterministic, and the same map twice is the same route.
    private static IReadOnlyList<NodeId> Walk(NodeId? entry, IReadOnlyDictionary<NodeId, NodeId> next)
    {
        if (entry is null)
            return [];
        var route = new List<NodeId> { entry.Value };
        var current = entry.Value;
        while (next.TryGetValue(current, out var onward))
        {
            route.Add(onward);
            current = onward;
        }
        return route;
    }

    // The RunMap overloads are this graph, described the way a finished map describes itself: its rooms, its
    // successors, its entries, and a weight that reads the role map rather than the room.
    private static double Score(
        RunMap map, IReadOnlyDictionary<NodeId, MapNodeKind> roles, Func<NodeId, MapNodeKind, double> weight,
        bool minimum)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(weight);

        var scores = Score(
            map.Nodes.Select(node => node.Id).ToList(),
            map.SuccessorIds,
            EntryNodes(map).ToList(),
            id => roles.TryGetValue(id, out var kind) ? weight(id, kind) : 0d);
        return minimum ? scores.Minimum : scores.Maximum;
    }

    // Where a walk may begin: the map's declared entries, or every room nothing leads into.
    internal static IEnumerable<NodeId> EntryNodes(RunMap map) =>
        map.EntryNodeIds.Count > 0 ? map.EntryNodeIds : map.RootIds();

    // Kahn's algorithm: rooms ordered so that every room precedes its successors. Shared with
    // MapConstraintValidator — the order a backwards pass needs is the same order whatever it is summing.
    internal static IReadOnlyList<NodeId> TopologicalOrder(RunMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return TopologicalOrder(map.Nodes.Select(node => node.Id).ToList(), map.SuccessorIds);
    }

    internal static IReadOnlyList<NodeId> TopologicalOrder(
        IReadOnlyCollection<NodeId> nodes, Func<NodeId, IReadOnlyList<NodeId>> successors)
    {
        var inDegree = new Dictionary<NodeId, int>(nodes.Count);
        foreach (var id in nodes)
            inDegree.TryAdd(id, 0);
        foreach (var id in nodes)
            foreach (var successor in successors(id))
                if (inDegree.ContainsKey(successor))
                    inDegree[successor]++;

        var queue = new Queue<NodeId>();
        foreach (var id in nodes)
            if (inDegree[id] == 0)
                queue.Enqueue(id);

        var order = new List<NodeId>();
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            order.Add(id);
            foreach (var successor in successors(id))
                if (inDegree.ContainsKey(successor) && --inDegree[successor] == 0)
                    queue.Enqueue(successor);
        }
        return order;
    }
}

// THE TWO EXTREME ROUTES THROUGH A MAP, and what each is worth. `ThinnestRoute` is one route achieving
// `Minimum` — there can be several, and which one is reported is deterministic rather than meaningful.
public sealed record PathScores
{
    public required double Minimum { get; init; }
    public required double Maximum { get; init; }
    public required IReadOnlyList<NodeId> ThinnestRoute { get; init; }
    public required IReadOnlyList<NodeId> RichestRoute { get; init; }
}
