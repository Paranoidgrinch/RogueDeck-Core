using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// ⚠⚠ "CHOOSE A CARD" IS THE SAME REQUEST WHETHER THE CARD IS ABOUT TO BE UPGRADED OR TAKEN AWAY, and the
// candidates look identical either way — the player's own deck. A screen can read the purpose text; anything
// answering automatically cannot, and got it backwards for the whole history of this project: it scored the
// candidates and took the best, which is right for an upgrade and exactly wrong for a removal.
//
// So the effect that CONSUMES the selection says what it means to do with it. Keep is the default, so no
// chooser written before this existed changes its mind about anything.
public class ChoiceIntentTests
{
    private sealed class Listening : IRunEntityChooser
    {
        public RunChoiceIntent Heard = RunChoiceIntent.Keep;
        public int Asked;

        public IReadOnlyList<T> ChooseEntities<T>(IReadOnlyList<T> candidates, int count, string purpose) =>
            candidates.Take(count).ToArray();

        public IReadOnlyList<T> ChooseEntities<T>(
            IReadOnlyList<T> candidates, int count, string purpose, bool allowSkip, RunChoiceIntent intent)
        {
            Heard = intent;
            Asked++;
            return candidates.Take(count).ToArray();
        }
    }

    private static RunDefinitionRegistry BuildRegistry()
    {
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        return builder.Build();
    }

    private static RunState DeckOf(IRunEntityChooser chooser, params string[] kinds)
    {
        var run = new RunState(new RunId("run"), new HealthState(30, 40), new RunMap(Array.Empty<Node>()));
        foreach (var kind in kinds)
            run.AddDeckCard(new CardDefinitionId(kind));
        run.SetEntityChooser(chooser);
        return run;
    }

    [Fact]
    public void A_removal_tells_whoever_answers_that_it_is_a_removal()
    {
        var listening = new Listening();
        var run = DeckOf(listening, "strike", "curse");

        run.EnqueueEffect(new RemoveCardsRunEffect(
            RunSelectors.DeckCards.ChooseByPlayer(1, "remove a card")));
        new RunEffectProcessor().ResolvePending(run, BuildRegistry());

        Assert.Equal(1, listening.Asked);
        Assert.Equal(RunChoiceIntent.Remove, listening.Heard);
        Assert.Single(run.Deck);
    }

    // Everything else means what it always meant.
    [Fact]
    public void An_upgrade_is_still_a_keeping_choice()
    {
        var listening = new Listening();
        var run = DeckOf(listening, "strike", "defend");

        run.EnqueueEffect(new UpgradeCardsRunEffect(
            RunSelectors.DeckCards.ChooseByPlayer(1, "upgrade a card")));
        new RunEffectProcessor().ResolvePending(run, BuildRegistry());

        Assert.Equal(1, listening.Asked);
        Assert.Equal(RunChoiceIntent.Keep, listening.Heard);
    }
}
