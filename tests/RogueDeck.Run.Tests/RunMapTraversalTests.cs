using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// B1 (branching run-map, graph traversal): RunRunner walks a graph map by player-chosen path — start at an entry
// node, resolve it, offer reachable-unvisited successors, pick one (or auto-advance on a single successor), end at
// a leaf. A linear map (no edges) keeps the exact index loop. Driven with a recording test resolver so the route
// is observable without real combat.
public class RunMapTraversalTests
{
    private static readonly NodeType Mark = new("test.mark");

    // Records the id of every node it resolves (in order), and optionally drains the hero to force a defeat.
    private sealed class RecordingResolver : INodeResolver
    {
        private readonly int _lethalOn;
        public List<string> Entered { get; } = new();
        public NodeType NodeType => Mark;

        public RecordingResolver(int lethalOn = -1) => _lethalOn = lethalOn;

        public NodeOutcome Resolve(NodeResolveContext context, Node node)
        {
            Entered.Add(node.Id.Value);
            if (Entered.Count == _lethalOn)
                context.Run.Health.SetCurrent(0);
            return new NodeOutcome($"entered {node.Id}");
        }
    }

    private static Node N(string id) => new(new NodeId(id), Mark, "payload");

    private static RunDefinitionRegistry Registry(RecordingResolver resolver)
    {
        var builder = new RunDefinitionRegistryBuilder();
        builder.RegisterResolver(resolver);
        return builder.Build();
    }

    private static RunState Run(RunMap map) =>
        new(new RunId("run"), new HealthState(30, 30), map);

    // start ─┬─> left ──┐
    //        └─> right ─┴─> boss   (a diamond: two routes reconverge at the boss)
    private static RunMap Diamond() => new(new[] { N("start"), N("left"), N("right"), N("boss") })
    {
        EntryNodeIds = [new NodeId("start")],
        Edges =
        [
            new MapEdge(new NodeId("start"), new NodeId("left")),
            new MapEdge(new NodeId("start"), new NodeId("right")),
            new MapEdge(new NodeId("left"), new NodeId("boss")),
            new MapEdge(new NodeId("right"), new NodeId("boss")),
        ],
    };

    [Fact]
    public void Graph_walks_only_the_chosen_route_leaving_the_unchosen_branch_unvisited()
    {
        var resolver = new RecordingResolver();
        var run = Run(Diamond());

        new RunRunner(Registry(resolver), new ScriptedChoiceProvider("left")).Run(run);

        Assert.Equal(new[] { "start", "left", "boss" }, resolver.Entered);
        Assert.DoesNotContain(new NodeId("right"), run.VisitedNodes); // the unchosen branch is never forced
        Assert.Equal(RunResult.Victory, run.Result);
        Assert.Equal(new NodeId("boss"), run.CurrentNodeId);
    }

    [Fact]
    public void Graph_routes_the_other_branch_when_the_player_picks_it()
    {
        var resolver = new RecordingResolver();
        var run = Run(Diamond());

        new RunRunner(Registry(resolver), new ScriptedChoiceProvider("right")).Run(run);

        Assert.Equal(new[] { "start", "right", "boss" }, resolver.Entered);
        Assert.DoesNotContain(new NodeId("left"), run.VisitedNodes);
    }

    [Fact]
    public void Graph_auto_advances_through_single_successors_and_ends_at_a_leaf()
    {
        var resolver = new RecordingResolver();
        // a -> b -> c, each with exactly one successor; no scripted choices needed.
        var map = new RunMap(new[] { N("a"), N("b"), N("c") })
        {
            Edges = [new MapEdge(new NodeId("a"), new NodeId("b")), new MapEdge(new NodeId("b"), new NodeId("c"))],
        };
        var run = Run(map);

        new RunRunner(Registry(resolver), new ScriptedChoiceProvider()).Run(run);

        Assert.Equal(new[] { "a", "b", "c" }, resolver.Entered); // a is the derived root (no incoming edge)
    }

    [Fact]
    public void Graph_picks_the_entry_node_when_several_are_declared()
    {
        var resolver = new RecordingResolver();
        var map = new RunMap(new[] { N("e1"), N("e2"), N("boss") })
        {
            EntryNodeIds = [new NodeId("e1"), new NodeId("e2")],
            Edges = [new MapEdge(new NodeId("e1"), new NodeId("boss")), new MapEdge(new NodeId("e2"), new NodeId("boss"))],
        };
        var run = Run(map);

        new RunRunner(Registry(resolver), new ScriptedChoiceProvider("e2")).Run(run);

        Assert.Equal(new[] { "e2", "boss" }, resolver.Entered);
    }

    [Fact]
    public void Graph_defeat_stops_the_walk_before_the_next_node()
    {
        var resolver = new RecordingResolver(lethalOn: 1); // the hero dies resolving the first node
        var map = new RunMap(new[] { N("a"), N("b") })
        {
            Edges = [new MapEdge(new NodeId("a"), new NodeId("b"))],
        };
        var run = Run(map);

        new RunRunner(Registry(resolver), new ScriptedChoiceProvider()).Run(run);

        Assert.Equal(new[] { "a" }, resolver.Entered); // b is never reached
        Assert.Equal(RunResult.Defeat, run.Result);
    }

    [Fact]
    public void Graph_raises_a_NodeChosen_event_per_walked_node()
    {
        var resolver = new RecordingResolver();
        var run = Run(Diamond());

        new RunRunner(Registry(resolver), new ScriptedChoiceProvider("left")).Run(run);

        var chosen = run.EventHistory.OfType<NodeChosenRunEvent>().Select(e => e.NodeId.Value).ToArray();
        Assert.Equal(new[] { "start", "left", "boss" }, chosen);
    }

    [Fact]
    public void Linear_map_ignores_the_graph_traversal_and_raises_no_NodeChosen()
    {
        var resolver = new RecordingResolver();
        var map = new RunMap(new[] { N("a"), N("b") }); // no edges ⇒ linear
        var run = Run(map);

        new RunRunner(Registry(resolver), new ScriptedChoiceProvider()).Run(run);

        Assert.Equal(new[] { "a", "b" }, resolver.Entered);
        Assert.Empty(run.VisitedNodes);                 // graph-only tracking stays untouched
        Assert.Null(run.CurrentNodeId);
        Assert.Empty(run.EventHistory.OfType<NodeChosenRunEvent>());
    }
}
