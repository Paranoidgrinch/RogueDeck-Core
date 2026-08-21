using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Shop economy R6: what a worn relic does to a REWARD. Like the shop faces, a reward rule is a standing fact
// while the relic is worn rather than a registration that ages out, so the reward asks what the player is
// wearing as it is being built — and it needs no save state. Three relics drive the slice: Bent Auction Gavel
// ("reject it and gain 65 Gold instead"), Twin-Lock Chest Key ("reveal 2 and choose 1"), and Pawnbroker's Loupe
// ("one random card is Appraised … skip the entire reward → gain 6 Gold").
public class RewardRuleTests
{
    private static readonly RunResourceId Gold = StandardRunIds.Gold;
    private static readonly JsonSerializerOptions Options = RunJson.CreateOptions();

    // Bent Auction Gavel. The rejection is not a refusal the engine has to model — it is simply another offer.
    [Fact]
    public void A_relic_can_put_a_buy_out_on_the_table()
    {
        var run = Wearing(Gavel());
        Offer(run, RelicReward(), new PickById("buy-out"));

        Assert.Equal(65, run.GetResource(Gold));
        Assert.Equal(0, run.GetCounter(Taken)); // the relic itself was never acquired
    }

    // "…Boss, Event and purchased rewards are excluded." An exclusion cannot be spelled as an inclusion without
    // listing every case that will ever exist, so the match asks what the reward is NOT tagged with.
    [Fact]
    public void An_excluded_reward_is_left_alone()
    {
        var run = Wearing(Gavel());
        var boss = new OfferRewardRunEffect(new RewardId("boss"), RelicOffers())
        {
            Kind = RewardKinds.Relic,
            Tags = ["normal", "boss"],
        };

        Offer(run, boss, new PickById("buy-out"));

        Assert.Equal(0, run.GetResource(Gold)); // the buy-out was never on the table
    }

    // Twin-Lock Chest Key: "reveal 2 eligible Normal Relics instead and choose 1." The reward's own source is
    // asked for the extra draw, so what appears is whatever that reward could have offered anyway.
    [Fact]
    public void A_relic_can_widen_what_is_revealed()
    {
        var run = Wearing(new RelicDefinition(new RelicId("key"), "Twin-Lock Chest Key",
            rewardRules: [new DrawMoreOffersRule(new RewardMatch(RewardKinds.Relic, ["normal"]))]));

        Offer(run, RelicReward(), new PickById("idol"));

        var offered = run.EventHistory.OfType<RewardOfferedRunEvent>().Single();
        Assert.Equal(2, offered.OfferIds.Count);
    }

    // Pawnbroker's Loupe, the take half: one random card in the reward is Appraised, and taking it pays.
    [Fact]
    public void A_relic_can_sweeten_one_of_the_offers()
    {
        var run = Wearing(Loupe());
        Offer(run, CardReward(), new PickTagged("appraised"));

        Assert.Equal(12, run.GetResource(Gold));
    }

    // "ONE random card is Appraised" — one, whichever the run RNG lands on, and the other two are untouched.
    // Driven straight at the rules, because what is being tested is the reshaping itself.
    [Fact]
    public void Only_as_many_offers_are_sweetened_as_the_rule_says()
    {
        var run = Wearing(Loupe());
        var reward = CardReward();
        var offers = reward.Source.Generate(run).ToList();

        RewardRules.Apply(run, run.ActiveRewardRules, reward.Kind, reward.Tags, reward.Source, offers);

        Assert.Equal(3, offers.Count);
        Assert.Single(offers, offer => offer.Tags?.Contains("appraised") == true);
        Assert.Equal(2, offers.Count(offer => offer.Grant.Count == 0));
    }

    // Pawnbroker's Loupe, the skip half. Taking nothing that was on the table is its own outcome, not the
    // absence of one — and the relic can only pay for walking away if the walk-away is announced.
    [Fact]
    public void Walking_away_from_a_reward_is_announced_with_what_it_was()
    {
        var run = Wearing(Loupe());
        Offer(run, CardReward(), new PickNothing());

        var skipped = run.EventHistory.OfType<RewardSkippedRunEvent>().Single();
        var context = new RunEvalContext(run, skipped);
        Assert.True(RunEventValues.RewardIsKind(RewardKinds.Card).Evaluate(context));
        Assert.True(RunEventValues.RewardHasTag("normal").Evaluate(context));
        Assert.False(RunEventValues.RewardHasTag("boss").Evaluate(context));
    }

    // Taking what was offered is NOT walking away, however few offers there were.
    [Fact]
    public void Taking_something_is_not_walking_away()
    {
        var run = Wearing(Loupe());
        Offer(run, CardReward(), new PickById("card-a"));

        Assert.Empty(run.EventHistory.OfType<RewardSkippedRunEvent>());
    }

