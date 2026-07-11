using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// In-combat card targeting (Tier-2 card domain), slice 1: CardInZoneExpression selects a card by position from a
// zone of the acting combatant, so a card operation can point at a card living in hand/draw/discard — not only a
// contextually-known card. Proven by feeding it into the existing MoveCardToZone operation to realise real card
// mechanics ("exhaust the first card in your hand", "put the top of your draw on top of hand").
public class CardInZoneExpressionTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    private sealed record Ctx;

    private static EffectExecutionContext<Ctx> MakeContext(CombatState combat) =>
        new(new Ctx(),
            new TriggeredEffectActionBuildContext(
                new CombatantTargetSelectionContext(
                    Combat: combat,
                    Source: combat.GetCombatant(HeroId),
                    EventTargetId: GoblinId),
                new TriggeredEffectActionSource(SourceCombatantId: HeroId)));

    private static CardInstanceId AddCard(CombatState combat, string id, CardZone zone, string definition = "test.card")
    {
        var instanceId = new CardInstanceId(id);
        combat.GetCardZones(HeroId).AddCard(
            new CardInstance(instanceId, new CardDefinitionId(definition), HeroId, zone));
        return instanceId;
    }

    [Fact]
    public void Exhausts_the_first_card_in_the_source_combatants_hand()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var first = AddCard(combat, "h1", CardZone.Hand);
        var second = AddCard(combat, "h2", CardZone.Hand);

        var moveKey = new EffectResultKey<MoveCardToZoneOutcome>("move");
        var program = new EffectProgram<Ctx>(new MoveCardToZoneNode<Ctx>(
            CombatantTargetSelectors.Source,
            new CardInZoneExpression<Ctx>(CardZone.Hand, index: 0),
            CardZone.ExhaustPile,
            resultKey: moveKey));

        EffectProgramExecutor.Execute(program, MakeContext(combat), combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var zones = combat.GetCardZones(HeroId);
        Assert.Equal(new[] { second }, zones.Hand.Select(c => c.Id));     // the first hand card left
        Assert.Equal(new[] { first }, zones.ExhaustPile.Select(c => c.Id)); // it landed in exhaust
    }

    [Fact]
    public void Selects_a_card_by_index_within_the_zone()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        AddCard(combat, "d1", CardZone.DrawPile);
        var top2 = AddCard(combat, "d2", CardZone.DrawPile); // index 1
        AddCard(combat, "d3", CardZone.DrawPile);

        var program = new EffectProgram<Ctx>(new MoveCardToZoneNode<Ctx>(
            CombatantTargetSelectors.Source,
            new CardInZoneExpression<Ctx>(CardZone.DrawPile, index: 1),
            CardZone.Hand));

        EffectProgramExecutor.Execute(program, MakeContext(combat), combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(new[] { top2 }, combat.GetCardZones(HeroId).Hand.Select(c => c.Id));
        Assert.Equal(2, combat.GetCardZones(HeroId).DrawPile.Count);
    }

    // A deterministic test chooser that always picks the candidate with the given instance id.
    private sealed class PicksCard : ICombatCardChooser
    {
        private readonly string _id;
        public PicksCard(string id) => _id = id;
        public IReadOnlyList<CardInstanceId> ChooseCards(
            IReadOnlyList<CardInstance> candidates, int count, string purpose) =>
            candidates.Where(c => c.Id.value == _id).Take(count).Select(c => c.Id).ToArray();
    }

    [Fact]
    public void A_chosen_card_lets_the_player_pick_which_card_in_hand()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        AddCard(combat, "h1", CardZone.Hand);
        var picked = AddCard(combat, "h2", CardZone.Hand);
        AddCard(combat, "h3", CardZone.Hand);
        combat.SetCardChooser(new PicksCard("h2"));

        var program = new EffectProgram<Ctx>(new MoveCardToZoneNode<Ctx>(
            CombatantTargetSelectors.Source,
            new ChosenCardInZoneExpression<Ctx>(CardZone.Hand, "upgrade a card"),
            CardZone.ExhaustPile));

        EffectProgramExecutor.Execute(program, MakeContext(combat), combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(new[] { picked }, combat.GetCardZones(HeroId).ExhaustPile.Select(c => c.Id)); // the player's pick
        Assert.Equal(2, combat.GetCardZones(HeroId).Hand.Count);
    }

    [Fact]
    public void A_chosen_card_falls_back_to_the_first_candidate_with_no_chooser()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var first = AddCard(combat, "h1", CardZone.Hand);
        AddCard(combat, "h2", CardZone.Hand);
        // no chooser set → headless default

        var program = new EffectProgram<Ctx>(new MoveCardToZoneNode<Ctx>(
            CombatantTargetSelectors.Source,
            new ChosenCardInZoneExpression<Ctx>(CardZone.Hand),
            CardZone.ExhaustPile));

        EffectProgramExecutor.Execute(program, MakeContext(combat), combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(new[] { first }, combat.GetCardZones(HeroId).ExhaustPile.Select(c => c.Id));
    }

    [Fact]
    public void A_random_card_pick_is_deterministic_by_seed()
    {
        static CardInstanceId? Exhausted()
        {
            var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin(); // fixed seed inside the factory
            for (var i = 0; i < 5; i++)
                combat.GetCardZones(HeroId).AddCard(
                    new CardInstance(new CardInstanceId($"h{i}"), new CardDefinitionId("test.card"), HeroId, CardZone.Hand));

            var program = new EffectProgram<Ctx>(new MoveCardToZoneNode<Ctx>(
                CombatantTargetSelectors.Source,
                new RandomCardInZoneExpression<Ctx>(CardZone.Hand),
                CardZone.ExhaustPile));
            EffectProgramExecutor.Execute(program, MakeContext(combat), combat);
            new CombatQueueProcessor().ResolvePendingQueues(combat, CombatTestFactory.CreateStandardRegistry());
            return combat.GetCardZones(HeroId).ExhaustPile.SingleOrDefault()?.Id;
        }

        Assert.NotNull(Exhausted());
        Assert.Equal(Exhausted(), Exhausted());   // same seed ⇒ same pick, reproducibly
    }

    [Fact]
    public void Transforms_a_chosen_card_into_a_different_definition()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var target = AddCard(combat, "h1", CardZone.Hand); // definition "test.card"

        var upgraded = new CardDefinitionId("test.card.plus");
        var key = new EffectResultKey<TransformCardOutcome>("xf");
        var program = new EffectProgram<Ctx>(new TransformCardNode<Ctx>(
            CombatantTargetSelectors.Source,
            new ChosenCardInZoneExpression<Ctx>(CardZone.Hand),
            upgraded,
            resultKey: key));

        var ctx = MakeContext(combat);
        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // The instance keeps its id + zone but now plays as the upgraded definition.
        Assert.Equal(upgraded, combat.GetCardZones(HeroId).GetCard(target).DefinitionId);
        Assert.True(ctx.TryGet(key, out var outcome));
        Assert.True(outcome!.WasTransformed);
        Assert.Equal(new CardDefinitionId("test.card"), outcome.PreviousDefinition);
        Assert.Equal(upgraded, outcome.CurrentDefinition);
    }

    [Fact]
    public void Transforming_to_the_same_definition_is_a_no_op()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        AddCard(combat, "h1", CardZone.Hand);

        var key = new EffectResultKey<TransformCardOutcome>("xf");
        var program = new EffectProgram<Ctx>(new TransformCardNode<Ctx>(
            CombatantTargetSelectors.Source,
            new CardInZoneExpression<Ctx>(CardZone.Hand, 0),
            new CardDefinitionId("test.card"), // same as current
            resultKey: key));

        var ctx = MakeContext(combat);
        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.True(ctx.TryGet(key, out var outcome));
        Assert.False(outcome!.WasTransformed);
    }

    [Fact]
    public void An_out_of_range_index_selects_no_card_and_moves_nothing()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        AddCard(combat, "h1", CardZone.Hand);

        var moveKey = new EffectResultKey<MoveCardToZoneOutcome>("move");
        var program = new EffectProgram<Ctx>(new MoveCardToZoneNode<Ctx>(
            CombatantTargetSelectors.Source,
            new CardInZoneExpression<Ctx>(CardZone.Hand, index: 5), // past the end
            CardZone.ExhaustPile,
            resultKey: moveKey));

        EffectProgramExecutor.Execute(program, MakeContext(combat), combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Single(combat.GetCardZones(HeroId).Hand);          // nothing moved
        Assert.Empty(combat.GetCardZones(HeroId).ExhaustPile);
    }

    // Multi-card selection: ForEachCardInZone runs its body once per card in a zone. With a definition filter it
    // touches only matching cards (Armaments+ "upgrade every Strike in hand"), leaving the rest alone; the body
    // targets the current card via IteratedCardExpression.
    [Fact]
    public void ForEachCardInZone_upgrades_every_matching_card_and_leaves_others_untouched()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var s1 = AddCard(combat, "s1", CardZone.Hand, "strike");
        var defend = AddCard(combat, "d1", CardZone.Hand, "defend");
        var s2 = AddCard(combat, "s2", CardZone.Hand, "strike");

        var program = new EffectProgram<Ctx>(new ForEachCardInZoneNode<Ctx>(
            CombatantTargetSelectors.Source,
            CardZone.Hand,
            new TransformCardNode<Ctx>(
                CombatantTargetSelectors.Source,
                new IteratedCardExpression<Ctx>(),
                new CardDefinitionId("strike.plus")),
            definitionFilter: new CardDefinitionId("strike")));

        EffectProgramExecutor.Execute(program, MakeContext(combat), combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var zones = combat.GetCardZones(HeroId);
        Assert.Equal(new CardDefinitionId("strike.plus"), zones.GetCard(s1).DefinitionId); // every Strike upgraded
        Assert.Equal(new CardDefinitionId("strike.plus"), zones.GetCard(s2).DefinitionId);
        Assert.Equal(new CardDefinitionId("defend"), zones.GetCard(defend).DefinitionId);  // the non-Strike left alone
    }

    // With no filter the body runs for every card in the zone — "exhaust your whole hand".
    [Fact]
    public void ForEachCardInZone_applies_the_body_to_all_cards_when_unfiltered()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        AddCard(combat, "h1", CardZone.Hand);
        AddCard(combat, "h2", CardZone.Hand);
        AddCard(combat, "h3", CardZone.Hand);

        var program = new EffectProgram<Ctx>(new ForEachCardInZoneNode<Ctx>(
            CombatantTargetSelectors.Source,
            CardZone.Hand,
            new MoveCardToZoneNode<Ctx>(
                CombatantTargetSelectors.Source,
                new IteratedCardExpression<Ctx>(),
                CardZone.ExhaustPile)));

        EffectProgramExecutor.Execute(program, MakeContext(combat), combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Empty(combat.GetCardZones(HeroId).Hand);
        Assert.Equal(3, combat.GetCardZones(HeroId).ExhaustPile.Count);
    }
}
