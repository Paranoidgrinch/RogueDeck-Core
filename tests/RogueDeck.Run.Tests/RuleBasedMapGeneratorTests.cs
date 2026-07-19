using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Phase 3 — rule-based, constraint-driven, balance-aware map generation. Themes: the output is a valid DAG; every
// entry→boss path meets the per-path minimums (proved independently by brute-force path enumeration, not just the
// validator); generation is deterministic from the seed; and combat nodes deepen in difficulty toward a target.
public class RuleBasedMapGeneratorTests
{
    private static readonly NodeType Mark = new("test.mark");

    private static NodeContent Realize(MapNodeKind kind, MapCoord coord, EncounterId? encounter) =>
        new(Mark, encounter?.ToString() ?? "none");

    private static BalanceCalculator EmptyBalance() =>
        new(new BalanceManifest(), Array.Empty<EncounterDefinition>());

    // ── Structural validity ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(42)]
    [InlineData(1000)]
    public void A_generated_map_is_a_valid_dag(int seed)
    {
        var spec = new MapGenerationSpec
        {
            Rows = 8,
            PerPathMinimums = new Dictionary<MapNodeKind, int>
            {
                [MapNodeKind.Elite] = 2,
                [MapNodeKind.Shop] = 1,
                [MapNodeKind.Workbench] = 1,
                [MapNodeKind.Treasure] = 1,
            },
            MinEnemiesPerPath = 5,
        };

        var generated = RuleBasedMapGenerator.Generate(spec, seed, 0, EmptyBalance(), Realize);
        Assert.Empty(RunMapValidator.Validate(generated.Map));
        Assert.Empty(MapConstraintValidator.Validate(generated, spec));
    }

    // ── Per-path minimums are GUARANTEED (brute force over every path) ────────────

    [Theory]
    [InlineData(3)]
    [InlineData(99)]
    [InlineData(2026)]
    public void Every_path_meets_the_per_path_minimums(int seed)
    {
        var spec = new MapGenerationSpec
        {
            Rows = 10, // 2 varied rows on top of the reserved ones, so paths genuinely differ above the floor
            MinWidth = 2,
            MaxWidth = 4,
            PerPathMinimums = new Dictionary<MapNodeKind, int>
            {
                [MapNodeKind.Elite] = 2,
                [MapNodeKind.Shop] = 2,
                [MapNodeKind.Workbench] = 1,
                [MapNodeKind.Treasure] = 1,
            },
            MinEnemiesPerPath = 6,
        };

        var generated = RuleBasedMapGenerator.Generate(spec, seed, 0, EmptyBalance(), Realize);
        var roles = generated.Roles;

        foreach (var path in AllPaths(generated.Map))
        {
            int Count(MapNodeKind k) => path.Count(id => roles[id] == k);
            var enemies = path.Count(id => roles[id] is MapNodeKind.Combat or MapNodeKind.Elite);

            Assert.True(Count(MapNodeKind.Elite) >= 2, "path short on elites");
            Assert.True(Count(MapNodeKind.Shop) >= 2, "path short on shops");
            Assert.True(Count(MapNodeKind.Workbench) >= 1, "path short on workbenches");
            Assert.True(Count(MapNodeKind.Treasure) >= 1, "path short on treasures");
            Assert.True(enemies >= 6, $"path short on enemies ({enemies})");

            // Exactly one boss, at the end.
            Assert.Equal(MapNodeKind.Boss, roles[path[^1]]);
            Assert.Single(path, id => roles[id] == MapNodeKind.Boss);
        }
    }

    // Even when the minimums exceed the branch-row count, the map still builds (gate funnels are always insertable)
    // and every path meets them — there is no infeasibility.
    [Theory]
    [InlineData(4)]
    [InlineData(77)]
    public void Tight_minimums_still_build_and_hold(int seed)
    {
        var spec = new MapGenerationSpec
        {
            Rows = 2, // fewer branch rows than the minimums — funnels fill the gap
            PerPathMinimums = new Dictionary<MapNodeKind, int>
            {
                [MapNodeKind.Elite] = 2,
                [MapNodeKind.Shop] = 2,
            },
        };
        var generated = RuleBasedMapGenerator.Generate(spec, seed, 0, EmptyBalance(), Realize);
        Assert.Empty(RunMapValidator.Validate(generated.Map));
        Assert.Empty(MapConstraintValidator.Validate(generated, spec));
    }

    // The wide branch rows carry real variety: heterogeneous columns within a row, so the map is not a stack of
    // one-kind rows. (With only Combat + Event weighted, at least one branch row mixes the two.)
    [Fact]
    public void Branch_rows_are_heterogeneous_not_one_kind_walls()
    {
        var spec = new MapGenerationSpec
        {
            Rows = 8,
            MinWidth = 3,
            MaxWidth = 4,
            KindWeights = new Dictionary<MapNodeKind, int> { [MapNodeKind.Combat] = 1, [MapNodeKind.Event] = 1 },
            Encounters = new EncounterDistribution
            {
                ByRole = new Dictionary<MapNodeKind, IReadOnlyList<EncounterPoolEntry>>
                {
                    [MapNodeKind.Boss] = new[] { new EncounterPoolEntry(new EncounterId("boss")) },
                },
            },
        };

        var generated = RuleBasedMapGenerator.Generate(spec, 5, 0, EmptyBalance(),
            (kind, coord, enc) => new NodeContent(Mark, "p"));

        // Group nodes by row; at least one wide row must contain more than one distinct role.
        var byRow = generated.Roles
            .GroupBy(kv => kv.Key.Value.Split('c')[0])
            .Select(g => g.Select(kv => kv.Value).Distinct().Count());
        Assert.Contains(byRow, distinct => distinct > 1);
    }

