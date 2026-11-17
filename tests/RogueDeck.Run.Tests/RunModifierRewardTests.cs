using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// What the run modifiers of a meta progression need from a reward (bnb, 2026-11-18): fewer offers ("Strict
// Filing"), none at all ("Bureaucratic Sacrifice"), a redraw offered once ("Second Opinion"), and content a run
// has locked because the player has not earned it yet.
public class RunModifierRewardTests
{
    [Fact]
    public void A_drop_rule_takes_one_offer_off_the_table()
    {
        var run = Wearing(new DropOffersRule(new RewardMatch(RewardKinds.Card), 1));
        var offered = Offer(run, CardReward(), new PickFirst());

        Assert.Equal(2, offered.Single().OfferIds.Count);
    }

    [Fact]
    public void A_drop_rule_of_count_zero_takes_every_match_but_leaves_excluded_rewards_alone()
    {
        var run = Wearing(new DropOffersRule(new RewardMatch(RewardKinds.Card, NoneTag: ["dropped"]), 0));

        Assert.Empty(Offer(run, CardReward(), new PickFirst()));

        var dropped = CardReward() with { Tags = ["dropped"] };
        Assert.Equal(3, Offer(run, dropped, new PickFirst()).Single().OfferIds.Count);
    }

    [Fact]
    public void A_redraw_is_offered_once_and_puts_the_same_reward_on_the_table_again()
    {
        var run = Wearing(new RedrawOfferRule(new RewardMatch(RewardKinds.Card)));
        var offered = Offer(run, CardReward(), new PickById("combat:redraw"));

        Assert.Equal(2, offered.Count);
        Assert.Contains("combat:redraw", offered[0].OfferIds);
        Assert.Equal(4, offered[0].OfferIds.Count);
        // The redrawn reward is the same three cards, and offers no second redraw.
        Assert.Equal(3, offered[1].OfferIds.Count);
        Assert.DoesNotContain(offered[1].OfferIds, id => id.EndsWith(":redraw", StringComparison.Ordinal));
        Assert.Contains(RewardRules.RedrawnTag, offered[1].Tags ?? []);
    }

    [Fact]
    public void Locked_content_is_never_drawn_and_unlocked_content_is()
    {
        var pool = new PoolRewardSource(RunPool.Uniform(
            new RewardOffer("card-a", [new AddCardToDeckRunEffect(new CardDefinitionId("a"))], RewardKinds.Card),
            new RewardOffer("card-b", [new AddCardToDeckRunEffect(new CardDefinitionId("b"))], RewardKinds.Card),
            new RewardOffer("card-c", [new AddCardToDeckRunEffect(new CardDefinitionId("c"))], RewardKinds.Card)), 3);

        var run = Wearing(null);
        run.SetFlag(new RunFlagId("meta." + CharacterContent.LockedPrefix + "b"), true);
        run.SetFlag(new RunFlagId(CharacterContent.LockedPrefix + "c"), true);

        Assert.Equal(["card-a"], pool.Generate(run).Select(o => o.Id));
        Assert.Equal(3, pool.Generate(Wearing(null)).Count);
    }

    private static OfferRewardRunEffect CardReward() =>
        new(new RewardId("combat"),
            [
                new RewardOffer("card-a", [], RewardKinds.Card),
                new RewardOffer("card-b", [], RewardKinds.Card),
                new RewardOffer("card-c", [], RewardKinds.Card),
            ], 1)
        {
            Kind = RewardKinds.Card,
        };

    private static RunState Wearing(IRewardRule? rule)
    {
        var run = new RunState(new RunId("run"), new HealthState(30, 40), new RunMap(Array.Empty<Node>()));
        if (rule is not null)
            run.AddRelic(new RelicInstance(new RelicDefinition(new RelicId("modifier"), "Modifier", rewardRules: [rule])));
        return run;
    }

    private static List<RewardOfferedRunEvent> Offer(RunState run, OfferRewardRunEffect reward, IRunEntityChooser chooser)
    {
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        run.SetEntityChooser(chooser);
        run.EnqueueEffect(reward);
        new RunEffectProcessor().ResolvePending(run, builder.Build());
        return [.. run.EventHistory.OfType<RewardOfferedRunEvent>()];
    }

    private sealed class PickFirst : IRunEntityChooser
    {
        public IReadOnlyList<T> ChooseEntities<T>(IReadOnlyList<T> candidates, int count, string purpose) =>
            candidates.Take(count).ToList();
    }

    private sealed class PickById(string id) : IRunEntityChooser
    {
        public IReadOnlyList<T> ChooseEntities<T>(IReadOnlyList<T> candidates, int count, string purpose) =>
            candidates.Where(c => c is RewardOffer offer && offer.Id == id).Take(count).ToList();
    }
}
