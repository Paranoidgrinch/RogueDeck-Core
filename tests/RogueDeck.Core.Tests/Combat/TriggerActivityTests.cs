using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// CombatState.TriggerActivity: how often each trigger has DONE something — ran and enqueued at least one effect.
// It is what a host lights a relic up by, so the line it draws is the one a player sees: a rule that fires and
// pays counts, a rule that fires and pays nothing (its own condition said no) does not.
public class TriggerActivityTests
{
    private static readonly CombatantId HeroId = new("hero_001");

    private static void Resolve(CombatState combat, CombatDefinitionRegistry registry) =>
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

    private static CardInstance AddCard(CombatState combat, CardDefinitionId cardId, CardZone zone)
    {
        var inst = new CardInstance(combat.CreateNextCardInstanceId(), cardId, HeroId, zone);
        combat.GetCardZones(HeroId).AddCard(inst);
        return inst;
    }

    [Fact]
    public void A_trigger_counts_when_it_pays_and_not_when_its_own_condition_says_no()
    {
        var pays = new TriggeredEffectDefinitionId("activity.pays");
        var idle = new TriggeredEffectDefinitionId("activity.idle");
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.CardMovedToZone.Define(
                pays,
                new EffectProgram<CardMovedToZoneTriggeredEffectContext>(
                    new DrawCardsNode<CardMovedToZoneTriggeredEffectContext>(
                        CombatantTargetSelectors.Source,
                        new ConstantExpression<CardMovedToZoneTriggeredEffectContext>(1))),
                filters: [new CardMovedToZoneToZoneTriggerFilter(CardZone.ExhaustPile)]));
        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.CardMovedToZone.Define(
                idle,
                new EffectProgram<CardMovedToZoneTriggeredEffectContext>(
                    new ConditionalEffectNode<CardMovedToZoneTriggeredEffectContext>(
                        new ComparisonExpression<CardMovedToZoneTriggeredEffectContext>(
                            new ConstantExpression<CardMovedToZoneTriggeredEffectContext>(0),
                            ComparisonOperator.Greater,
                            new ConstantExpression<CardMovedToZoneTriggeredEffectContext>(1)),
                        new DrawCardsNode<CardMovedToZoneTriggeredEffectContext>(
                            CombatantTargetSelectors.Source,
                            new ConstantExpression<CardMovedToZoneTriggeredEffectContext>(1)))),
                filters: [new CardMovedToZoneToZoneTriggerFilter(CardZone.ExhaustPile)]));
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var first = AddCard(combat, StandardCombatIds.StrikeCard, CardZone.Hand);
        var second = AddCard(combat, StandardCombatIds.StrikeCard, CardZone.Hand);
        AddCard(combat, StandardCombatIds.StrikeCard, CardZone.DrawPile);
        AddCard(combat, StandardCombatIds.StrikeCard, CardZone.DrawPile);

        Assert.Empty(combat.TriggerActivity);
        combat.EnqueueEffect(new MoveCardToZoneEffectRequest(HeroId, first.Id, CardZone.ExhaustPile));
        Resolve(combat, registry);
        combat.EnqueueEffect(new MoveCardToZoneEffectRequest(HeroId, second.Id, CardZone.ExhaustPile));
        Resolve(combat, registry);

        Assert.Equal(2, combat.TriggerActivity.GetValueOrDefault(pays));
        Assert.Equal(0, combat.TriggerActivity.GetValueOrDefault(idle));
    }
}
