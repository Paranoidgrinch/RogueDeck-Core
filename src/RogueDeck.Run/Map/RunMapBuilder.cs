namespace RogueDeck.Run;

// Fluent assembly for a graph (branching) run-map: add nodes, wire directed edges, name entry nodes — without
// hand-writing MapEdge lists. Guards duplicate node ids at add time; Build() emits the RunMap (run RunMapValidator
// on it to check DAG/reachability). Used by the layered act generator and by hand-authored worked examples.
public sealed class RunMapBuilder
{
    private readonly List<Node> _nodes = new();
    private readonly List<MapEdge> _edges = new();
    private readonly List<NodeId> _entries = new();
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);

    public RunMapBuilder AddNode(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!_ids.Add(node.Id.Value))
            throw new InvalidOperationException($"Duplicate map node id '{node.Id.Value}'.");
        _nodes.Add(node);
        return this;
    }

    public RunMapBuilder AddNode(NodeId id, NodeType type, object payload) =>
        AddNode(new Node(id, type, payload));

    public RunMapBuilder Connect(NodeId from, NodeId to)
    {
        _edges.Add(new MapEdge(from, to));
        return this;
    }

    public RunMapBuilder Connect(string from, string to) => Connect(new NodeId(from), new NodeId(to));

    public RunMapBuilder Entry(NodeId id)
    {
        _entries.Add(id);
        return this;
    }

    public RunMapBuilder Entry(string id) => Entry(new NodeId(id));

    public bool HasNode(NodeId id) => _ids.Contains(id.Value);

    public RunMap Build() =>
        new(_nodes.ToList()) { Edges = _edges.ToList(), EntryNodeIds = _entries.ToList() };
}
