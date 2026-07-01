using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Tests for the data ForEach (R3): apply effect templates to each selected card with that card in scope.
// "This card" templates target the exact copy (surviving to drain via its instance id), and value templates
// read the card via CardValue.
public class ForEachCardTests
{
    private static readonly RunResourceId Gold = StandardRunIds.Gold;
    private static readonly RunCardTagId Blessed = new("blessed");

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
    public void ForEach_upgrades_and_tags_each_selected_card()
    {
        var registry = BuildRegistry();
        var run = DeckOf("strike", "strike", "defend");

        run.EnqueueEffect(new ForEachCardRunEffect(
            RunSelectors.DeckCards.OfKind(new CardDefinitionId("strike")),
            new[] { RunEffectTemplates.UpgradeThisCard(), RunEffectTemplates.TagThisCard(Blessed) }));
        Drain(run, registry);

        foreach (var card in run.Deck.Where(c => c.DefinitionId == new CardDefinitionId("strike")))
        {
            Assert.Equal(1, card.UpgradeLevel);
            Assert.True(card.HasTag(Blessed));
        }
        // The unselected card is untouched.
        var defend = run.Deck.Single(c => c.DefinitionId == new CardDefinitionId("defend"));
        Assert.Equal(0, defend.UpgradeLevel);
        Assert.False(defend.HasTag(Blessed));
    }

    [Fact]
    public void ForEach_this_card_targets_the_exact_copy()
    {
        var registry = BuildRegistry();
        var run = DeckOf("strike", "strike");
        run.Deck[0].SetMemory("mark", 1); // only the first copy is marked

        // Upgrade only marked copies (memory 'mark' >= 1).
        run.EnqueueEffect(new ForEachCardRunEffect(
            RunSelectors.DeckCards.Matching(RunExpr.GreaterOrEqual(CardValue.Memory("mark"), RunExpr.Const(1))),
            new[] { RunEffectTemplates.UpgradeThisCard() }));
        Drain(run, registry);

        Assert.Equal(1, run.Deck[0].UpgradeLevel);
        Assert.Equal(0, run.Deck[1].UpgradeLevel); // sibling copy untouched
    }

    [Fact]
    public void ForEach_value_template_reads_the_card()
    {
        var registry = BuildRegistry();
        var run = DeckOf("a", "b", "c");
        run.Deck[0].Upgrade(2);
        run.Deck[1].Upgrade();

        // Gain gold equal to each card's upgrade level (2 + 1 + 0 = 3).
        run.EnqueueEffect(new ForEachCardRunEffect(
            RunSelectors.DeckCards,
            new[] { RunEffectTemplates.GainResource(Gold, CardValue.UpgradeLevel) }));
        Drain(run, registry);

        Assert.Equal(3, run.GetResource(Gold));
    }

    [Fact]
    public void ForEach_this_card_transform_replaces_each_selected()
    {
        var registry = BuildRegistry();
        var run = DeckOf("curse", "curse", "strike");

        run.EnqueueEffect(new ForEachCardRunEffect(
            RunSelectors.DeckCards.OfKind(new CardDefinitionId("curse")),
            new[] { RunEffectTemplates.TransformThisCard(RunPool.Uniform(new CardDefinitionId("blessing"))) }));
        Drain(run, registry);

        Assert.Equal(2, run.Deck.Count(c => c.DefinitionId == new CardDefinitionId("blessing")));
        Assert.Equal(0, run.Deck.Count(c => c.DefinitionId == new CardDefinitionId("curse")));
    }

    [Fact]
    public void Builder_sugar_authors_a_data_foreach()
    {
        var registry = BuildRegistry();
        var processor = new RunEffectProcessor();
        var run = DeckOf("strike", "strike");

        var script = new EventScriptBuilder("blessing")
            .Situation("blessing", "t", s => s
                .Choice("bless", c => c.ForEachCard(
                    RunSelectors.DeckCards, RunEffectTemplates.UpgradeThisCard())))
            .Build();

        var node = new Node(new NodeId("n"), StandardRunIds.EventNode, script);
        var context = new NodeResolveContext(run, new ScriptedChoiceProvider("bless"), registry, processor);
        new EventNodeResolver().Resolve(context, node);
        processor.ResolvePending(run, registry);

        Assert.All(run.Deck, c => Assert.Equal(1, c.UpgradeLevel));
    }
}
