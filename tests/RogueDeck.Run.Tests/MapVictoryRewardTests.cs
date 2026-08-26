using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// A generated fight pays what its ROLE pays — except where the act names a payout for that one fight. A boss
// handing out one of its own three relics cannot come from a role-wide table: every boss of the act would hand
// out the same pool.
public class MapVictoryRewardTests
{
    private static MapVictoryReward Reward(string offerId) =>
        new(new FixedRewardSource([new RewardOffer(offerId, [new ChangeResourceRunEffect(StandardRunIds.Gold, 1)])]));

    private static MapGenerationSpec Spec() => new()
    {
        Rows = 3,
        VictoryRewards = new Dictionary<MapNodeKind, MapVictoryReward> { [MapNodeKind.Boss] = Reward("role") },
        VictoryRewardsByEncounter = new Dictionary<string, MapVictoryReward> { ["the_charter"] = Reward("its-own") },
    };

    private static string? OfferOf(NodeContent content) =>
        ((content.Payload as EncounterRef)?.VictoryReward as FixedRewardSource)?.Offers[0].Id;

    [Fact]
    public void A_fight_that_names_its_own_payout_gets_it()
    {
        var node = MapNodeRealizer.Realize(Spec(), MapNodeKind.Boss, new EncounterId("the_charter"));

        Assert.Equal("its-own", OfferOf(node));
        Assert.Equal("spoils:the_charter", (node.Payload as EncounterRef)!.VictoryRewardId!.Value.Value);
    }

    [Fact]
    public void Every_other_fight_still_pays_what_its_role_pays()
    {
        Assert.Equal("role", OfferOf(MapNodeRealizer.Realize(Spec(), MapNodeKind.Boss, new EncounterId("someone_else"))));
    }

    [Fact]
    public void A_fight_whose_role_pays_nothing_and_names_nothing_pays_nothing()
    {
        var node = MapNodeRealizer.Realize(Spec(), MapNodeKind.Combat, new EncounterId("a_scribe"));
        Assert.Null((node.Payload as EncounterRef)!.VictoryReward);
    }
}
