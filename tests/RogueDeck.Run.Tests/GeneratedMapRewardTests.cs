namespace RogueDeck.Run.Tests;

// A generated map's fights pay out. Without this the default realization hands every procedural combat a bare
// EncounterRef — the fight runs, the player wins, and nothing is granted — which silently costs a generated act
// its entire reward economy (an authored map states the spoils on each EncounterRef).
public class GeneratedMapRewardTests
{
    private static readonly RewardId Offer = new("test.offer");

    private static MapGenerationSpec Spec(bool withRewards) => new()
    {
        Rows = 6,
        PerPathMinimums = new Dictionary<MapNodeKind, int> { [MapNodeKind.Elite] = 1 },
        MinEnemiesPerPath = 3,
        Encounters = new EncounterDistribution
        {
            ByRole = new Dictionary<MapNodeKind, IReadOnlyList<EncounterPoolEntry>>
            {
                [MapNodeKind.Combat] = new[] { new EncounterPoolEntry(new EncounterId("fight.a")) },
                [MapNodeKind.Elite] = new[] { new EncounterPoolEntry(new EncounterId("elite.a")) },
                [MapNodeKind.Boss] = new[] { new EncounterPoolEntry(new EncounterId("boss.a")) },
            },
        },
        NodeRefs = new Dictionary<MapNodeKind, string> { [MapNodeKind.Event] = "event.a" },
        VictoryRewards = withRewards
            ? new Dictionary<MapNodeKind, MapVictoryReward>
            {
                [MapNodeKind.Combat] = new(Gold(20)),
                [MapNodeKind.Elite] = new(Gold(50), RewardIdPrefix: "elite-spoils"),
                [MapNodeKind.Boss] = new(Gold(100)),
            }
            : new Dictionary<MapNodeKind, MapVictoryReward>(),
    };

    private static FixedRewardSource Gold(int amount) =>
        new([new RewardOffer("spoils", [new ChangeResourceRunEffect(StandardRunIds.Gold, amount)])]);

    private static IReadOnlyList<EncounterRef> Fights(MapGenerationSpec spec) =>
        RuleBasedMapGenerator.Generate(spec, seed: 4, startingLoadout: 0,
                new BalanceCalculator(new BalanceManifest(), Array.Empty<EncounterDefinition>()),
                (kind, coord, encounter, nodeRef) => MapNodeRealizer.Realize(spec, kind, encounter, nodeRef))
            .Map.Nodes.Select(n => n.Payload).OfType<EncounterRef>().ToList();

    [Fact]
    public void Every_generated_fight_carries_the_spoils_its_role_declares()
    {
        var fights = Fights(Spec(withRewards: true));

        Assert.NotEmpty(fights);
        Assert.All(fights, fight => Assert.NotNull(fight.VictoryReward));
        // The reward id names the encounter, so two fights of one role stay apart in the run log.
        Assert.All(fights, fight => Assert.Contains(fight.Id.Value, fight.VictoryRewardId!.Value.Value));
        Assert.Contains(fights, fight => fight.VictoryRewardId!.Value.Value.StartsWith("elite-spoils:"));
    }

    [Fact]
    public void A_spec_that_declares_no_rewards_still_generates_plain_fights()
    {
        Assert.All(Fights(Spec(withRewards: false)), fight => Assert.Null(fight.VictoryReward));
    }
}
