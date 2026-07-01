using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Tests for the data-first card filter vocabulary (R4) and card-value accessors (R5): filters are composed
// from CardValue + the ordinary combinators (no lambda), and SumCards aggregates a card-value expression.
public class CardPredicateTests
{
    private static readonly RunCardTagId Cursed = new("cursed");

    private static RunState DeckOf(params string[] kinds)
    {
        var map = new RunMap(Array.Empty<Node>());
        var run = new RunState(new RunId("run"), new HealthState(30, 40), map);
        foreach (var kind in kinds)
            run.AddDeckCard(new CardDefinitionId(kind));
        return run;
    }

    private static IEnumerable<string> Kinds(IEnumerable<RunCardInstance> cards) =>
        cards.Select(c => c.DefinitionId.ToString());

    [Fact]
    public void CardValue_accessors_read_the_card_in_scope()
    {
        var run = DeckOf("strike");
        var card = run.Deck[0];
        card.Upgrade(2);
        card.AddTag(Cursed);
        card.SetMemory("uses", 4);
        var ctx = new RunEvalContext(run, card: card);

        Assert.Equal(2, CardValue.UpgradeLevel.Evaluate(ctx));
        Assert.Equal(4, CardValue.Memory("uses").Evaluate(ctx));
        Assert.True(CardValue.HasTag(Cursed).Evaluate(ctx));
        Assert.True(CardValue.IsKind(new CardDefinitionId("strike")).Evaluate(ctx));
        Assert.True(CardValue.Upgraded.Evaluate(ctx));
    }

    [Fact]
    public void CardValue_without_a_card_throws()
    {
        var run = DeckOf("strike");
        Assert.Throws<InvalidOperationException>(() => CardValue.UpgradeLevel.Evaluate(run));
    }

    [Fact]
    public void Matching_filters_with_a_composed_data_predicate()
    {
        var run = DeckOf("strike", "strike", "defend");
        run.Deck[0].AddTag(Cursed);
        run.Deck[0].Upgrade();

        // strike AND cursed AND upgraded — pure data, no lambda.
        var predicate = RunExpr.And(
            CardValue.IsKind(new CardDefinitionId("strike")),
            RunExpr.And(CardValue.HasTag(Cursed), CardValue.Upgraded));

        var selected = RunSelectors.DeckCards.Matching(predicate).Select(run);
        Assert.Single(selected);
        Assert.Equal(run.Deck[0].Id, selected[0].Id);
    }

    [Fact]
    public void Matching_can_compare_card_value_against_run_state()
    {
        var run = DeckOf("a", "b", "c");
        run.Deck[0].SetMemory("power", 5);
        run.Deck[1].SetMemory("power", 1);
        run.SetCounter(new RunCounterId("threshold"), 3);

        // Cards whose memory 'power' >= the run counter 'threshold' (3).
        var predicate = RunExpr.GreaterOrEqual(
            CardValue.Memory("power"), RunExpr.Counter(new RunCounterId("threshold")));

        var selected = RunSelectors.DeckCards.Matching(predicate).Select(run);
        Assert.Equal(new[] { "a" }, Kinds(selected));
    }

    [Fact]
    public void Shorthands_still_work_over_the_predicate_path()
    {
        var run = DeckOf("strike", "curse");
        run.Deck[1].AddTag(Cursed);

        Assert.Single(RunSelectors.DeckCards.WithTag(Cursed).Select(run));
        Assert.Single(RunSelectors.DeckCards.OfKind(new CardDefinitionId("strike")).Select(run));
        Assert.Equal(2, RunSelectors.DeckCards.Upgradable().Select(run).Count);
    }

    [Fact]
    public void SumCards_totals_a_card_value_expression()
    {
        var run = DeckOf("a", "b", "c");
        run.Deck[0].Upgrade(2);
        run.Deck[1].Upgrade();

        Assert.Equal(3, RunExpr.SumCards(RunSelectors.DeckCards, CardValue.UpgradeLevel).Evaluate(run));
    }
}
