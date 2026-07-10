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

    private static CardInstanceId AddCard(CombatState combat, string id, CardZone zone)
    {
        var instanceId = new CardInstanceId(id);
        combat.GetCardZones(HeroId).AddCard(
            new CardInstance(instanceId, new CardDefinitionId("test.card"), HeroId, zone));
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
}
