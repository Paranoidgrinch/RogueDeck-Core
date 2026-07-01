using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Tests for the selector system (Phase F2): sources, filters, and the reduction modes (all / take / random /
// player choice). Deterministic modes resolve from RunState alone; the player-choice mode needs a chooser.
public class RunSelectorTests
{
    // A deterministic chooser for tests: takes the first `count` candidates.
    private sealed class FirstNChooser : IRunEntityChooser
    {
        public IReadOnlyList<T> ChooseEntities<T>(IReadOnlyList<T> candidates, int count, string purpose) =>
            candidates.Take(count).ToArray();
    }

    private static RunState NewRun(int seed = 1)
    {
        var map = new RunMap(Array.Empty<Node>());
        return new RunState(new RunId("run"), new HealthState(30, 40), map, randomSeed: seed);
    }

    private static RunState DeckOf(params string[] kinds)
    {
        var run = NewRun();
        foreach (var kind in kinds)
            run.AddDeckCard(new CardDefinitionId(kind));
        return run;
    }

    [Fact]
    public void DeckCards_selects_all_copies()
    {
        var run = DeckOf("a", "b", "c");
        var selected = RunSelectors.DeckCards.Select(run);
        Assert.Equal(3, selected.Count);
    }

    [Fact]
    public void Where_and_card_filters_narrow_the_source()
    {
        var run = DeckOf("strike", "strike", "defend");
        run.Deck[0].AddTag(new RunCardTagId("cursed"));

        Assert.Equal(2, RunSelectors.DeckCards.OfKind(new CardDefinitionId("strike")).Select(run).Count);
        Assert.Single(RunSelectors.DeckCards.WithTag(new RunCardTagId("cursed")).Select(run));
        Assert.Equal(3, RunSelectors.DeckCards.Upgradable().Select(run).Count);

        run.Deck[0].Upgrade();
        Assert.Equal(2, RunSelectors.DeckCards.Upgradable().Select(run).Count); // the upgraded one drops out
    }

    [Fact]
    public void Take_returns_the_first_n_in_source_order()
    {
        var run = DeckOf("a", "b", "c", "d");
        var taken = RunSelectors.DeckCards.Take(2).Select(run);
        Assert.Equal(
            new[] { new CardDefinitionId("a"), new CardDefinitionId("b") },
            taken.Select(c => c.DefinitionId).ToArray());
    }

    [Fact]
    public void Random_draws_distinct_entities_reproducibly_by_seed()
    {
        var runA = DeckOf("a", "b", "c", "d", "e");
        var runB = DeckOf("a", "b", "c", "d", "e");

        var pickA = RunSelectors.DeckCards.Random(3).Select(runA);
        var pickB = RunSelectors.DeckCards.Random(3).Select(runB);

        Assert.Equal(3, pickA.Count);
        Assert.Equal(3, pickA.Select(c => c.Id).Distinct().Count()); // distinct copies
        Assert.Equal(
            pickA.Select(c => c.Id).ToArray(),
            pickB.Select(c => c.Id).ToArray()); // same seed → same pick
    }

    [Fact]
    public void Random_clamps_to_available_and_handles_empty()
    {
        var run = DeckOf("a", "b");
        Assert.Equal(2, RunSelectors.DeckCards.Random(5).Select(run).Count); // clamped
        Assert.Empty(RunSelectors.DeckCards.WithTag(new RunCardTagId("none")).Random(3).Select(run));
    }

    [Fact]
    public void ChooseByPlayer_uses_the_chooser_and_requires_one()
    {
        var run = DeckOf("a", "b", "c");
        var selector = RunSelectors.DeckCards.ChooseByPlayer(2, "remove");

        var context = new RunSelectorContext(run, new FirstNChooser());
        var chosen = selector.Select(context);
        Assert.Equal(
            new[] { new CardDefinitionId("a"), new CardDefinitionId("b") },
            chosen.Select(c => c.DefinitionId).ToArray());

        // Without a chooser it is an author error.
        Assert.Throws<InvalidOperationException>(() => selector.Select(run));
    }
}
