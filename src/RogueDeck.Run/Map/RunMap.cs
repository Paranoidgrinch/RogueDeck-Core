namespace RogueDeck.Run;

// One stop on the run. Type selects the resolver; Payload carries whatever that resolver needs (a combat
// description, an event script, …). Payload is intentionally untyped so new node kinds need no core change.
public sealed class Node
{
    public NodeId Id { get; }
    public NodeType Type { get; }
    public object Payload { get; }

    public Node(NodeId id, NodeType type, object payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        Id = id;
        Type = type;
        Payload = payload;
    }
}

// A directed edge in the run map's graph: after finishing `From`, `To` becomes a reachable next node. Only
// meaningful when a map declares edges — an edge-less map is linear (its nodes are walked in order). B0 adds the
// data; traversal that reads it arrives in B1. A readonly record struct so it round-trips through RunJson for free.
public readonly record struct MapEdge(NodeId From, NodeId To);

// Optional 2D screen coordinate for a node — purely presentational, so a map UI can draw the graph where the
// author placed it. Traversal/validation ignore it entirely (like a combatant's grid position vs. the fight). A
// list entry per placed node; unplaced nodes are auto-laid-out by the UI.
public sealed record NodeLayout(NodeId Node, int X, int Y);

// The shape of a run. Two shapes over one type:
//   • Linear (Edges empty, the default): the nodes are an ordered list walked start-to-finish — today's behavior,
//     byte-for-byte unchanged.
//   • Graph (Edges present): the nodes form a directed graph; the player walks it by choosing among the reachable
//     successors of the current node. `EntryNodeIds` names where a walk may begin (empty ⇒ derive it — the first
//     node / all in-degree-0 nodes). Both Edges and EntryNodeIds are additive, opt-in overlays: a map that sets
//     neither is exactly today's linear map.
public sealed class RunMap
{
    public IReadOnlyList<Node> Nodes { get; }

    // The graph's directed edges. Empty ⇒ a linear map (nodes walked in order). Additive; init-only so existing
    // `new RunMap(nodes)` call sites and old serialized maps (no `edges`) stay linear and unchanged.
    public IReadOnlyList<MapEdge> Edges { get; init; } = [];

    // Where a graph walk may start. Empty ⇒ the traversal derives entry nodes. Ignored by a linear map.
    public IReadOnlyList<NodeId> EntryNodeIds { get; init; } = [];

    // Optional 2D screen coordinates for the map UI (one entry per placed node). Purely presentational — traversal
    // and validation never read it. Empty ⇒ the UI auto-lays-out the graph. Additive/init-only, round-trips as data.
    public IReadOnlyList<NodeLayout> Layout { get; init; } = [];

    public RunMap(IReadOnlyList<Node> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        Nodes = nodes;
    }

    // ── Graph queries (pure; used by the runner's graph walk, the map validator, and a map UI) ────────────────

    // The distinct successors of `from` in edge-declaration order — the nodes an edge leads to from `from`.
    public IReadOnlyList<NodeId> SuccessorIds(NodeId from) =>
        Edges.Where(edge => edge.From == from).Select(edge => edge.To).Distinct().ToList();

    // The roots of the graph: nodes with no incoming edge. On a graph with no declared EntryNodeIds these are where
    // a walk may begin. (A linear map, with no edges, treats every node as a root — callers guard on Edges first.)
    public IReadOnlyList<NodeId> RootIds()
    {
        var hasIncoming = Edges.Select(edge => edge.To).ToHashSet();
        return Nodes.Where(node => !hasIncoming.Contains(node.Id)).Select(node => node.Id).ToList();
    }

    public bool TryGetNode(NodeId id, out Node? node)
    {
        node = Nodes.FirstOrDefault(candidate => candidate.Id == id);
        return node is not null;
    }
}
