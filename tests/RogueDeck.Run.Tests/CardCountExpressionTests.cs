using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// "How many cards in the deck match this?" as DATA.
//
// The count expression had always been evaluable and never storable: an authored document that asked it
// threw at serialization, so a branch could not say "upgrade a Deed; if there is no Deed, upgrade anything
// else" — a clause B&B's Fixed-Day Festival writes out in so many words. The closed card form is registered
// now, which is the only one a document can carry: the selector it counts is itself data.
public class CardCountExpressionTests
{
    [Fact]
    public void A_card_count_over_a_data_selector_round_trips_and_still_counts()
    {
        var options = RunJson.CreateOptions();
        var counting = RunExpr.GreaterThan(
            RunExpr.Count(RunSelectors.DeckCards.WithTag(new RunCardTagId("deed"))),
            RunExpr.Const(0));

        var json = RunJson.ToJson(counting, options);
        Assert.Contains("cardCount", json, StringComparison.Ordinal);

        var read = RunJson.FromJson<IRunExpression<bool>>(json, options);

        var run = new RunState(new RunId("run"), new HealthState(20, 20), new RunMap([]));
        Assert.False(read.Evaluate(new RunEvalContext(run)));

        run.AddDeckCard(new CardDefinitionId("filing")).AddTag(new RunCardTagId("deed"));
        Assert.True(read.Evaluate(new RunEvalContext(run)));
    }
}
