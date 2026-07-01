using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Tests for selector-based card effects (Phase F3): remove / upgrade / tag / transform over a selector,
// drained through the processor, plus player-choice removal wired through the run's chooser.
public class CardEffectTests
{
    private sealed class FirstNChooser : IRunEntityChooser
    {
        public IReadOnlyList<T> ChooseEntities<T>(IReadOnlyList<T> candidates, int count, string purpose) =>
            candidates.Take(count).ToArray();
    }

    private static RunDefinitionRegistry BuildRegistry()
    {
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        return builder.Build();
    }

    private static RunState DeckOf(params string[] kinds)
    {
        var map = new RunMap(Array.Empty<Node>());
        var run = new RunState(new RunId("run"), new HealthState(30, 40), map);
        foreach (var kind in kinds)
            run.AddDeckCard(new CardDefinitionId(kind));
        return run;
    }

    private static void Drain(RunState run, RunDefinitionRegistry registry) =>
        new RunEffectProcessor().ResolvePending(run, registry);

    private static IEnumerable<string> Kinds(RunState run) => run.Deck.Select(c => c.DefinitionId.ToString());

    [Fact]
    public void RemoveCards_removes_the_selected()
    {
        var registry = BuildRegistry();
        var run = DeckOf("strike", "strike", "curse");

        run.EnqueueEffect(new RemoveCardsRunEffect(
            RunSelectors.DeckCards.OfKind(new CardDefinitionId("curse"))));
        Drain(run, registry);

        Assert.Equal(new[] { "strike", "strike" }, Kinds(run));
        Assert.Single(run.EventHistory.OfType<CardRemovedFromDeckRunEvent>());
    }

    [Fact]
    public void UpgradeCards_raises_upgrade_level()
    {
        var registry = BuildRegistry();
        var run = DeckOf("strike", "defend");

        run.EnqueueEffect(new UpgradeCardsRunEffect(
            RunSelectors.DeckCards.OfKind(new CardDefinitionId("strike"))));
        Drain(run, registry);

        Assert.Equal(1, run.Deck.Single(c => c.DefinitionId == new CardDefinitionId("strike")).UpgradeLevel);
        Assert.Equal(0, run.Deck.Single(c => c.DefinitionId == new CardDefinitionId("defend")).UpgradeLevel);
    }

    [Fact]
    public void TagCards_and_UntagCards_change_tags()
    {
        var registry = BuildRegistry();
        var run = DeckOf("a", "b");
        var cursed = new RunCardTagId("cursed");

        run.EnqueueEffect(new TagCardsRunEffect(RunSelectors.DeckCards, cursed, true));
        Drain(run, registry);
        Assert.All(run.Deck, c => Assert.True(c.HasTag(cursed)));

        run.EnqueueEffect(new TagCardsRunEffect(
            RunSelectors.DeckCards.OfKind(new CardDefinitionId("a")), cursed, false));
        Drain(run, registry);
        Assert.False(run.Deck.Single(c => c.DefinitionId == new CardDefinitionId("a")).HasTag(cursed));
        Assert.True(run.Deck.Single(c => c.DefinitionId == new CardDefinitionId("b")).HasTag(cursed));
    }

    [Fact]
    public void TransformCards_replaces_with_a_new_copy()
    {
        var registry = BuildRegistry();
        var run = DeckOf("curse");
        var original = run.Deck[0].Id;

        run.EnqueueEffect(new TransformCardsRunEffect(
            RunSelectors.DeckCards.OfKind(new CardDefinitionId("curse")),
            RunPool.Uniform(new CardDefinitionId("blessing"))));
        Drain(run, registry);

        Assert.Equal(new[] { "blessing" }, Kinds(run));
        Assert.NotEqual(original, run.Deck[0].Id); // a new copy, not the old one
        Assert.Single(run.EventHistory.OfType<CardTransformedRunEvent>());
    }

    [Fact]
    public void ChooseByPlayer_removal_resolves_through_the_runs_chooser()
    {
        var registry = BuildRegistry();
        var run = DeckOf("a", "b", "c");
        run.SetEntityChooser(new FirstNChooser());

        // Player removes 2 cards; the chooser (first-N) picks a and b.
        run.EnqueueEffect(new RemoveCardsRunEffect(RunSelectors.DeckCards.ChooseByPlayer(2, "remove")));
        Drain(run, registry);

        Assert.Equal(new[] { "c" }, Kinds(run));
    }

    [Fact]
    public void Builder_sugar_authors_card_effects()
    {
        var registry = BuildRegistry();
        var processor = new RunEffectProcessor();
        var run = DeckOf("strike", "curse");
        run.SetEntityChooser(new FirstNChooser());

        var script = new EventScriptBuilder("altar")
            .Situation("altar", "t", s => s
                .Choice("cleanse", c => c
                    .RemoveCards(RunSelectors.DeckCards.OfKind(new CardDefinitionId("curse")))
                    .UpgradeCards(RunSelectors.DeckCards.OfKind(new CardDefinitionId("strike")))
                    .AddCard(new CardDefinitionId("blessing"))))
            .Build();

        var node = new Node(new NodeId("n"), StandardRunIds.EventNode, script);
        var context = new NodeResolveContext(run, new ScriptedChoiceProvider("cleanse"), registry, processor);
        new EventNodeResolver().Resolve(context, node);
        processor.ResolvePending(run, registry);

        Assert.Equal(new[] { "strike", "blessing" }, Kinds(run));
        Assert.Equal(1, run.Deck.Single(c => c.DefinitionId == new CardDefinitionId("strike")).UpgradeLevel);
    }
}
