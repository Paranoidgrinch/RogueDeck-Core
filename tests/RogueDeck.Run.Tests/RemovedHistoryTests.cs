using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// The removed history: what a run no longer has, and what it was. A deck is not the only place a card can be —
// a game whose events offer to give one back has to know which card, how far it had been improved, and what was
// written on it. Written by every permanent removal, struck out by the recovery that uses it.
public class RemovedHistoryTests
{
    private static readonly CardDefinitionId Strike = new("strike");

    private static RunDefinitionRegistry Registry()
    {
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        return builder.Build();
    }

    private static RunState NewRun() =>
        new(new RunId("run"), new HealthState(30, 40), new RunMap(Array.Empty<Node>()));

    private static void Drain(RunState run, RunDefinitionRegistry registry) =>
        new RunEffectProcessor().ResolvePending(run, registry);

    [Fact]
    public void A_removed_card_is_remembered_as_it_stood()
    {
        var registry = Registry();
        var run = NewRun();
        var card = run.AddDeckCard(Strike);
        card.Upgrade(1);
        card.AddTag(new RunCardTagId("true_name"));

        run.EnqueueEffect(new RemoveCardsRunEffect(RunSelectors.DeckCards));
        Drain(run, registry);

        Assert.Empty(run.Deck);
        var remembered = Assert.Single(run.RemovedCards);
        Assert.Equal(Strike, remembered.Definition);
        Assert.Equal(1, remembered.UpgradeLevel);
        Assert.Contains(new RunCardTagId("true_name"), remembered.Tags);
    }

    [Fact]
    public void Recovering_a_card_gives_it_back_the_way_it_left_and_strikes_the_entry_out()
    {
        var registry = Registry();
        var run = NewRun();
        run.AddDeckCard(Strike).Upgrade(1);
        run.EnqueueEffect(new RemoveCardsRunEffect(RunSelectors.DeckCards));
        Drain(run, registry);

        run.EnqueueEffect(new RestoreRemovedCardRunEffect());
        Drain(run, registry);

        var back = Assert.Single(run.Deck);
        Assert.Equal(Strike, back.DefinitionId);
        Assert.Equal(1, back.UpgradeLevel);
        // A card can be recovered ONCE.
        Assert.Empty(run.RemovedCards);
    }

    // The Librarian's branch: restored, one further improvement, and its true name.
    [Fact]
    public void A_recovery_may_add_an_improvement_and_an_inscription()
    {
        var registry = Registry();
        var run = NewRun();
        run.AddDeckCard(Strike);
        run.EnqueueEffect(new RemoveCardsRunEffect(RunSelectors.DeckCards));
        Drain(run, registry);

        run.EnqueueEffect(new RestoreRemovedCardRunEffect(ExtraUpgrades: 1, Tags: ["true_name"]));
        Drain(run, registry);

        var back = Assert.Single(run.Deck);
        Assert.Equal(1, back.UpgradeLevel);
        Assert.Contains(new RunCardTagId("true_name"), back.Tags);
    }

    [Fact]
    public void Recovering_from_an_empty_history_does_nothing()
    {
        var registry = Registry();
        var run = NewRun();

        run.EnqueueEffect(new RestoreRemovedCardRunEffect());
        Drain(run, registry);

        Assert.Empty(run.Deck);
    }

    // "If the history has entries…" — the question an event asks before offering the branch at all.
    [Fact]
    public void The_history_can_be_counted()
    {
        var registry = Registry();
        var run = NewRun();
        var count = new RemovedCardCountExpression();
        Assert.Equal(0, count.Evaluate(run.SelectorContext));

        run.AddDeckCard(Strike);
        run.EnqueueEffect(new RemoveCardsRunEffect(RunSelectors.DeckCards));
        Drain(run, registry);

        Assert.Equal(1, count.Evaluate(run.SelectorContext));
    }

    // The history is part of the run, so it has to survive being written to disk.
    [Fact]
    public void The_history_rides_through_a_save()
    {
        var registry = Registry();
        var map = new RunMap(Array.Empty<Node>());
        var run = NewRun();
        run.AddDeckCard(Strike).Upgrade(1);
        run.EnqueueEffect(new RemoveCardsRunEffect(RunSelectors.DeckCards));
        Drain(run, registry);

        var json = RunSaveJson.ToJson(run.Snapshot());
        var restored = RunState.Restore(RunSaveJson.FromJson(json), map, null);

        var remembered = Assert.Single(restored.RemovedCards);
        Assert.Equal(Strike, remembered.Definition);
        Assert.Equal(1, remembered.UpgradeLevel);
        Assert.Equal(json, RunSaveJson.ToJson(restored.Snapshot()));
    }

    // A save from a run that has removed nothing is written exactly as it always was.
    [Fact]
    public void A_run_that_removed_nothing_writes_no_history()
    {
        Assert.DoesNotContain("RemovedCards", RunSaveJson.ToJson(NewRun().Snapshot()));
    }
}
