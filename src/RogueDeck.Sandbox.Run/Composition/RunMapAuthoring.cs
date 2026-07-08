using RogueDeck.Run;

namespace RogueDeck.Sandbox.Composition;

// Pure map-edit operations for the Studio Run tab: node add/remove/move plus the branching-map surface (edges +
// entry nodes). Each returns a NEW RunMap, preserving the parts it does not touch — critically the Edges and
// EntryNodeIds, which a naive `new RunMap(nodes)` would silently drop. Removing a node also drops every edge and
// entry that referenced it, so the graph stays consistent. Kept pure + UI-free so it is unit-testable and the
// razor component stays a thin lens over it.
public static class RunMapAuthoring
{
    public static RunMap AddNode(RunMap map, Node node) =>
        Make([.. map.Nodes, node], map.Edges, map.EntryNodeIds, map.Layout);

    public static RunMap RemoveNode(RunMap map, int index)
    {
        if (index < 0 || index >= map.Nodes.Count)
            return map;
        var id = map.Nodes[index].Id;
        return Make(
            map.Nodes.Where((_, i) => i != index).ToList(),
            map.Edges.Where(edge => edge.From != id && edge.To != id).ToList(),
            map.EntryNodeIds.Where(entry => entry != id).ToList(),
            map.Layout.Where(l => l.Node != id).ToList());
    }

    public static RunMap MoveNode(RunMap map, int index, int direction)
    {
        var target = index + direction;
        if (index < 0 || index >= map.Nodes.Count || target < 0 || target >= map.Nodes.Count)
            return map;
        var nodes = map.Nodes.ToList();
        (nodes[index], nodes[target]) = (nodes[target], nodes[index]);
        return Make(nodes, map.Edges, map.EntryNodeIds, map.Layout);
    }

    // Sets (or moves) a node's presentational 2D coordinate for the map UI; a no-op for an unknown node.
    public static RunMap SetNodeLayout(RunMap map, NodeId id, int x, int y)
    {
        if (!HasNode(map, id))
            return map;
        var layout = map.Layout.Where(l => l.Node != id).Append(new NodeLayout(id, x, y)).ToList();
        return Make(map.Nodes, map.Edges, map.EntryNodeIds, layout);
    }

    // Adds a directed edge, guarding against self-loops, duplicates, and endpoints that are not real nodes.
    public static RunMap AddEdge(RunMap map, NodeId from, NodeId to)
    {
        var edge = new MapEdge(from, to);
        if (from == to || map.Edges.Contains(edge) || !HasNode(map, from) || !HasNode(map, to))
            return map;
        return Make(map.Nodes, [.. map.Edges, edge], map.EntryNodeIds, map.Layout);
    }

    public static RunMap RemoveEdge(RunMap map, MapEdge edge) =>
        Make(map.Nodes, map.Edges.Where(existing => existing != edge).ToList(), map.EntryNodeIds, map.Layout);

    // Marks/unmarks a node as an entry (where a graph walk may begin). No declared entries ⇒ the runner derives
    // them (roots), so clearing all entries is valid.
    public static RunMap ToggleEntry(RunMap map, NodeId id)
    {
        if (!HasNode(map, id))
            return map;
        var entries = map.EntryNodeIds.Contains(id)
            ? map.EntryNodeIds.Where(entry => entry != id).ToList()
            : [.. map.EntryNodeIds, id];
        return Make(map.Nodes, map.Edges, entries, map.Layout);
    }

    private static bool HasNode(RunMap map, NodeId id) => map.Nodes.Any(node => node.Id == id);

    private static RunMap Make(
        IReadOnlyList<Node> nodes, IReadOnlyList<MapEdge> edges,
        IReadOnlyList<NodeId> entries, IReadOnlyList<NodeLayout> layout) =>
        new(nodes) { Edges = edges, EntryNodeIds = entries, Layout = layout };
}
