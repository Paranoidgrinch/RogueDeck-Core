using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// THE INSTRUMENT, MEASURED AGAINST MAPS WHOSE ANSWERS ARE KNOWN BY HAND (map rework S1).
//
// Every later claim about map generation is going to be a number this class produced, so the numbers have to be
// right on maps small enough to check by eye. The hand-built graphs below are the whole point: on a generated
// map a wrong fork count looks like a plausible fork count.
public class MapDiagnosticsTests
{
    private static readonly NodeType Mark = new("test.mark");

    private static NodeContent Realize(MapNodeKind kind, MapCoord coord, EncounterId? encounter, string? nodeRef = null) =>
        new(Mark, encounter?.ToString() ?? nodeRef ?? "none");

    private static BalanceCalculator EmptyBalance() =>
        new(new BalanceManifest(), Array.Empty<EncounterDefinition>());

    // A two-wide act with one genuinely varied row and one uniform one:
    //
    //   r0   C  ?      ← varied: the fork below it decides something
    //   r1   R  R      ← uniform: both routes hold a rest, so the choice above was no choice
    //   r2   B         ← the boss both routes converge into
    //
    // r0c0 forks to both of r1; r0c1 goes straight down. So: one fork, one merge (r1c0 has two parents),
    // and the boss is a merge as well.
    private static GeneratedMap ByHand()
    {
        var roles = new Dictionary<NodeId, MapNodeKind>();
        var builder = new RunMapBuilder();

        void Add(string id, MapNodeKind role)
        {
            builder.AddNode(new NodeId(id), Mark, id);
            roles[new NodeId(id)] = role;
        }

        Add("r0c0", MapNodeKind.Combat);
        Add("r0c1", MapNodeKind.Event);
        Add("r1c0", MapNodeKind.Rest);
        Add("r1c1", MapNodeKind.Rest);
        Add("r2c0", MapNodeKind.Boss);
        builder.Entry("r0c0").Entry("r0c1");
        builder.Connect("r0c0", "r1c0").Connect("r0c0", "r1c1").Connect("r0c1", "r1c1");
        builder.Connect("r1c0", "r2c0").Connect("r1c1", "r2c0");
        return new GeneratedMap(builder.Build(), roles);
    }

    [Fact]
    public void It_counts_the_rows_the_columns_and_the_edges()
    {
        var diagnostic = MapDiagnostics.Of(ByHand(), seed: 5);

        Assert.Equal(3, diagnostic.Rows);
        Assert.Equal(5, diagnostic.NodeCount);
        Assert.Equal(5, diagnostic.EdgeCount);
        Assert.Equal(new[] { 2, 2, 1 }, diagnostic.Widths);
        Assert.Equal(2, diagnostic.Entries);
        Assert.Equal(5, diagnostic.Seed);
    }

    [Fact]
    public void A_fork_is_a_room_with_two_ways_out_and_a_merge_one_with_two_ways_in()
    {
        var diagnostic = MapDiagnostics.Of(ByHand());

        Assert.Equal(1, diagnostic.Forks);   // r0c0 alone
        Assert.Equal(2, diagnostic.Merges);  // r1c1 (two parents) and the boss
    }

    // The measurement the whole rework exists for: a row whose columns are all the same room is a row where
    // choosing a side changed nothing.
    [Fact]
    public void A_row_whose_columns_hold_the_same_room_counts_as_uniform()
    {
        var diagnostic = MapDiagnostics.Of(ByHand());

        Assert.Equal(1, diagnostic.UniformRows); // r1: Rest, Rest
        Assert.Equal(1, diagnostic.VariedRows);  // r0: Combat, Event
    }

    [Fact]
    public void A_role_every_route_holds_the_same_amount_of_reads_as_a_single_number()
    {
        var diagnostic = MapDiagnostics.Of(ByHand());

        // Both routes cross exactly one rest and exactly one boss; only the opening room differs.
        Assert.Equal(new MapRoleSpread(1, 1), diagnostic.PerPath[MapNodeKind.Rest]);
        Assert.Equal("1", diagnostic.PerPath[MapNodeKind.Rest].ToString());
        Assert.Equal(new MapRoleSpread(0, 1), diagnostic.PerPath[MapNodeKind.Combat]);
        Assert.Equal("0..1", diagnostic.PerPath[MapNodeKind.Combat].ToString());
        Assert.Equal(2, diagnostic.RoomCounts[MapNodeKind.Rest]);
    }

    [Fact]
    public void The_depth_of_a_row_is_the_percentage_the_gates_are_authored_against()
    {
        var diagnostic = MapDiagnostics.Of(ByHand());
        var byRow = diagnostic.Nodes.GroupBy(node => node.Row).ToDictionary(g => g.Key, g => g.First().DepthPercent);

        // Row 0 is the entry and the last row is the boss, so the deepest row a room can stand on is 100 %.
        Assert.Equal(0, byRow[0]);
        Assert.Equal(100, byRow[1]);
        Assert.Equal(MapDepth.Percent(1, 3), byRow[1]);
    }

