using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Battery probe #6 Overload: gain 3 energy now; at end of turn, lose all remaining energy and take
// 2 damage per energy lost. Verifies the full chain with no engine change — a card grants energy +
// a marker, and a real TurnEnded trigger (driven through CombatTurnProcessor) reads/loses the energy
// and scales self-damage off the actual LoseResource outcome.
public class OverloadCompositionTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    [Fact]
    public void Overload_AtEndOfTurnLosesAllEnergyAndTakesTwoDamagePerEnergy()
    {
        var marker = new StatusDefinitionId("challenge.overload");
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterStatus(new StatusDefinition(
            marker, new PackageId("challenge"), "status.overload.name", "status.overload.desc",
            polarity: StatusPolarity.Neutral, usesDuration: true));

        // The card: gain 3 energy now + arm the end-of-turn rule (the marker).
        var cardId = new CardDefinitionId("challenge.overload_card");
        builder.RegisterCard(new CardDefinitionBuilder(cardId, new PackageId("challenge"), "card.n", "card.d")
        {
            Program = new EffectProgram<CardPlayContext>(
                new SequenceEffectNode<CardPlayContext>([
                    new GainResourceNode<CardPlayContext>(
                        CombatantTargetSelectors.Source, StandardCombatIds.EnergyResource,
                        new ConstantExpression<CardPlayContext>(3)),
                    new ApplyStatusNode<CardPlayContext>(
                        CombatantTargetSelectors.Source, marker,
                        new ConstantExpression<CardPlayContext>(1), durationTurns: 2),
                ])),
        });

        // The end-of-turn payload: lose all energy (a large request caps at the current amount), then
        // deal 2 × the amount actually lost back to the wearer.
        var lost = new EffectResultKey<OrderedTargetOutcomes<LoseResourceOutcome>>("overload_lost");
        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.TurnEnded.Define(
                new TriggeredEffectDefinitionId("challenge.overload_trigger"),
                new EffectProgram<TurnEndedTriggeredEffectContext>(
                    new CausalSequenceEffectNode<TurnEndedTriggeredEffectContext>([
                        new LoseResourceNode<TurnEndedTriggeredEffectContext>(
                            CombatantTargetSelectors.Source, StandardCombatIds.EnergyResource,
                            new ConstantExpression<TurnEndedTriggeredEffectContext>(999), resultKey: lost),
                        new DealDamageNode<TurnEndedTriggeredEffectContext>(
                            CombatantTargetSelectors.Source,
                            new MultiplyExpression<TurnEndedTriggeredEffectContext>(
                                new ConstantExpression<TurnEndedTriggeredEffectContext>(2),
                                new PreviousOutcomeFieldExpression<TurnEndedTriggeredEffectContext, LoseResourceOutcome>(
                                    lost, o => o.LostAmount))),
                    ])),
                filters: [new TurnEndedCombatantHasStatusTriggerFilter(marker)]));
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);
        hero.Health.SetMax(50);
        hero.Health.SetCurrent(50);
        hero.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(0, max: 10));

        var processor = new CombatTurnProcessor();
        processor.StartCurrentTurn(combat, registry);
        // Pin a deterministic base of 1 energy after the turn-start refill, so the card's +3 → 4.
        hero.Resources[StandardCombatIds.EnergyResource].SetCurrent(1);

        var card = new CardInstance(combat.CreateNextCardInstanceId(), cardId, HeroId, CardZone.Hand);
        combat.GetCardZones(HeroId).AddCard(card);
        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, card.Id, GoblinId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
        Assert.Equal(4, hero.Resources[StandardCombatIds.EnergyResource].Current); // 1 + 3

        processor.EndCurrentTurn(combat, registry); // TurnEnded → lose 4, take 2 × 4 = 8

        Assert.Equal(0, hero.Resources[StandardCombatIds.EnergyResource].Current);
        Assert.Equal(42, hero.Health.Current); // 50 − 8
    }
}
