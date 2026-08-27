using RogueDeck.Core.Combat;

namespace RogueDeck.Run.Tests;

// Two run-level map constraints the B&B act specs require:
//   (1) encounter templates are drawn WITHOUT replacement — no fight repeats within a generated map;
//   (2) a per-path minimum of MULTI-enemy combats, guaranteed like any other per-path minimum (gate funnels).
public class MapEncounterConstraintsTests
{
    private static readonly NodeType Mark = new("test.mark");

    private static NodeContent Realize(MapNodeKind kind, MapCoord coord, EncounterId? encounter, string? nodeRef = null) =>
        new(Mark, encounter?.ToString() ?? nodeRef ?? "none");

    private static BalanceCalculator EmptyBalance() =>
        new(new BalanceManifest(), Array.Empty<EncounterDefinition>());

    private static IReadOnlyList<EncounterPoolEntry> Pool(string prefix, int count) =>
        Enumerable.Range(0, count).Select(i => new EncounterPoolEntry(new EncounterId($"{prefix}{i}"))).ToArray();

    [Fact]
    public void No_combat_encounter_template_repeats_across_a_generated_map()
    {
        var spec = new MapGenerationSpec
        {
            Rows = 8,
            MinWidth = 2,
            MaxWidth = 4,
            MinEnemiesPerPath = 4,
            PerPathMinimums = new Dictionary<MapNodeKind, int> { [MapNodeKind.Elite] = 2 },
            Encounters = new EncounterDistribution
            {
                ByRole = new Dictionary<MapNodeKind, IReadOnlyList<EncounterPoolEntry>>
                {
                    [MapNodeKind.Combat] = Pool("fight.", 60), // pool >> node count ⇒ every draw is unique
                    [MapNodeKind.Elite] = Pool("elite.", 30),
                    [MapNodeKind.Boss] = Pool("boss.", 5),
                },
            },
        };

        var generated = RuleBasedMapGenerator.Generate(spec, seed: 42, 0, EmptyBalance(), Realize);

        var fights = generated.Map.Nodes
            .Where(n => generated.Roles[n.Id] is MapNodeKind.Combat or MapNodeKind.Elite or MapNodeKind.Boss)
            .Select(n => (string)n.Payload)
            .ToList();

        Assert.NotEmpty(fights);
        Assert.Equal(fights.Count, fights.Distinct().Count()); // no template used twice
    }

    [Fact]
    public void Every_path_meets_the_multi_combat_minimum()
    {
        var spec = new MapGenerationSpec
        {
            Rows = 8,
            MinWidth = 2,
            MaxWidth = 4,
            PerPathMinimums = new Dictionary<MapNodeKind, int>
            {
                [MapNodeKind.Combat] = 4,
                [MapNodeKind.MultiCombat] = 2,
            },
            Encounters = new EncounterDistribution
            {
                ByRole = new Dictionary<MapNodeKind, IReadOnlyList<EncounterPoolEntry>>
                {
                    [MapNodeKind.Combat] = Pool("solo.", 40),
                    [MapNodeKind.MultiCombat] = Pool("duo.", 20),
                    [MapNodeKind.Boss] = Pool("boss.", 5),
                },
            },
        };

        var generated = RuleBasedMapGenerator.Generate(spec, seed: 7, 0, EmptyBalance(), Realize);

        // The constraint validator is satisfied…
        Assert.Empty(MapConstraintValidator.Validate(generated, spec));

        // …and every entry→boss path really has ≥2 MultiCombat + ≥4 Combat nodes.
        var worstMulti = MapConstraintValidator.WorstPathCount(
            generated.Map, generated.Roles, k => k == MapNodeKind.MultiCombat);
        var worstCombat = MapConstraintValidator.WorstPathCount(
            generated.Map, generated.Roles, k => k == MapNodeKind.Combat);
        Assert.True(worstMulti >= 2, $"worst-path MultiCombat = {worstMulti}");
        Assert.True(worstCombat >= 4, $"worst-path Combat = {worstCombat}");

        // MultiCombat nodes realize as combats drawing from the multi pool.
        foreach (var (id, kind) in generated.Roles)
            if (kind == MapNodeKind.MultiCombat)
                Assert.StartsWith("duo.", (string)generated.Map.Nodes.First(n => n.Id == id).Payload);
    }

