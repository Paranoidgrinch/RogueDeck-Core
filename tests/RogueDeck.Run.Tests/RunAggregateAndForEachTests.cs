using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Tests for Phase F4: Count/Sum aggregate expressions over selectors, and ForEach-over-a-selector authored
// through the ExpandRunEffect substrate.
public class RunAggregateAndForEachTests
{
    private static readonly RunResourceId Gold = StandardRunIds.Gold;
    private static readonly RunCardTagId Curse = new("curse");

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

    [Fact]
    public void Count_counts_the_selection()
    {
        var run = DeckOf("strike", "curse", "curse");
        run.Deck[1].AddTag(Curse);
        run.Deck[2].AddTag(Curse);

        Assert.Equal(3, RunExpr.Count(RunSelectors.DeckCards).Evaluate(run));
        Assert.Equal(2, RunExpr.Count(RunSelectors.DeckCards.WithTag(Curse)).Evaluate(run));
    }

    [Fact]
    public void Count_drives_a_computed_value()
    {
        var registry = BuildRegistry();
        var run = DeckOf("curse", "curse", "strike");
        run.Deck[0].AddTag(Curse);
        run.Deck[1].AddTag(Curse);

        // Lose 3 HP per curse in the deck.
        run.EnqueueEffect(new ComputedDamageRunEffect(
            RunExpr.Multiply(RunExpr.Count(RunSelectors.DeckCards.WithTag(Curse)), RunExpr.Const(3))));
        Drain(run, registry);

        Assert.Equal(24, run.Health.Current); // 30 - 2*3
    }

    [Fact]
    public void Sum_totals_a_per_element_value()
    {
        var run = DeckOf("a", "b", "c");
        run.Deck[0].Upgrade(2);
        run.Deck[1].Upgrade();

        Assert.Equal(3, RunExpr.Sum(RunSelectors.DeckCards, c => c.UpgradeLevel).Evaluate(run));
    }

    [Fact]
    public void Count_of_a_player_choice_selector_is_an_author_error()
    {
        var run = DeckOf("a", "b");
        Assert.Throws<InvalidOperationException>(() =>
            RunExpr.Count(RunSelectors.DeckCards.ChooseByPlayer(1)).Evaluate(run));
    }

    [Fact]
    public void ForEachCard_applies_per_card_effects()
    {
        var registry = BuildRegistry();
        var run = DeckOf("a", "b", "c");

        // For each card, gain 5 gold and stamp memory — one block per selected card.
        var script = new EventScriptBuilder("s")
            .Situation("s", "t", s => s
                .Choice("harvest", c => c.ForEachCard(
                    RunSelectors.DeckCards,
                    card => new IRunEffectRequest[]
                    {
                        new ChangeResourceRunEffect(Gold, 5),
                        new SetCardMemoryRunEffect(RunSelectors.DeckCards.OfKind(card.DefinitionId), "harvested", 1),
                    })))
            .Build();

        var processor = new RunEffectProcessor();
        var node = new Node(new NodeId("n"), StandardRunIds.EventNode, script);
        var context = new NodeResolveContext(run, new ScriptedChoiceProvider("harvest"), registry, processor);
        new EventNodeResolver().Resolve(context, node);
        processor.ResolvePending(run, registry);

        Assert.Equal(15, run.GetResource(Gold)); // 5 per card * 3
        Assert.All(run.Deck, c => Assert.Equal(1, c.GetMemory("harvested")));
    }
}
