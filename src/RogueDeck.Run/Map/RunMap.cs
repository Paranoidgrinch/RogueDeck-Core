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

// The shape of a run. For this slice it is a linear, ordered list of nodes; a branching graph (nodes +
// edges + selection) is a later slice and slots in behind this same type.
public sealed class RunMap
{
    public IReadOnlyList<Node> Nodes { get; }

    public RunMap(IReadOnlyList<Node> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        Nodes = nodes;
    }
}
