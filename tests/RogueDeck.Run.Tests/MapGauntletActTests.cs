namespace RogueDeck.Run.Tests;

// AN ACT THAT IS NOTHING BUT ITS BOSSES. Every act so far is a backbone of rooms ending on one boss; the B&B
// design's final act is three bosses back to back with no room, no recovery and no spoils between them. That
// is not a second generator — it is a spec with no branch rows (Rows = 0) and BossRooms = 3, and what these
// tests pin is that such a map is still a map: three rooms, three DIFFERENT fights, in an order the run knows
// from the moment it starts, and nothing else in it at all.
public class MapGauntletActTests
{
    private static readonly NodeType Mark = new("test.mark");

    private static NodeContent Realize(MapNodeKind kind, MapCoord coord, EncounterId? encounter, string? nodeRef = null) =>
        new(Mark, encounter?.ToString() ?? nodeRef ?? "none");

    private static BalanceCalculator EmptyBalance() =>
        new(new BalanceManifest(), Array.Empty<EncounterDefinition>());

    private static MapGenerationSpec Gauntlet(int rooms = 3, int gods = 6) => new()
    {
        Rows = 0,
        BossRooms = rooms,
        MinWidth = 1,
        MaxWidth = 1,
        KindWeights = new Dictionary<MapNodeKind, int>(),
        Encounters = new EncounterDistribution
        {
            ByRole = new Dictionary<MapNodeKind, IReadOnlyList<EncounterPoolEntry>>
            {
                [MapNodeKind.Boss] = Enumerable.Range(0, gods)
                    .Select(i => new EncounterPoolEntry(new EncounterId($"god.{i}"))).ToArray(),
            },
        },
    };

    [Fact]
    public void A_gauntlet_act_is_three_boss_rooms_and_nothing_else()
    {
        var generated = RuleBasedMapGenerator.Generate(Gauntlet(), seed: 20260905, 0, EmptyBalance(), Realize);

        Assert.Equal(3, generated.Map.Nodes.Count);
        Assert.All(generated.Map.Nodes, node => Assert.Equal(MapNodeKind.Boss, generated.Roles[node.Id]));
        Assert.Empty(RunMapValidator.Validate(generated.Map));
        Assert.Empty(MapConstraintValidator.Validate(generated, Gauntlet()));
    }

    [Fact]
    public void The_three_rooms_are_walked_one_after_another_from_the_first()
    {
        var generated = RuleBasedMapGenerator.Generate(Gauntlet(), seed: 7, 0, EmptyBalance(), Realize);
        var map = generated.Map;

        // One way in, one way on, one way out: the act offers no choice, only an order.
        Assert.Single(map.EntryNodeIds);
        var walked = new List<NodeId>();
        var at = map.EntryNodeIds[0];
        while (true)
        {
            walked.Add(at);
            var next = map.Edges.Where(e => e.From == at).Select(e => e.To).ToList();
            if (next.Count == 0)
                break;
            Assert.Single(next);
            at = next[0];
        }
        Assert.Equal(3, walked.Count);
    }

    [Fact]
    public void No_god_is_fought_twice_in_the_same_run()
    {
        for (var seed = 0; seed < 200; seed++)
        {
            var generated = RuleBasedMapGenerator.Generate(Gauntlet(), seed, 0, EmptyBalance(), Realize);
            var fought = generated.Map.Nodes.Select(n => (string)n.Payload).ToList();
            Assert.Equal(3, fought.Count);
            Assert.Equal(3, fought.Distinct().Count());
        }
    }

    // The pool-that-nothing-draws-from finding, one act later: six gods of which a run meets three is only a
    // pool of six if every one of them can actually be met.
    [Fact]
    public void All_six_gods_are_reachable_across_runs()
    {
        var met = new HashSet<string>();
        for (var seed = 0; seed < 200; seed++)
            foreach (var node in RuleBasedMapGenerator.Generate(Gauntlet(), seed, 0, EmptyBalance(), Realize).Map.Nodes)
                met.Add((string)node.Payload);

        Assert.Equal(6, met.Count);
    }

    [Fact]
    public void An_act_with_no_rows_may_promise_nothing_because_it_has_nowhere_to_keep_a_promise()
    {
        var spec = Gauntlet() with
        {
            PerPathMinimums = new Dictionary<MapNodeKind, int> { [MapNodeKind.Rest] = 1 },
        };
        Assert.Throws<ArgumentOutOfRangeException>(() => spec.Validate());
    }

    [Fact]
    public void An_ordinary_act_still_ends_on_exactly_one_boss()
    {
        var spec = new MapGenerationSpec
        {
            Rows = 6,
            MinEnemiesPerPath = 3,
            Encounters = new EncounterDistribution
            {
                ByRole = new Dictionary<MapNodeKind, IReadOnlyList<EncounterPoolEntry>>
                {
                    [MapNodeKind.Combat] = [new(new EncounterId("fight"))],
                    [MapNodeKind.Boss] = [new(new EncounterId("boss"))],
                },
            },
            NodeRefs = new Dictionary<MapNodeKind, string> { [MapNodeKind.Event] = "door" },
        };

        var generated = RuleBasedMapGenerator.Generate(spec, seed: 3, 0, EmptyBalance(), Realize);
        Assert.Single(generated.Roles.Values, role => role == MapNodeKind.Boss);
    }
}
