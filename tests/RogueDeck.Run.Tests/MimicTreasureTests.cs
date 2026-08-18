using RogueDeck.Core.Combat;

namespace RogueDeck.Run.Tests;

// Mimic-in-treasure: a Treasure node flips into a combat (drawn from Encounters[Mimic], tuned ~ a weak elite of
// the act) with a per-act chance. Deterministic per seed; 0% leaves treasure alone; realizes as a normal combat.
public class MimicTreasureTests
{
    private static readonly NodeType Mark = new("test.mark");

    private static NodeContent Realize(MapNodeKind kind, MapCoord coord, EncounterId? encounter, string? nodeRef = null) =>
        new(Mark, encounter?.ToString() ?? nodeRef ?? "none");

    private static BalanceCalculator EmptyBalance() =>
        new(new BalanceManifest(), Array.Empty<EncounterDefinition>());

    private static MapGenerationSpec Spec(int mimicChance) => new()
    {
        Rows = 8,
        PerPathMinimums = new Dictionary<MapNodeKind, int> { [MapNodeKind.Treasure] = 1 },
        MinEnemiesPerPath = 2,
        TreasureMimicChancePercent = mimicChance,
        Encounters = new EncounterDistribution
        {
            ByRole = new Dictionary<MapNodeKind, IReadOnlyList<EncounterPoolEntry>>
            {
                [MapNodeKind.Combat] = new[] { new EncounterPoolEntry(new EncounterId("fight.a")) },
                [MapNodeKind.Elite] = new[] { new EncounterPoolEntry(new EncounterId("elite.a")) },
                [MapNodeKind.Boss] = new[] { new EncounterPoolEntry(new EncounterId("boss.a")) },
                [MapNodeKind.Mimic] = new[] { new EncounterPoolEntry(new EncounterId("mimic.a")) },
            },
        },
        NodeRefs = new Dictionary<MapNodeKind, string>
        {
            [MapNodeKind.Treasure] = "ev.treasure",
            [MapNodeKind.Event] = "ev.event",
            [MapNodeKind.Rest] = "ev.rest",
            [MapNodeKind.Shop] = "shop.a",
            [MapNodeKind.Workbench] = "wb.a",
        },
    };

    [Fact]
    public void At_100_percent_every_treasure_node_becomes_a_mimic_combat()
    {
        var generated = RuleBasedMapGenerator.Generate(Spec(100), seed: 7, 0, EmptyBalance(), Realize);
        var roles = generated.Roles;

        Assert.Contains(roles.Values, k => k == MapNodeKind.Mimic);
        Assert.DoesNotContain(roles.Values, k => k == MapNodeKind.Treasure); // all flipped

        // Every mimic node realized as the mimic combat encounter.
        foreach (var (id, kind) in roles)
            if (kind == MapNodeKind.Mimic)
            {
                var node = generated.Map.Nodes.First(n => n.Id == id);
                Assert.Equal("mimic.a", node.Payload);
            }
    }

    [Fact]
    public void At_zero_percent_treasure_stays_treasure()
    {
        var generated = RuleBasedMapGenerator.Generate(Spec(0), seed: 7, 0, EmptyBalance(), Realize);
        var roles = generated.Roles;

        Assert.Contains(roles.Values, k => k == MapNodeKind.Treasure);
        Assert.DoesNotContain(roles.Values, k => k == MapNodeKind.Mimic);
    }

    [Fact]
    public void Mimic_placement_is_deterministic_per_seed()
    {
        var a = RuleBasedMapGenerator.Generate(Spec(50), seed: 123, 0, EmptyBalance(), Realize).Roles;
        var b = RuleBasedMapGenerator.Generate(Spec(50), seed: 123, 0, EmptyBalance(), Realize).Roles;

        Assert.Equal(
            a.Count(kv => kv.Value == MapNodeKind.Mimic),
            b.Count(kv => kv.Value == MapNodeKind.Mimic));
    }

    [Fact]
    public void The_default_realizer_turns_a_mimic_into_a_combat_node()
    {
        var content = MapNodeRealizer.Realize(Spec(20), MapNodeKind.Mimic, new EncounterId("mimic.a"));
        Assert.Equal(StandardRunIds.CombatNode, content.Type);
        Assert.Equal(new EncounterRef(new EncounterId("mimic.a")), content.Payload);
    }
}
