namespace RogueDeck.Run;

// Structural validation for a branching (graph) run-map: node ids are unique, edge endpoints exist, declared
// entry nodes exist, the graph is a forward-only DAG (no cycles), and every node is reachable from an entry.
// Returns human-readable problems (empty = structurally sound), matching RunDocumentValidator's data-in/data-out
// style. A linear map (no edges) has no graph structure to check and always validates clean.
public static class RunMapValidator
{
    public static IReadOnlyList<string> Validate(RunMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var problems = new List<string>();
        if (map.Edges.Count == 0)
            return problems; // linear map: nothing graph-shaped to validate

        var ids = new HashSet<NodeId>();
        foreach (var node in map.Nodes)
            if (!ids.Add(node.Id))
                problems.Add($"Map: duplicate node id '{node.Id.Value}'.");

        foreach (var edge in map.Edges)
        {
            if (!ids.Contains(edge.From))
                problems.Add($"Map: edge references unknown source node '{edge.From.Value}'.");
            if (!ids.Contains(edge.To))
                problems.Add($"Map: edge references unknown target node '{edge.To.Value}'.");
        }

        foreach (var entry in map.EntryNodeIds)
            if (!ids.Contains(entry))
                problems.Add($"Map: entry node '{entry.Value}' does not exist.");

        var entries = (map.EntryNodeIds.Count > 0 ? map.EntryNodeIds : map.RootIds())
            .Where(ids.Contains)
            .ToList();
        if (entries.Count == 0)
            problems.Add(
                "Map: no entry node — the graph has no root (every node has an incoming edge, so it is cyclic).");

        if (HasCycle(map, out var cycleNode))
            problems.Add(
                $"Map: the graph has a cycle (a run map must be a forward-only DAG); node '{cycleNode}' is on it.");

        // Reachability is only meaningful on an acyclic graph with a real entry; a cycle already faults above.
        if (entries.Count > 0 && !HasCycle(map, out _))
        {
            var reachable = Reachable(map, entries);
            foreach (var node in map.Nodes)
                if (!reachable.Contains(node.Id))
                    problems.Add($"Map: node '{node.Id.Value}' is unreachable from any entry node.");
        }

        return problems;
    }

    private static ISet<NodeId> Reachable(RunMap map, IReadOnlyList<NodeId> entries)
    {
        var visited = new HashSet<NodeId>();
        var stack = new Stack<NodeId>(entries);
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (!visited.Add(id))
                continue;
            foreach (var successor in map.SuccessorIds(id))
                stack.Push(successor);
        }
        return visited;
    }

    // Classic DFS 3-colouring: a back-edge to a node still on the recursion stack (gray) is a cycle.
    private static bool HasCycle(RunMap map, out string cycleNode)
    {
        cycleNode = "";
        var color = new Dictionary<NodeId, int>(); // 0 = white/unseen, 1 = gray/on-stack, 2 = black/done
        foreach (var node in map.Nodes)
            if (Color(color, node.Id) == 0 && Visit(map, node.Id, color, out cycleNode))
                return true;
        return false;
    }

    private static bool Visit(RunMap map, NodeId id, Dictionary<NodeId, int> color, out string cycleNode)
    {
        cycleNode = "";
        color[id] = 1;
        foreach (var successor in map.SuccessorIds(id))
        {
            var c = Color(color, successor);
            if (c == 1)
            {
                cycleNode = successor.Value;
                return true;
            }
            if (c == 0 && Visit(map, successor, color, out cycleNode))
                return true;
        }
        color[id] = 2;
        return false;
    }

    private static int Color(Dictionary<NodeId, int> color, NodeId id) => color.GetValueOrDefault(id);
}