    // ── Determinism ──────────────────────────────────────────────────────────────

    [Fact]
    public void The_same_seed_reproduces_map_and_roles()
    {
        var spec = new MapGenerationSpec { Rows = 7, MinEnemiesPerPath = 3 };
        Assert.Equal(
            Signature(RuleBasedMapGenerator.Generate(spec, 123, 0, EmptyBalance(), Realize)),
            Signature(RuleBasedMapGenerator.Generate(spec, 123, 0, EmptyBalance(), Realize)));
        Assert.NotEqual(
            Signature(RuleBasedMapGenerator.Generate(spec, 123, 0, EmptyBalance(), Realize)),
            Signature(RuleBasedMapGenerator.Generate(spec, 456, 0, EmptyBalance(), Realize)));
    }

    // ── Balancing hook: combat difficulty deepens toward the depth target ─────────

    [Fact]
    public void Combat_nodes_deepen_in_difficulty_toward_the_depth_target()
    {
        // Three combats of rising threat, all-combat rows, and a target net that drops 10 per row from 80.
        var balance = new BalanceCalculator(
            new BalanceManifest
            {
                Encounters = new Dictionary<string, int>
                {
                    ["easy"] = -20,   // net 80 at loadout 100
                    ["hard"] = -60,   // net 40
                    ["brutal"] = -90, // net 10
                },
            },
            Array.Empty<EncounterDefinition>());

        var spec = new MapGenerationSpec
        {
            Rows = 8,
            KindWeights = new Dictionary<MapNodeKind, int> { [MapNodeKind.Combat] = 1 }, // every varied row is combat
            Encounters = new EncounterDistribution
            {
                ByRole = new Dictionary<MapNodeKind, IReadOnlyList<EncounterPoolEntry>>
                {
                    [MapNodeKind.Combat] = new[]
                    {
                        new EncounterPoolEntry(new EncounterId("easy")),
                        new EncounterPoolEntry(new EncounterId("hard")),
                        new EncounterPoolEntry(new EncounterId("brutal")),
                    },
                },
            },
            BalanceTargets = new BalanceTargets { StartNet = 80, NetDropPerRow = 10, Tolerance = 15 },
        };

        var chosen = new Dictionary<MapCoord, string>();
        RuleBasedMapGenerator.Generate(spec, 5, startingLoadout: 100, balance, (kind, coord, encounter) =>
        {
            chosen[coord] = encounter?.ToString() ?? "none";
            return new NodeContent(Mark, "p");
        });

        // Row 1 target 70 ⇒ only 'easy' (net 80) is in band; row 7 target 10 ⇒ only 'brutal' (net 10).
        Assert.All(chosen.Where(c => c.Key.Row == 1), c => Assert.Equal("easy", c.Value));
        Assert.All(chosen.Where(c => c.Key.Row == 7), c => Assert.Equal("brutal", c.Value));
    }

    // ── MapConstraintValidator flags violations ──────────────────────────────────

    [Fact]
    public void Validator_reports_a_missing_per_path_kind_and_a_map_wide_shortfall()
    {
        // A trivial combat→boss map with no elites.
        var map = new RunMapBuilder()
            .AddNode(new NodeId("a"), Mark, "p")
            .AddNode(new NodeId("b"), Mark, "p")
            .Connect("a", "b")
            .Entry("a")
            .Build();
        var generated = new GeneratedMap(map, new Dictionary<NodeId, MapNodeKind>
        {
            [new NodeId("a")] = MapNodeKind.Combat,
            [new NodeId("b")] = MapNodeKind.Boss,
        });

        var spec = new MapGenerationSpec
        {
            Rows = 1,
            PerPathMinimums = new Dictionary<MapNodeKind, int> { [MapNodeKind.Elite] = 1 },
            MapWideMinimums = new Dictionary<MapNodeKind, int> { [MapNodeKind.Shop] = 2 },
        };

        var problems = MapConstraintValidator.Validate(generated, spec);
        Assert.Contains(problems, p => p.Contains("Elite"));
        Assert.Contains(problems, p => p.Contains("Shop"));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static List<List<NodeId>> AllPaths(RunMap map)
    {
        var entries = map.EntryNodeIds.Count > 0 ? map.EntryNodeIds : map.RootIds();
        var paths = new List<List<NodeId>>();

        void Walk(NodeId id, List<NodeId> acc)
        {
            acc.Add(id);
            var successors = map.SuccessorIds(id).ToList();
            if (successors.Count == 0)
                paths.Add(new List<NodeId>(acc));
            else
                foreach (var successor in successors)
                    Walk(successor, acc);
            acc.RemoveAt(acc.Count - 1);
        }

        foreach (var entry in entries)
            Walk(entry, new List<NodeId>());
        return paths;
    }

    private static string Signature(GeneratedMap generated)
    {
        var map = generated.Map;
        var roles = string.Join("|", generated.Roles.OrderBy(kv => kv.Key.Value).Select(kv => $"{kv.Key.Value}:{kv.Value}"));
        return string.Join("|", map.Nodes.Select(n => n.Id.Value)) + "##"
            + string.Join("|", map.Edges.Select(e => $"{e.From.Value}->{e.To.Value}")) + "##"
            + string.Join("|", map.EntryNodeIds.Select(id => id.Value)) + "##" + roles;
    }
}
