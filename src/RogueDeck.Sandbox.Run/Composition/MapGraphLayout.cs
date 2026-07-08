using RogueDeck.Run;

namespace RogueDeck.Sandbox.Composition;

// Resolves a 2D screen position for every map node, so the Studio can draw the graph as an SVG. A node with an
// authored coordinate (RunMap.Layout) keeps it; the rest are auto-placed in a layered layout — column = graph
// depth (longest path from an entry/root), row = order among same-depth nodes. Pure + UI-free so it is testable.
public static class MapGraphLayout
{
    public const int CellWidth = 170;
    public const int CellHeight = 84;
    public const int NodeWidth = 130;
    public const int NodeHeight = 46;
    private const int Margin = 12;

    // Top-left (X, Y) per node id.
    public static IReadOnlyDictionary<NodeId, (int X, int Y)> Resolve(RunMap map)
    {
        var authored = map.Layout.ToDictionary(l => l.Node, l => (l.X, l.Y));
        var depth = ComputeDepths(map);
        var rowCursor = new Dictionary<int, int>();
        var result = new Dictionary<NodeId, (int X, int Y)>();

        foreach (var node in map.Nodes)
        {
            if (authored.TryGetValue(node.Id, out var coord))
            {
                result[node.Id] = coord;
                continue;
            }
            var column = depth.GetValueOrDefault(node.Id);
            var row = rowCursor.GetValueOrDefault(column);
            rowCursor[column] = row + 1;
            result[node.Id] = (column * CellWidth + Margin, row * CellHeight + Margin);
        }
        return result;
    }

    // The SVG canvas size that fits every node, with a margin.
    public static (int Width, int Height) CanvasSize(IReadOnlyDictionary<NodeId, (int X, int Y)> positions)
    {
        if (positions.Count == 0)
            return (NodeWidth + 2 * Margin, NodeHeight + 2 * Margin);
        var maxX = positions.Values.Max(p => p.X) + NodeWidth + Margin;
        var maxY = positions.Values.Max(p => p.Y) + NodeHeight + Margin;
        return (maxX, maxY);
    }

    // Longest-path depth from a root/entry, by relaxing forward edges. Caps at node count, so a (malformed) cycle
    // does not loop forever — the validator flags cycles separately.
    private static IReadOnlyDictionary<NodeId, int> ComputeDepths(RunMap map)
    {
        var depth = map.Nodes.ToDictionary(node => node.Id, _ => 0);
        for (var pass = 0; pass < map.Nodes.Count; pass++)
        {
            var changed = false;
            foreach (var edge in map.Edges)
                if (depth.ContainsKey(edge.From) && depth.TryGetValue(edge.To, out var to)
                    && to < depth[edge.From] + 1)
                {
                    depth[edge.To] = depth[edge.From] + 1;
                    changed = true;
                }
            if (!changed)
                break;
        }
        return depth;
    }
}