    [Fact]
    public void A_disabled_relic_changes_nothing_about_a_reward()
    {
        var run = new RunState(new RunId("run"), new HealthState(30, 40), new RunMap(Array.Empty<Node>()));
        var relic = new RelicInstance(Gavel());
        relic.SetEnabled(false);
        run.AddRelic(relic);

        Offer(run, RelicReward(), new PickById("buy-out"));

        Assert.Equal(0, run.GetResource(Gold));
    }

    [Fact]
    public void Reward_rules_round_trip_as_relic_data()
    {
        var restored = RunJson.FromJson<RelicData>(RunJson.ToJson(RelicData.From(Loupe()), Options), Options)
            .ToDefinition();

        var rule = Assert.IsType<AppendOfferGrantRule>(Assert.Single(restored.RewardRules));
        Assert.Equal(RewardKinds.Card, rule.Match.Kind);
        Assert.Equal(["appraised"], rule.OfferTags);
    }

    [Fact]
    public void A_relic_that_touches_no_reward_writes_nothing()
    {
        var json = RunJson.ToJson(RelicData.From(new RelicDefinition(new RelicId("plain"), "Plain")), Options);

        Assert.DoesNotContain("RewardRules", json, StringComparison.Ordinal);
    }

    // ── the relics ─────────────────────────────────────────────────────────────

    private static RelicDefinition Gavel() =>
        new(new RelicId("gavel"), "Bent Auction Gavel",
            rewardRules:
            [
                new AddRewardOfferRule(
                    new RewardMatch(RewardKinds.Relic, ["normal"], NoneTag: ["boss", "event", "purchased"]),
                    new RewardOffer("buy-out", [new ChangeResourceRunEffect(Gold, 65)])),
            ]);

    private static RelicDefinition Loupe() =>
        new(new RelicId("loupe"), "Pawnbroker's Loupe",
            rewardRules:
            [
                new AppendOfferGrantRule(
                    new RewardMatch(RewardKinds.Card, ["normal"]),
                    [new ChangeResourceRunEffect(Gold, 12)],
                    Count: 1,
                    OfferTags: ["appraised"]),
            ]);

    // ── harness ────────────────────────────────────────────────────────────────

    // The grants are a counter rather than a real relic: what is under test is which offers reach the table.
    private static readonly RunCounterId Taken = new("relics-taken");

    private static IReadOnlyList<RewardOffer> RelicOffers() =>
        [new RewardOffer("idol", [new IncrementCounterRunEffect(Taken, 1)], RewardKinds.Relic, ["normal"]),
         new RewardOffer("charm", [new IncrementCounterRunEffect(Taken, 1)], RewardKinds.Relic, ["normal"])];

    // A treasure's single relic: one offer drawn from a two-deep pool, so the Chest Key has something to reveal.
    private static OfferRewardRunEffect RelicReward() =>
        new(new RewardId("treasure"), new PoolRewardSource(RunPool.Uniform(RelicOffers().ToArray()), 1), 1)
        {
            Kind = RewardKinds.Relic,
            Tags = ["normal"],
        };

    private static OfferRewardRunEffect CardReward() =>
        new(new RewardId("combat"),
            [
                new RewardOffer("card-a", [], RewardKinds.Card, ["normal"]),
                new RewardOffer("card-b", [], RewardKinds.Card, ["normal"]),
                new RewardOffer("card-c", [], RewardKinds.Card, ["normal"]),
            ], 1)
        {
            Kind = RewardKinds.Card,
            Tags = ["normal"],
        };

    private static RunState Wearing(RelicDefinition relic)
    {
        var run = new RunState(new RunId("run"), new HealthState(30, 40), new RunMap(Array.Empty<Node>()));
        run.AddRelic(new RelicInstance(relic));
        return run;
    }

    private static void Offer(RunState run, OfferRewardRunEffect reward, IRunEntityChooser chooser)
    {
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        run.SetEntityChooser(chooser);
        run.EnqueueEffect(reward);
        new RunEffectProcessor().ResolvePending(run, builder.Build());
    }

    private sealed class PickById(string id) : IRunEntityChooser
    {
        public IReadOnlyList<T> ChooseEntities<T>(IReadOnlyList<T> candidates, int count, string purpose) =>
            candidates.Where(c => c is RewardOffer offer && offer.Id == id).Take(count).ToList();
    }

    private sealed class PickTagged(string tag) : IRunEntityChooser
    {
        public IReadOnlyList<T> ChooseEntities<T>(IReadOnlyList<T> candidates, int count, string purpose) =>
            candidates.Where(c => c is RewardOffer { Tags: { } tags } && tags.Contains(tag)).Take(count).ToList();
    }

    private sealed class PickNothing : IRunEntityChooser
    {
        public IReadOnlyList<T> ChooseEntities<T>(IReadOnlyList<T> candidates, int count, string purpose) =>
            candidates.Take(count).ToList();

        public IReadOnlyList<T> ChooseEntities<T>(
            IReadOnlyList<T> candidates, int count, string purpose, bool allowSkip) => [];
    }
}
