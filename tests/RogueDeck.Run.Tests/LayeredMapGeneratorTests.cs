using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// B3 (branching run-map, generation): LayeredMapGenerator emits a deterministic Slay-the-Spire-style act — a valid
// DAG of rows feeding a single boss leaf, with kinds distributed — and RunMapBuilder assembles graph maps by hand.
public class LayeredMapGeneratorTests
{
    private static readonly NodeType Mark = new("test.mark");

    // A content delegate that realizes every kind as the same test node type, so tests can walk the topology
    // without real combat/event content.
    private static NodeContent AsMark(MapNodeKind kind, MapCoord coord) => new(Mark, "payload");

    private static RunMap Act(int seed, int rows = 5) =>
        LayeredMapGenerator.Generate(new LayeredMapSpec(rows, MinWidth: 2, MaxWidth: 4), seed, AsMark);

    // ── Structural validity ────────────────────────────────────────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    [InlineData(1000)]
    public void A_generated_act_is_a_valid_dag(int seed)
    {
        Assert.Empty(RunMapValidator.Validate(Act(seed)));
    }

    [Fact]
    public void The_entry_row_is_the_roots_and_the_boss_is_a_single_leaf()
    {
        var map = Act(seed: 3, rows: 4);

        // Entry nodes are exactly row 0.
        Assert.All(map.EntryNodeIds, id => Assert.StartsWith("r0c", id.Value));
        Assert.NotEmpty(map.EntryNodeIds);

        // Exactly one boss node (row 4), and it is a leaf (no outgoing edges).
        var boss = Assert.Single(map.Nodes, n => n.Id.Value.StartsWith("r4c"));
        Assert.Empty(map.SuccessorIds(boss.Id));
        Assert.Equal("r4c0", boss.Id.Value);
    }

    [Fact]
    public void Every_node_has_a_path_to_the_boss()
    {
        var map = Act(seed: 11, rows: 5);
        var boss = new NodeId("r5c0");

        foreach (var node in map.Nodes)
            Assert.True(Reaches(map, node.Id, boss), $"node '{node.Id.Value}' has no path to the boss");
    }

    private static bool Reaches(RunMap map, NodeId from, NodeId target)
    {
        var seen = new HashSet<NodeId>();
        var stack = new Stack<NodeId>();
        stack.Push(from);
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (id == target)
                return true;
            if (!seen.Add(id))
                continue;
            foreach (var s in map.SuccessorIds(id))
                stack.Push(s);
        }
        return false;
    }

    // ── Kind distribution ────────────────────────────────────────────────────

    [Fact]
    public void Kinds_follow_the_row_rules()
    {
        var kinds = new Dictionary<MapCoord, MapNodeKind>();
        LayeredMapGenerator.Generate(new LayeredMapSpec(5), seed: 9, (kind, coord) =>
        {
            kinds[coord] = kind;
            return new NodeContent(Mark, "p");
        });

        Assert.All(kinds.Where(k => k.Key.Row == 0), k => Assert.Equal(MapNodeKind.Combat, k.Value));
        Assert.All(kinds.Where(k => k.Key.Row == 4), k => Assert.Equal(MapNodeKind.Rest, k.Value)); // row before boss
        Assert.All(kinds.Where(k => k.Key.Row == 5), k => Assert.Equal(MapNodeKind.Boss, k.Value)); // boss row
    }

    // ── Determinism ────────────────────────────────────────────────────────────

    [Fact]
    public void The_same_seed_reproduces_an_identical_map()
    {
        Assert.Equal(Signature(Act(seed: 123)), Signature(Act(seed: 123)));
    }

    [Fact]
    public void Different_seeds_generally_produce_different_maps()
    {
        Assert.NotEqual(Signature(Act(seed: 1)), Signature(Act(seed: 2)));
    }

    private static string Signature(RunMap map) =>
        string.Join("|", map.Nodes.Select(n => n.Id.Value)) + "##" +
        string.Join("|", map.Edges.Select(e => $"{e.From.Value}->{e.To.Value}")) + "##" +
        string.Join("|", map.EntryNodeIds.Select(id => id.Value));

    // ── End-to-end: a generated act is walkable by RunRunner ─────────────────────

    private sealed class RecordingResolver : INodeResolver
    {
        public List<string> Entered { get; } = new();
        public NodeType NodeType => Mark;
        public NodeOutcome Resolve(NodeResolveContext context, Node node)
        {
            Entered.Add(node.Id.Value);
            return new NodeOutcome(node.Id.Value);
        }
    }

    [Fact]
    public void A_generated_act_is_walkable_start_to_boss()
    {
        var map = Act(seed: 55, rows: 5);
        var resolver = new RecordingResolver();
        var builder = new RunDefinitionRegistryBuilder();
        builder.RegisterResolver(resolver);
        var run = new RunState(new RunId("run"), new HealthState(30, 30), map);

        new RunRunner(builder.Build(), new ScriptedChoiceProvider()).Run(run); // default picks the first fork each time

        Assert.Equal(RunResult.Victory, run.Result);
        Assert.StartsWith("r0c", resolver.Entered.First());  // began on the entry row
        Assert.Equal("r5c0", resolver.Entered.Last());       // ended on the boss
        Assert.Equal(new NodeId("r5c0"), run.CurrentNodeId);
    }

    // ── RunMapBuilder ────────────────────────────────────────────────────────

    [Fact]
    public void RunMapBuilder_assembles_nodes_edges_and_entries()
    {
        var map = new RunMapBuilder()
            .AddNode(new NodeId("a"), Mark, "p")
            .AddNode(new NodeId("b"), Mark, "p")
            .Connect("a", "b")
            .Entry("a")
            .Build();

        Assert.Equal(2, map.Nodes.Count);
        Assert.Equal(new MapEdge(new NodeId("a"), new NodeId("b")), Assert.Single(map.Edges));
        Assert.Equal(new NodeId("a"), Assert.Single(map.EntryNodeIds));
        Assert.Empty(RunMapValidator.Validate(map));
    }

    [Fact]
    public void RunMapBuilder_rejects_a_duplicate_node_id()
    {
        var builder = new RunMapBuilder().AddNode(new NodeId("a"), Mark, "p");
        Assert.Throws<InvalidOperationException>(() => builder.AddNode(new NodeId("a"), Mark, "p"));
    }
}