    [Fact]
    public void Non_combat_nodes_draw_distinct_refs_from_their_pool()
    {
        var spec = new MapGenerationSpec
        {
            Rows = 8,
            MinWidth = 2,
            MaxWidth = 4,
            MinEnemiesPerPath = 2,
            PerPathMinimums = new Dictionary<MapNodeKind, int> { [MapNodeKind.Event] = 3 },
            KindWeights = new Dictionary<MapNodeKind, int>
            {
                [MapNodeKind.Combat] = 3,
                [MapNodeKind.Event] = 3,
            },
            Encounters = new EncounterDistribution
            {
                ByRole = new Dictionary<MapNodeKind, IReadOnlyList<EncounterPoolEntry>>
                {
                    [MapNodeKind.Combat] = Pool("fight.", 40),
                    [MapNodeKind.Boss] = Pool("boss.", 5),
                },
            },
            // A pool of distinct events (Realize surfaces the chosen ref as the node payload).
            NodeRefPools = new Dictionary<MapNodeKind, IReadOnlyList<string>>
            {
                [MapNodeKind.Event] = Enumerable.Range(0, 20).Select(i => $"event.{i}").ToArray(),
            },
        };

        var generated = RuleBasedMapGenerator.Generate(spec, seed: 3, 0, EmptyBalance(), Realize);

        var eventRefs = generated.Map.Nodes
            .Where(n => generated.Roles[n.Id] == MapNodeKind.Event)
            .Select(n => (string)n.Payload)
            .ToList();

        Assert.NotEmpty(eventRefs);
        Assert.All(eventRefs, r => Assert.StartsWith("event.", r));
        Assert.Equal(eventRefs.Count, eventRefs.Distinct().Count()); // pool >> nodes ⇒ all distinct
    }

    // A door the design only opens late in the act ("earliest stage 8") must not be the first room of the run.
    [Fact]
    public void A_depth_gated_ref_never_opens_in_the_shallow_half_of_the_act()
    {
        var refs = Enumerable.Range(0, 20).Select(i => $"event.{i}").ToArray();
        var spec = new MapGenerationSpec
        {
            Rows = 10,
            MinWidth = 2,
            MaxWidth = 4,
            MinEnemiesPerPath = 2,
            PerPathMinimums = new Dictionary<MapNodeKind, int> { [MapNodeKind.Event] = 3 },
            KindWeights = new Dictionary<MapNodeKind, int>
            {
                [MapNodeKind.Combat] = 3,
                [MapNodeKind.Event] = 3,
            },
            Encounters = new EncounterDistribution
            {
                ByRole = new Dictionary<MapNodeKind, IReadOnlyList<EncounterPoolEntry>>
                {
                    [MapNodeKind.Combat] = Pool("fight.", 60),
                    [MapNodeKind.Boss] = Pool("boss.", 5),
                },
            },
            NodeRefPools = new Dictionary<MapNodeKind, IReadOnlyList<string>>
            {
                [MapNodeKind.Event] = refs,
            },
            // Half the doors are late doors; the other half may open anywhere, so the gate always has somewhere
            // to yield to and never has to be waived.
            NodeRefMinimumDepthPercent = refs.Where((_, i) => i % 2 == 0).ToDictionary(r => r, _ => 70),
        };

        var deepest = 0;
        var seen = 0;
        for (var seed = 1; seed <= 40; seed++)
        {
            var generated = RuleBasedMapGenerator.Generate(spec, seed, 0, EmptyBalance(), Realize);
            var rows = generated.Map.Nodes.Max(n => Row(n.Id)) + 1;
            deepest = Math.Max(deepest, rows);
            foreach (var node in generated.Map.Nodes)
            {
                if (generated.Roles[node.Id] != MapNodeKind.Event)
                    continue;
                var payload = (string)node.Payload;
                if (!spec.NodeRefMinimumDepthPercent.ContainsKey(payload))
                    continue;
                seen++;
                // rows - 2 is the deepest row a door can sit on (the last row is the boss), and that row is 100%.
                Assert.True(Row(node.Id) * 100 / (rows - 2) >= 70,
                    $"'{payload}' opened at row {Row(node.Id)} of {rows}");
            }
        }

        Assert.True(seen > 0, "no gated door was ever placed, so the gate proved nothing");
        Assert.True(deepest > 10, "the generated map should be taller than the act's branch backbone");
    }

