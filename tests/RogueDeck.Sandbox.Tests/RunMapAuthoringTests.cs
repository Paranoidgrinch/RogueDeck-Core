using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;

namespace RogueDeck.Sandbox.Tests;

// The Studio Run tab's pure map-edit operations: node add/remove/move + the branching surface (edges + entries).
// The invariant that matters most is that every edit PRESERVES the parts it does not touch (edges/entries survive
// a node reorder; a removed node's dangling edges/entries are cleaned up), which a naive rebuild would drop.
public class RunMapAuthoringTests
{
    private static readonly NodeType Mark = new("event");
    private static Node N(string id) => new(new NodeId(id), Mark, new EventRef(new EventId(id)));
    private static MapEdge E(string from, string to) => new(new NodeId(from), new NodeId(to));

    private static RunMap Fork() => new(new[] { N("a"), N("b"), N("c") })
    {
        Edges = [E("a", "b"), E("a", "c")],
        EntryNodeIds = [new NodeId("a")],
    };

    [Fact]
    public void AddNode_appends_and_keeps_edges_and_entries()
    {
        var map = RunMapAuthoring.AddNode(Fork(), N("d"));

        Assert.Equal(new[] { "a", "b", "c", "d" }, map.Nodes.Select(n => n.Id.Value));
        Assert.Equal(2, map.Edges.Count);                       // edges preserved
        Assert.Equal(new NodeId("a"), Assert.Single(map.EntryNodeIds));
    }

    [Fact]
    public void MoveNode_reorders_without_disturbing_edges_or_entries()
    {
        var map = RunMapAuthoring.MoveNode(Fork(), index: 1, direction: 1); // swap b and c

        Assert.Equal(new[] { "a", "c", "b" }, map.Nodes.Select(n => n.Id.Value));
        Assert.Contains(E("a", "b"), map.Edges);
        Assert.Contains(E("a", "c"), map.Edges);
        Assert.Equal(new NodeId("a"), Assert.Single(map.EntryNodeIds));
    }

    [Fact]
    public void RemoveNode_drops_the_node_and_every_edge_and_entry_touching_it()
    {
        var map = RunMapAuthoring.RemoveNode(Fork(), index: 0); // remove 'a', the entry + source of both edges

        Assert.Equal(new[] { "b", "c" }, map.Nodes.Select(n => n.Id.Value));
        Assert.Empty(map.Edges);          // both edges referenced 'a'
        Assert.Empty(map.EntryNodeIds);   // 'a' was the only entry
    }

    [Fact]
    public void AddEdge_adds_a_valid_edge_but_rejects_self_loops_duplicates_and_unknown_endpoints()
    {
        Assert.Contains(E("b", "c"), RunMapAuthoring.AddEdge(Fork(), new NodeId("b"), new NodeId("c")).Edges);

        Assert.Equal(2, RunMapAuthoring.AddEdge(Fork(), new NodeId("a"), new NodeId("a")).Edges.Count);   // self-loop
        Assert.Equal(2, RunMapAuthoring.AddEdge(Fork(), new NodeId("a"), new NodeId("b")).Edges.Count);   // duplicate
        Assert.Equal(2, RunMapAuthoring.AddEdge(Fork(), new NodeId("a"), new NodeId("ghost")).Edges.Count); // unknown
    }

    [Fact]
    public void RemoveEdge_removes_only_that_edge()
    {
        var map = RunMapAuthoring.RemoveEdge(Fork(), E("a", "c"));

        Assert.Equal(E("a", "b"), Assert.Single(map.Edges));
    }

    [Fact]
    public void ToggleEntry_adds_then_removes_an_entry()
    {
        var added = RunMapAuthoring.ToggleEntry(Fork(), new NodeId("b"));
        Assert.Equal(new[] { "a", "b" }, added.EntryNodeIds.Select(id => id.Value));

        var removed = RunMapAuthoring.ToggleEntry(added, new NodeId("a"));
        Assert.Equal(new[] { "b" }, removed.EntryNodeIds.Select(id => id.Value));
    }

    [Fact]
    public void ToggleEntry_ignores_an_unknown_node()
    {
        Assert.Equal(Fork().EntryNodeIds.Count, RunMapAuthoring.ToggleEntry(Fork(), new NodeId("ghost")).EntryNodeIds.Count);
    }
}
