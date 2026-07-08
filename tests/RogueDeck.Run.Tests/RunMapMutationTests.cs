using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// B5 (branching run-map, mutation): content can reshape the map mid-run via serializable effects — open a hidden
// path, collapse a bridge, splice in a node. Mutation goes through RunState (RunMap stays immutable, rebuilt); the
// graph walk reads the map fresh each step, so a change takes effect on the next fork.
public class RunMapMutationTests
{
    private static readonly NodeType Mark = new("test.mark");
    private static Node N(string id) => new(new NodeId(id), Mark, "payload");
    private static MapEdge E(string from, string to) => new(new NodeId(from), new NodeId(to));

    private static RunState RunWith(RunMap map) => new(new RunId("run"), new HealthState(30, 30), map);

    private static RunMap Fork() => new(new[] { N("start"), N("a"), N("b"), N("boss") })
    {
        EntryNodeIds = [new NodeId("start")],
        Edges = [E("start", "a"), E("start", "b"), E("a", "boss"), E("b", "boss")],
    };

    // ── RunState mutation methods ───────────────────────────────────────────────

    [Fact]
    public void AddMapEdge_and_AddMapNode_extend_the_graph()
    {
        var run = RunWith(Fork());

        Assert.True(run.AddMapNode(N("secret")));
        Assert.True(run.AddMapEdge(new NodeId("start"), new NodeId("secret")));

        Assert.Contains(run.Map.Nodes, n => n.Id.Value == "secret");
        Assert.Contains(new MapEdge(new NodeId("start"), new NodeId("secret")), run.Map.Edges);
    }

    [Fact]
    public void Adding_a_duplicate_node_or_edge_is_a_no_op()
    {
        var run = RunWith(Fork());

        Assert.False(run.AddMapNode(N("a")));                                  // node already present
        Assert.False(run.AddMapEdge(new NodeId("start"), new NodeId("a")));    // edge already present
    }

    [Fact]
    public void RemoveMapNode_also_drops_every_edge_and_entry_that_touched_it()
    {
        var run = RunWith(Fork());

        Assert.True(run.RemoveMapNode(new NodeId("a")));

        Assert.DoesNotContain(run.Map.Nodes, n => n.Id.Value == "a");
        Assert.DoesNotContain(run.Map.Edges, e => e.From.Value == "a" || e.To.Value == "a");
        // 'b' route is intact, so the map is still a valid DAG from start to boss.
        Assert.Empty(RunMapValidator.Validate(run.Map));
    }

    [Fact]
    public void RemoveMapEdge_removes_only_that_edge()
    {
        var run = RunWith(Fork());

        Assert.True(run.RemoveMapEdge(new NodeId("start"), new NodeId("b")));
        Assert.False(run.RemoveMapEdge(new NodeId("start"), new NodeId("b"))); // already gone

        Assert.DoesNotContain(E("start", "b"), run.Map.Edges);
        Assert.Contains(E("start", "a"), run.Map.Edges);
    }

    // ── End-to-end through RunRunner ────────────────────────────────────────────

    private sealed class RecordingResolver : INodeResolver
    {
        public List<string> Entered { get; } = new();
        public NodeType NodeType => Mark;
        public NodeOutcome Resolve(NodeResolveContext context, Node node)
        {
            Entered.Add(node.Id.Value);
            // The 'start' node opens a hidden path the first time it resolves.
            if (node.Id.Value == "start")
                context.Run.EnqueueEffect(new AddMapEdgeRunEffect(new NodeId("start"), new NodeId("secret")));
            return new NodeOutcome(node.Id.Value);
        }
    }

    private static RunDefinitionRegistry Registry(INodeResolver resolver)
    {
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder); // for the map-mutation effect handlers
        builder.RegisterResolver(resolver);
        return builder.Build();
    }

    [Fact]
    public void A_mid_run_add_edge_opens_a_previously_unreachable_node()
    {
        // 'secret' starts unreachable from 'start'; resolving 'start' adds the edge, so the fork now offers it.
        var map = new RunMap(new[] { N("start"), N("boss"), N("secret") })
        {
            EntryNodeIds = [new NodeId("start")],
            Edges = [E("start", "boss"), E("secret", "boss")],
        };
        var run = RunWith(map);

        new RunRunner(Registry(new RecordingResolver()), new ScriptedChoiceProvider("secret")).Run(run);

        Assert.Contains(new NodeId("secret"), run.VisitedNodes); // the opened path was walkable
        Assert.Equal(RunResult.Victory, run.Result);
    }

    private sealed class CollapsingResolver : INodeResolver
    {
        public NodeType NodeType => Mark;
        public NodeOutcome Resolve(NodeResolveContext context, Node node)
        {
            if (node.Id.Value == "start")
                context.Run.EnqueueEffect(new RemoveMapEdgeRunEffect(new NodeId("start"), new NodeId("b")));
            return new NodeOutcome(node.Id.Value);
        }
    }

    [Fact]
    public void A_mid_run_remove_edge_collapses_a_route_before_the_fork()
    {
        var run = RunWith(Fork());

        // Resolving 'start' collapses start->b, so the only remaining successor is 'a' (auto-advanced).
        new RunRunner(Registry(new CollapsingResolver()), new ScriptedChoiceProvider()).Run(run);

        Assert.Contains(new NodeId("a"), run.VisitedNodes);
        Assert.DoesNotContain(new NodeId("b"), run.VisitedNodes); // the collapsed route is never walked
        Assert.Contains(StandardRunLogTypes.MapChanged, run.Log.Select(l => l.Type));
    }

    // ── Serialization ───────────────────────────────────────────────────────────

    [Fact]
    public void The_map_mutation_effects_round_trip_as_json()
    {
        var options = RunJson.CreateOptions();
        IRunEffectRequest[] effects =
        [
            new AddMapNodeRunEffect(new Node(new NodeId("secret"), StandardRunIds.EventNode, new EventRef(new EventId("shrine")))),
            new RemoveMapNodeRunEffect(new NodeId("old")),
            new AddMapEdgeRunEffect(new NodeId("a"), new NodeId("b")),
            new RemoveMapEdgeRunEffect(new NodeId("a"), new NodeId("b")),
        ];

        foreach (var effect in effects)
        {
            var json = RunJson.ToJson(effect, options);
            var back = RunJson.FromJson<IRunEffectRequest>(json, options);
            Assert.Equal(json, RunJson.ToJson(back, options));
            Assert.Equal(effect.GetType(), back.GetType());
        }
    }
}
