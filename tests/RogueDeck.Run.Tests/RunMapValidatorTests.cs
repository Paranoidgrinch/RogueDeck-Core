using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// B2 (branching run-map, reachability & rules): RunMapValidator enforces the graph is a forward-only DAG with
// valid endpoints and full reachability; RunState.CurrentReachableNodes surfaces the current fork for a UI.
public class RunMapValidatorTests
{
    private static readonly NodeType Mark = new("test.mark");
    private static Node N(string id) => new(new NodeId(id), Mark, "payload");
    private static MapEdge E(string from, string to) => new(new NodeId(from), new NodeId(to));

    private static RunMap Diamond() => new(new[] { N("start"), N("left"), N("right"), N("boss") })
    {
        EntryNodeIds = [new NodeId("start")],
        Edges = [E("start", "left"), E("start", "right"), E("left", "boss"), E("right", "boss")],
    };

    // ── Validator ─────────────────────────────────────────────────────────────

    [Fact]
    public void A_well_formed_graph_validates_clean()
    {
        Assert.Empty(RunMapValidator.Validate(Diamond()));
    }

    [Fact]
    public void A_linear_map_has_no_graph_structure_to_validate()
    {
        Assert.Empty(RunMapValidator.Validate(new RunMap(new[] { N("a"), N("b") })));
    }

    [Fact]
    public void A_cycle_is_reported()
    {
        var map = new RunMap(new[] { N("a"), N("b"), N("c") })
        {
            Edges = [E("a", "b"), E("b", "c"), E("c", "a")], // c loops back to a
        };

        var problems = RunMapValidator.Validate(map);

        Assert.Contains(problems, p => p.Contains("cycle"));
    }

    [Fact]
    public void An_edge_to_a_nonexistent_node_is_reported()
    {
        var map = new RunMap(new[] { N("a") }) { Edges = [E("a", "ghost")] };

        Assert.Contains(RunMapValidator.Validate(map), p => p.Contains("unknown target node 'ghost'"));
    }

    [Fact]
    public void A_declared_entry_that_does_not_exist_is_reported()
    {
        var map = new RunMap(new[] { N("a"), N("b") })
        {
            EntryNodeIds = [new NodeId("missing")],
            Edges = [E("a", "b")],
        };

        Assert.Contains(RunMapValidator.Validate(map), p => p.Contains("entry node 'missing' does not exist"));
    }

    [Fact]
    public void A_node_unreachable_from_any_entry_is_reported()
    {
        // island has no path from start; it is a dangling extra node.
        var map = new RunMap(new[] { N("start"), N("boss"), N("island") })
        {
            EntryNodeIds = [new NodeId("start")],
            Edges = [E("start", "boss")],
        };

        Assert.Contains(RunMapValidator.Validate(map), p => p.Contains("'island' is unreachable"));
    }

    // ── RunState.CurrentReachableNodes (the fork a UI renders) ──────────────────

    private static RunState RunAt(RunMap map, params string[] visitInOrder)
    {
        var run = new RunState(new RunId("run"), new HealthState(30, 30), map);
        foreach (var id in visitInOrder)
            run.AdvanceToNode(new NodeId(id));
        return run;
    }

    [Fact]
    public void CurrentReachableNodes_lists_the_unvisited_successors_of_the_current_node()
    {
        var run = RunAt(Diamond(), "start");

        Assert.Equal(new[] { "left", "right" }, run.CurrentReachableNodes().Select(n => n.Id.Value));
    }

    [Fact]
    public void CurrentReachableNodes_excludes_already_visited_successors()
    {
        var map = new RunMap(new[] { N("start"), N("a"), N("b") })
        {
            Edges = [E("start", "a"), E("start", "b")],
        };
        var run = RunAt(map, "a", "start"); // a already walked, now back at start (contrived, exercises the filter)

        Assert.Equal(new[] { "b" }, run.CurrentReachableNodes().Select(n => n.Id.Value));
    }

    [Fact]
    public void CurrentReachableNodes_is_empty_at_a_leaf_and_on_a_linear_map()
    {
        Assert.Empty(RunAt(Diamond(), "start", "left", "boss").CurrentReachableNodes()); // boss is a leaf
        Assert.Empty(RunAt(new RunMap(new[] { N("a") })).CurrentReachableNodes());       // no edges + no current
    }
}
