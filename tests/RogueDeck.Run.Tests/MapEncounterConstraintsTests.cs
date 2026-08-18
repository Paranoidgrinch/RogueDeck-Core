using RogueDeck.Core.Combat;

namespace RogueDeck.Run.Tests;

// Two run-level map constraints the B&B act specs require:
//   (1) encounter templates are drawn WITHOUT replacement — no fight repeats within a generated map;
//   (2) a per-path minimum of MULTI-enemy combats, guaranteed like any other per-path minimum (gate funnels).
public class MapEncounterConstraintsTests
{
    private static readonly NodeType Mark = new("test.mark");

    private static NodeContent Realize(MapNodeKind kind, MapCoord coord, EncounterId? encounter) =>
        new(Mark, encounter?.ToString() ?? "none");

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
}