    // The same gate one level up: a ROLE the act says starts deeper never stands in the opening rooms — not as
    // a funnel the generator laid, and not as a branch row's own draw either. Without it an act's curve is an
    // accident of the gate order, and a shop can be the first room of a run that has no gold yet.
    [Fact]
    public void A_role_gated_by_depth_never_stands_in_the_opening_rooms()
    {
        var spec = new MapGenerationSpec
        {
            Rows = 6,
            MinWidth = 2,
            MaxWidth = 4,
            MinEnemiesPerPath = 4,
            PerPathMinimums = new Dictionary<MapNodeKind, int>
            {
                [MapNodeKind.Combat] = 4,
                [MapNodeKind.Elite] = 2,
                [MapNodeKind.Shop] = 2,
            },
            // The lanes WANT to open on a shop or an elite; only the depth rule stops them.
            KindWeights = new Dictionary<MapNodeKind, int>
            {
                [MapNodeKind.Combat] = 2,
                [MapNodeKind.Shop] = 3,
                [MapNodeKind.Elite] = 3,
            },
            RoleMinimumDepthPercent = new Dictionary<MapNodeKind, int>
            {
                [MapNodeKind.Shop] = 30,
                [MapNodeKind.Elite] = 50,
            },
            Encounters = new EncounterDistribution
            {
                ByRole = new Dictionary<MapNodeKind, IReadOnlyList<EncounterPoolEntry>>
                {
                    [MapNodeKind.Combat] = Pool("fight.", 60),
                    [MapNodeKind.Elite] = Pool("elite.", 20),
                    [MapNodeKind.Boss] = Pool("boss.", 5),
                },
            },
            NodeRefs = new Dictionary<MapNodeKind, string> { [MapNodeKind.Shop] = "shop" },
        };

        var gatedSeen = 0;
        for (var seed = 1; seed <= 40; seed++)
        {
            var generated = RuleBasedMapGenerator.Generate(spec, seed, 0, EmptyBalance(), Realize);
            Assert.Empty(MapConstraintValidator.Validate(generated, spec)); // the promises still hold
            var rows = generated.Map.Nodes.Max(n => Row(n.Id)) + 1;
            foreach (var node in generated.Map.Nodes)
            {
                var role = generated.Roles[node.Id];
                if (!spec.RoleMinimumDepthPercent.TryGetValue(role, out var earliest))
                    continue;
                gatedSeen++;
                Assert.True(Row(node.Id) * 100 / (rows - 2) >= earliest,
                    $"a {role} stood at row {Row(node.Id)} of {rows}, earlier than the act allows ({earliest}%)");
            }
        }

        Assert.True(gatedSeen > 0, "no gated room was ever placed, so the gate proved nothing");
    }

    // A generated node id is "r{row}c{col}" (MapWiring.Id).
    private static int Row(NodeId id) =>
        int.Parse(id.Value[1..id.Value.IndexOf('c')], System.Globalization.CultureInfo.InvariantCulture);
}
