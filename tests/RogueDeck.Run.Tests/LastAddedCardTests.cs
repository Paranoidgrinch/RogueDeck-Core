using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// "The card you just got." A family of content acts on the card an offer or a purchase just handed over —
// upgrade it, mark it — and the effect that does so runs AFTER the effect that added it, in the same chain,
// with no event and no card in scope to name it by. Two relics need exactly this: Apprentice's Whetstone
// ("whenever you purchase a card, you may pay 20 additional Gold to upgrade it immediately") and Appraiser's
// Chalk ("one random eligible card in a card reward is offered upgraded").
public class LastAddedCardTests
{
    private static readonly RunResourceId Gold = StandardRunIds.Gold;
    private static readonly CardDefinitionId Strike = new("strike");
    private static readonly JsonSerializerOptions Options = RunJson.CreateOptions();

    // Apprentice's Whetstone, written entirely as data: the purchase says it was a card (R3), and the offer the
    // relic puts up is declinable, which is the "you may".
    [Fact]
    public void A_relic_can_sharpen_the_card_a_shop_just_sold()
    {
        var run = NewRun(100);
        BuyACard(run, Whetstone(), takesTheOffer: true);

        var card = Assert.Single(run.Deck);
        Assert.Equal(1, card.UpgradeLevel);
        Assert.Equal(50, run.GetResource(Gold)); // 30 for the card, 20 for the edge
    }

    [Fact]
    public void Declining_the_offer_leaves_the_card_as_it_was()
    {
        var run = NewRun(100);
        BuyACard(run, Whetstone(), takesTheOffer: false);

        Assert.Equal(0, Assert.Single(run.Deck).UpgradeLevel);
        Assert.Equal(70, run.GetResource(Gold));
    }

    // Appraiser's Chalk: the reward rule appends "…and upgrade what you just took" to one offer. The two
    // effects resolve in order inside the same grant, which is exactly what makes "the card you just got" a
    // usable handle here — there is no event to read and nothing else in scope.
    [Fact]
    public void A_reward_offer_can_hand_over_an_upgraded_card()
    {
        var run = NewRun(0);
        run.AddRelic(new RelicInstance(Chalk()));
        Offer(run, new OfferRewardRunEffect(
            new RewardId("combat"),
            [new RewardOffer("strike", [new AddCardToDeckRunEffect(Strike)], RewardKinds.Card, ["normal"])],
            1)
        {
            Kind = RewardKinds.Card,
            Tags = ["normal"],
        });

        Assert.Equal(1, Assert.Single(run.Deck).UpgradeLevel);
    }

    [Fact]
    public void With_nothing_added_yet_the_handle_names_nothing()
    {
        var run = NewRun(0);

        Assert.Empty(RunSelectors.LastAddedCard.Select(run.SelectorContext));
    }

    // A chain that gives a card and then takes it away cannot go on to act on the ghost.
    [Fact]
    public void A_card_that_has_left_the_deck_is_no_longer_the_one_you_just_got()
    {
        var run = NewRun(0);
        var card = run.AddDeckCard(Strike);
        run.RemoveDeckCard(card.Id);

        Assert.Empty(RunSelectors.LastAddedCard.Select(run.SelectorContext));
    }

    [Fact]
    public void The_handle_round_trips_as_data()
    {
        var effect = new UpgradeCardsRunEffect(RunSelectors.LastAddedCard);

        var restored = RunJson.FromJson<IRunEffectRequest>(RunJson.ToJson<IRunEffectRequest>(effect, Options), Options);

        Assert.IsType<LastAddedCardSelector>(Assert.IsType<UpgradeCardsRunEffect>(restored).Selector);
    }

    // ── the relics ─────────────────────────────────────────────────────────────

    private static RelicDefinition Whetstone() =>
        new(new RelicId("whetstone"), "Apprentice's Whetstone",
            runPrograms:
            [
                new DataTriggeredRunEffect<ShopItemPurchasedRunEvent>(
                    RunExpr.And(
                        RunEventValues.ShopItemIsKind(ShopEntryKinds.Card),
                        RunExpr.HasResource(Gold, 20)),
                    [
                        new LiteralEffectTemplate(new OfferRewardRunEffect(
                            new RewardId("whetstone"),
                            [
                                new RewardOffer("sharpen",
                                [
                                    new ChangeResourceRunEffect(Gold, -20),
                                    new UpgradeCardsRunEffect(RunSelectors.LastAddedCard),
                                ]),
                            ],
                            1)),
                    ]),
            ]);

    private static RelicDefinition Chalk() =>
        new(new RelicId("chalk"), "Appraiser's Chalk",
            rewardRules:
            [
                new AppendOfferGrantRule(
                    new RewardMatch(RewardKinds.Card, ["normal"]),
                    [new UpgradeCardsRunEffect(RunSelectors.LastAddedCard)],
                    Count: 1,
                    OfferTags: ["appraised"]),
            ]);

    // ── harness ────────────────────────────────────────────────────────────────

    private static RunState NewRun(int gold)
    {
        var run = new RunState(new RunId("run"), new HealthState(30, 40), new RunMap(Array.Empty<Node>()));
        run.SetResource(Gold, gold);
        return run;
    }

    private static RunDefinitionRegistry Registry()
    {
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        return builder.Build();
    }

    private static void Offer(RunState run, OfferRewardRunEffect reward)
    {
        run.SetEntityChooser(new TakesEverything());
        run.EnqueueEffect(reward);
        new RunEffectProcessor().ResolvePending(run, Registry());
    }

    private static void BuyACard(RunState run, RelicDefinition relic, bool takesTheOffer)
    {
        run.AddRelic(new RelicInstance(relic));
        var shop = new ShopDefinition(
            [new ShopEntry("strike", Gold, 30, [new AddCardToDeckRunEffect(Strike)], Kind: ShopEntryKinds.Card)],
            OfferCount: 1);

        var provider = new ScriptedChoiceProvider("strike", "leave");
        run.SetEntityChooser(takesTheOffer ? new TakesEverything() : new TakesNothing());
        var context = new NodeResolveContext(run, provider, Registry(), new RunEffectProcessor());
        new ShopNodeResolver().Resolve(context, new Node(new NodeId("shop"), StandardRunIds.ShopNode, shop));
    }

    private sealed class TakesEverything : IRunEntityChooser
    {
        public IReadOnlyList<T> ChooseEntities<T>(IReadOnlyList<T> candidates, int count, string purpose) =>
            candidates.Take(count).ToList();
    }

    private sealed class TakesNothing : IRunEntityChooser
    {
        public IReadOnlyList<T> ChooseEntities<T>(IReadOnlyList<T> candidates, int count, string purpose) =>
            candidates.Take(count).ToList();

        public IReadOnlyList<T> ChooseEntities<T>(
            IReadOnlyList<T> candidates, int count, string purpose, bool allowSkip) => [];
    }
}