    [Fact]
    public void The_grid_draws_the_rooms_and_says_where_each_one_leads()
    {
        var grid = MapDiagnostics.Of(ByHand()).Grid();

        Assert.Contains("r00 w2  C ?", grid);
        Assert.Contains("0>0,1 1>1", grid);  // the fork, and the straight run beside it
        Assert.Contains("r01 w2  R R", grid);
        Assert.Contains("r02 w1  B", grid);
    }

    [Fact]
    public void A_node_carries_the_authored_thing_it_realized_as()
    {
        var spec = new MapGenerationSpec
        {
            Rows = 6,
            KindWeights = new Dictionary<MapNodeKind, int> { [MapNodeKind.Combat] = 1 },
            Encounters = new EncounterDistribution
            {
                ByRole = new Dictionary<MapNodeKind, IReadOnlyList<EncounterPoolEntry>>
                {
                    [MapNodeKind.Combat] = [new(new EncounterId("fight-a"), 1)],
                    [MapNodeKind.Boss] = [new(new EncounterId("the-boss"), 1)],
                },
            },
        };
        var generated = RuleBasedMapGenerator.Generate(
            spec, seed: 3, 0, EmptyBalance(),
            (kind, coord, encounter, nodeRef) => MapNodeRealizer.Realize(spec, kind, encounter, nodeRef));

        var diagnostic = MapDiagnostics.Of(generated, seed: 3);
        Assert.All(diagnostic.Nodes, node => Assert.NotNull(node.Content));
        Assert.Contains(diagnostic.Nodes, node => node.Content == "the-boss");
    }

    [Fact]
    public void A_map_whose_rooms_reach_past_each_other_reports_the_crossing()
    {
        // Two rooms that swap sides: the left one reaches right, the right one reaches left, and on screen the
        // two lines cross. The strategic generator's topology forbids this by construction, so the measurement
        // has to exist before there is anything to hold to it.
        var roles = new Dictionary<NodeId, MapNodeKind>();
        var builder = new RunMapBuilder();
        foreach (var (id, role) in new[]
        {
            ("r0c0", MapNodeKind.Combat), ("r0c1", MapNodeKind.Combat),
            ("r1c0", MapNodeKind.Event), ("r1c1", MapNodeKind.Rest),
        })
        {
            builder.AddNode(new NodeId(id), Mark, id);
            roles[new NodeId(id)] = role;
        }
        builder.Entry("r0c0").Entry("r0c1");
        builder.Connect("r0c0", "r1c1").Connect("r0c1", "r1c0");

        Assert.Equal(1, MapDiagnostics.Of(new GeneratedMap(builder.Build(), roles)).Crossings);
        Assert.Equal(0, MapDiagnostics.Of(ByHand()).Crossings);
    }

    // The rule-based generator has no persistent route strands — its lane flavour is the screen column — and a
    // report that invented one for it would be the first thing to mislead a comparison between the generators.
    [Fact]
    public void The_rule_based_generator_reports_no_strands_because_it_has_none()
    {
        var diagnostic = MapDiagnostics.Of(ByHand());

        Assert.All(diagnostic.Nodes, node => Assert.Null(node.Strand));
        Assert.All(diagnostic.Nodes, node => Assert.Null(node.LaneProfile));
    }

    // Measuring the same map twice has to read the same, or no golden file can be built on it.
    [Fact]
    public void The_same_map_renders_identically_every_time()
    {
        Assert.Equal(MapDiagnostics.Of(ByHand(), 1).Render(), MapDiagnostics.Of(ByHand(), 1).Render());
    }

    // The instrument must not go exponential: Act IV of B&B has 8 436 complete routes, and a per-path spread
    // that enumerated them would be abandoned exactly when the map got interesting.
    [Fact]
    public void A_wide_map_is_measured_without_walking_its_routes()
    {
        var spec = new MapGenerationSpec
        {
            Rows = 30,
            MinWidth = 4,
            MaxWidth = 4,
            PerPathMinimums = new Dictionary<MapNodeKind, int> { [MapNodeKind.Elite] = 2 },
            KindWeights = new Dictionary<MapNodeKind, int>
            {
                [MapNodeKind.Combat] = 6,
                [MapNodeKind.Event] = 2,
                [MapNodeKind.Elite] = 1,
            },
        };
        var generated = RuleBasedMapGenerator.Generate(spec, seed: 11, 0, EmptyBalance(), Realize);

        var started = System.Diagnostics.Stopwatch.StartNew();
        var diagnostic = MapDiagnostics.Of(generated, 11);
        started.Stop();

        Assert.True(diagnostic.NodeCount > 100, $"the map should be wide, but holds {diagnostic.NodeCount} nodes");
        Assert.True(started.ElapsedMilliseconds < 2000,
            $"measuring a {diagnostic.NodeCount}-node map took {started.ElapsedMilliseconds} ms");
        Assert.Equal(2, diagnostic.PerPath[MapNodeKind.Elite].Min);
    }
}
