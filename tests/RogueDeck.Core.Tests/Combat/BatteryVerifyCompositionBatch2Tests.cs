using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Verify-compose batch for the ✅? battery probes #27 / #30 / #45 — confirm they assemble from existing
// primitives with no new engine code.
public class BatteryVerifyCompositionBatch2Tests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    private static void Resolve(CombatState combat, CombatDefinitionRegistry registry) =>
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

    private static CardInstance AddCard(CombatState combat, CardDefinitionId cardId, CardZone zone)
    {
        var inst = new CardInstance(combat.CreateNextCardInstanceId(), cardId, HeroId, zone);
        combat.GetCardZones(HeroId).AddCard(inst);
        return inst;
    }

    // #27 Tinker: reduce the cost of cards by 1 this turn. Composes via a status carrying a declarative
    // CardCost passive-modifier spec (AddFlat −1) applied for the turn — the DeclarativePassiveCostModifier
    // reads the playing combatant's specs in the cost pipeline. No bespoke cost modifier needed.
    [Fact]
    public void Tinker_StatusWithCardCostSpec_ReducesPlayCost()
    {
        var tinker = new StatusDefinitionId("challenge.tinker");
        var cardId = new CardDefinitionId("challenge.costly");

        CombatDefinitionRegistry Build()
        {
            var builder = CombatTestFactory.CreateStandardBuilder();
            builder.RegisterStatus(new StatusDefinition(
                tinker, new PackageId("challenge"), "status.tinker.name", "status.tinker.desc",
                polarity: StatusPolarity.Buff, usesDuration: true,
                passiveModifiers: [new PassiveModifierSpec(PassiveModifierPipeline.CardCost, PassiveModifierOperation.AddFlat, -1)]));
            builder.RegisterCard(new CardDefinitionBuilder(cardId, new PackageId("challenge"), "card.n", "card.d")
            {
                Costs = { new ResourceCost(StandardCombatIds.EnergyResource, 2) },
                Program = new EffectProgram<CardPlayContext>(
                    new DealDamageNode<CardPlayContext>(CombatantTargetSelectors.EventTarget, new ConstantExpression<CardPlayContext>(1))),
            });
            return builder.Build();
        }

        // Control: full 2 cost (Tinker registered but not applied).
        var registryNoTinker = Build();
        var combat1 = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat1.GetCombatant(HeroId).SetResource(StandardCombatIds.EnergyResource, new ValuePoolState(3, max: 3));
        var inst1 = AddCard(combat1, cardId, CardZone.Hand);
        combat1.EnqueueEffect(new PlayCardEffectRequest(HeroId, inst1.Id, GoblinId));
        Resolve(combat1, registryNoTinker);
        Assert.Equal(1, combat1.GetCombatant(HeroId).Resources[StandardCombatIds.EnergyResource].Current); // 3 − 2

        // With Tinker: cost reduced to 1.
        var registryTinker = Build();
        var combat2 = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat2.GetCombatant(HeroId).SetResource(StandardCombatIds.EnergyResource, new ValuePoolState(3, max: 3));
        combat2.EnqueueEffect(new ApplyStatusEffectRequest(HeroId, tinker, DurationTurns: 1));
        Resolve(combat2, registryTinker);
        var inst2 = AddCard(combat2, cardId, CardZone.Hand);
        combat2.EnqueueEffect(new PlayCardEffectRequest(HeroId, inst2.Id, GoblinId));
        Resolve(combat2, registryTinker);
        Assert.Equal(2, combat2.GetCombatant(HeroId).Resources[StandardCombatIds.EnergyResource].Current); // 3 − 1
    }

    // #30 Recursion: whenever a card is exhausted, draw a card. Composes via a CardMovedToZone trigger
    // filtered to the exhaust zone → DrawCards.
    [Fact]
    public void Recursion_OnCardExhausted_DrawsACard()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.CardMovedToZone.Define(
                new TriggeredEffectDefinitionId("challenge.recursion"),
                new EffectProgram<CardMovedToZoneTriggeredEffectContext>(
                    new DrawCardsNode<CardMovedToZoneTriggeredEffectContext>(
                        CombatantTargetSelectors.Source, new ConstantExpression<CardMovedToZoneTriggeredEffectContext>(1))),
                filters: [new CardMovedToZoneToZoneTriggerFilter(CardZone.ExhaustPile)]));
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var toExhaust = AddCard(combat, StandardCombatIds.StrikeCard, CardZone.Hand);
        var inDraw = AddCard(combat, StandardCombatIds.StrikeCard, CardZone.DrawPile);

        // Exhaust the hand card → the recursion trigger draws one from the draw pile.
        combat.EnqueueEffect(new MoveCardToZoneEffectRequest(HeroId, toExhaust.Id, CardZone.ExhaustPile));
        Resolve(combat, registry);

        var zones = combat.GetCardZones(HeroId);
        Assert.Contains(zones.ExhaustPile, c => c.Id == toExhaust.Id);
        Assert.Contains(zones.Hand, c => c.Id == inDraw.Id); // drawn by recursion
        Assert.Empty(zones.DrawPile);
    }

    // #45 Investment: gain resource now, and more at the start of next turn. Composes via a one-shot
    // TurnStarted temporary rule installed when the card is played. A custom resource avoids the standard
    // energy refill so the delayed gain is observable.
    [Fact]
    public void Investment_InstallsOneShotNextTurnResourceGain()
    {
        var mana = new ResourceId("challenge.mana");
        var cardId = new CardDefinitionId("challenge.investment");
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterCard(new CardDefinitionBuilder(cardId, new PackageId("challenge"), "card.n", "card.d")
        {
            Program = new EffectProgram<CardPlayContext>(
                new CausalSequenceEffectNode<CardPlayContext>([
                    new GainResourceNode<CardPlayContext>(
                        CombatantTargetSelectors.Source, mana, new ConstantExpression<CardPlayContext>(2), defaultMax: 100),
                    new InstallTemporaryRuleNode<CardPlayContext>(
                        TriggeredProgramContextAdapters.TurnStarted.Define(
                            new TriggeredEffectDefinitionId("challenge.investment_payout"),
                            new EffectProgram<TurnStartedTriggeredEffectContext>(
                                new GainResourceNode<TurnStartedTriggeredEffectContext>(
                                    CombatantTargetSelectors.Source, mana, new ConstantExpression<TurnStartedTriggeredEffectContext>(4), defaultMax: 100))),
                        TemporaryRuleLifetime.OneShot),
                ])),
        });
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHero(); // hero-only: only the hero's turn starts
        var hero = combat.GetCombatant(HeroId);
        hero.AddResource(mana, new ValuePoolState(0, max: 100));
        var turn = new CombatTurnProcessor();

        turn.StartCurrentTurn(combat, registry);
        var inst = AddCard(combat, cardId, CardZone.Hand);
        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, inst.Id, HeroId));
        Resolve(combat, registry);
        Assert.Equal(2, hero.Resources[mana].Current); // immediate gain

        turn.EndCurrentTurnAndStartNextTurn(combat, registry); // next hero turn → payout fires
        Assert.Equal(6, hero.Resources[mana].Current); // 2 + 4

        turn.EndCurrentTurnAndStartNextTurn(combat, registry); // one-shot consumed → no further gain
        Assert.Equal(6, hero.Resources[mana].Current);
    }
}
