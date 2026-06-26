using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

/// <summary>
/// Tests for PlayCardEffectRequest/Handler and PlayCardNode (§11.9 item 14).
/// </summary>
public class PlayCardOperationTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    // ── PlayCardEffectHandler: success cases ──────────────────────────────────

    [Fact]
    public void PlayCard_FreeCard_DealsDamageAndMovesToDiscard()
    {
        var (builder, combat) = Setup();

        var cardId = new CardDefinitionId("test.play_strike");
        var card = BuildFreeCard(cardId, new EffectProgram<CardPlayContext>(
            new DealDamageNode<CardPlayContext>(
                CombatantTargetSelectors.EventTarget,
                new ConstantExpression<CardPlayContext>(5))));

        builder.RegisterCard(card);

        var instance = AddCardToHand(combat, HeroId, cardId);
        var slot = new PlayCardOutcomeSlot();

        combat.EnqueueEffect(new PlayCardEffectRequest(
            PlayerId: HeroId,
            CardInstanceId: instance.Id,
            TargetCombatantId: GoblinId,
            OutcomeSlot: slot));

        new CombatQueueProcessor().ResolvePendingQueues(combat, builder.Build());

        Assert.True(slot.Value?.WasPlayed);
        Assert.Equal(slot.Value?.CardInstanceId, instance.Id);
        Assert.Equal(12 - 5, combat.GetCombatant(GoblinId).Health.Current);
        Assert.Equal(CardZone.DiscardPile, instance.Zone);
        Assert.Empty(combat.GetCardZones(HeroId).Hand);
    }

    [Fact]
    public void PlayCard_PaysCostFromResource()
    {
        var (builder, combat) = Setup();

        var cardId = new CardDefinitionId("test.cost_strike");
        var card = BuildCardWithEnergyCost(cardId, cost: 2, new EffectProgram<CardPlayContext>(
            new NoOpEffectNode<CardPlayContext>()));

        builder.RegisterCard(card);

        GiveEnergy(combat, HeroId, current: 3, max: 3);
        var instance = AddCardToHand(combat, HeroId, cardId);

        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, instance.Id));
        new CombatQueueProcessor().ResolvePendingQueues(combat, builder.Build());

        var energy = combat.GetCombatant(HeroId).Resources[StandardCombatIds.EnergyResource];
        Assert.Equal(1, energy.Current);
    }

    [Fact]
    public void PlayCard_FiresCardPlayedEvent()
    {
        var (builder, combat) = Setup();

        var cardId = new CardDefinitionId("test.event_card");
        var card = BuildFreeCard(cardId, new EffectProgram<CardPlayContext>(new NoOpEffectNode<CardPlayContext>()));
        builder.RegisterCard(card);

        var instance = AddCardToHand(combat, HeroId, cardId);
        var eventsRaised = new List<ICombatEvent>();

        // Capture events by registering a handler for CardPlayedCombatEvent
        var captureHandler = new CaptureEventHandler<CardPlayedCombatEvent>(eventsRaised);
        builder.RegisterCombatEventHandler(captureHandler);

        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, instance.Id));
        new CombatQueueProcessor().ResolvePendingQueues(combat, builder.Build());

        Assert.Contains(eventsRaised, e => e is CardPlayedCombatEvent cpe && cpe.CardDefinitionId == cardId);
    }

    // ── PlayCardEffectHandler: no-op cases ────────────────────────────────────

    [Fact]
    public void PlayCard_CardNotInHand_NoOp_WasPlayedFalse()
    {
        var (builder, combat) = Setup();

        var cardId = new CardDefinitionId("test.nohand_card");
        var card = BuildFreeCard(cardId, new EffectProgram<CardPlayContext>(
            new DealDamageNode<CardPlayContext>(CombatantTargetSelectors.EventTarget, new ConstantExpression<CardPlayContext>(5))));
        builder.RegisterCard(card);

        // Put card in DrawPile, not Hand
        var instance = new CardInstance(combat.CreateNextCardInstanceId(), cardId, HeroId, CardZone.DrawPile);
        combat.GetCardZones(HeroId).AddCard(instance);

        var slot = new PlayCardOutcomeSlot();
        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, instance.Id, GoblinId, slot));
        new CombatQueueProcessor().ResolvePendingQueues(combat, builder.Build());

        Assert.False(slot.Value?.WasPlayed);
        Assert.Equal(12, combat.GetCombatant(GoblinId).Health.Current);
        Assert.Equal(CardZone.DrawPile, instance.Zone);
    }

    [Fact]
    public void PlayCard_InsufficientEnergy_NoOp_WasPlayedFalse()
    {
        var (builder, combat) = Setup();

        var cardId = new CardDefinitionId("test.expensive_card");
        var card = BuildCardWithEnergyCost(cardId, cost: 3, new EffectProgram<CardPlayContext>(
            new DealDamageNode<CardPlayContext>(CombatantTargetSelectors.EventTarget, new ConstantExpression<CardPlayContext>(5))));
        builder.RegisterCard(card);

        GiveEnergy(combat, HeroId, current: 1, max: 3);  // only 1 energy, need 3
        var instance = AddCardToHand(combat, HeroId, cardId);

        var slot = new PlayCardOutcomeSlot();
        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, instance.Id, GoblinId, slot));
        new CombatQueueProcessor().ResolvePendingQueues(combat, builder.Build());

        Assert.False(slot.Value?.WasPlayed);
        Assert.Equal(12, combat.GetCombatant(GoblinId).Health.Current);  // no damage
        Assert.Equal(CardZone.Hand, instance.Zone);                       // card not moved
        Assert.Equal(1, combat.GetCombatant(HeroId).Resources[StandardCombatIds.EnergyResource].Current);
    }

    [Fact]
    public void PlayCard_DeadPlayer_NoOp_WasPlayedFalse()
    {
        var (builder, combat) = Setup();

        var cardId = new CardDefinitionId("test.dead_player_card");
        var card = BuildFreeCard(cardId, new EffectProgram<CardPlayContext>(new NoOpEffectNode<CardPlayContext>()));
        builder.RegisterCard(card);

        combat.GetCombatant(HeroId).SetLifecycleState(CombatantLifecycleState.Dead);
        var instance = AddCardToHand(combat, HeroId, cardId);

        var slot = new PlayCardOutcomeSlot();
        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, instance.Id, null, slot));
        new CombatQueueProcessor().ResolvePendingQueues(combat, builder.Build());

        Assert.False(slot.Value?.WasPlayed);
        Assert.Equal(CardZone.Hand, instance.Zone);
    }

    // ── PlayCardNode: programmatic card play ──────────────────────────────────

    [Fact]
    public void PlayCardNode_PlaysCardAndRecordsOutcome()
    {
        var (builder, combat) = Setup();

        var innerCardId = new CardDefinitionId("test.inner_strike");
        var innerCard = BuildFreeCard(innerCardId, new EffectProgram<CardPlayContext>(
            new DealDamageNode<CardPlayContext>(
                CombatantTargetSelectors.EventTarget,
                new ConstantExpression<CardPlayContext>(4))));
        builder.RegisterCard(innerCard);

        var instance = AddCardToHand(combat, HeroId, innerCardId);

        var resultKey = new EffectResultKey<OrderedTargetOutcomes<PlayCardOutcome>>("played");

        // Outer program plays the inner card
        var outerProgram = new EffectProgram<CardPlayContext>(
            new PlayCardNode<CardPlayContext>(
                playerSelector: CombatantTargetSelectors.Source,
                cardExpression: new ExplicitCardInstanceExpression<CardPlayContext>(instance.Id),
                cardTargetSelector: CombatantTargetSelectors.EventTarget,
                resultKey: resultKey));

        var buildCtx = MakeBuildContext(combat, HeroId, GoblinId);
        var ctx = new EffectExecutionContext<CardPlayContext>(
            new CardPlayContext(innerCard.Build(), null), buildCtx);

        EffectProgramExecutor.Execute(outerProgram, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, builder.Build());

        // Inner card's DealDamage resolved
        Assert.Equal(12 - 4, combat.GetCombatant(GoblinId).Health.Current);
        Assert.Equal(CardZone.DiscardPile, instance.Zone);

        // Outcome stored
        var outcomes = ctx.Get(resultKey);
        Assert.Single(outcomes.Results);
        Assert.True(outcomes.Results[0].Outcome.WasPlayed);
        Assert.Equal(instance.Id, outcomes.Results[0].Outcome.CardInstanceId);
    }

    [Fact]
    public void PlayCardNode_NullCardExpression_StoresEmptyOutcomes()
    {
        var (builder, combat) = Setup();

        var resultKey = new EffectResultKey<OrderedTargetOutcomes<PlayCardOutcome>>("played");

        // ExplicitCardInstanceExpression wraps an id that doesn't exist — we test null via
        // a non-existing card instance id. PlayCardEffectHandler will produce WasPlayed=false
        // but we also want to test the null path in the executor. Use a card expression
        // that always returns null.
        var outerProgram = new EffectProgram<CardPlayContext>(
            new PlayCardNode<CardPlayContext>(
                playerSelector: CombatantTargetSelectors.Source,
                cardExpression: new NullCardInstanceExpression<CardPlayContext>(),
                resultKey: resultKey));

        var buildCtx = MakeBuildContext(combat, HeroId, GoblinId);
        var ctx = new EffectExecutionContext<CardPlayContext>(
            new CardPlayContext(new CardDefinitionBuilder(new CardDefinitionId("test.dummy"), new PackageId("test"), "n", "d").Build(), null),
            buildCtx);

        EffectProgramExecutor.Execute(outerProgram, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, builder.Build());

        var outcomes = ctx.Get(resultKey);
        Assert.Empty(outcomes.Results);
    }

    [Fact]
    public void PlayCardNode_NestedCardPlay_OuterProgramContinuesAfterInnerCardEffects()
    {
        // §11.9 item 14: Outer program = [PlayCard(inner), Heal(source, 3)]
        // Inner card deals 5 damage. Outer then heals hero for 3.
        // This proves the outer program continues after inner card's queue resolves.

        var (builder, combat) = Setup();

        combat.GetCombatant(HeroId).Health.SetCurrent(10);  // hero at 10 HP

        var innerCardId = new CardDefinitionId("test.nested_inner");
        var innerCard = BuildFreeCard(innerCardId, new EffectProgram<CardPlayContext>(
            new DealDamageNode<CardPlayContext>(
                CombatantTargetSelectors.EventTarget,
                new ConstantExpression<CardPlayContext>(5))));
        builder.RegisterCard(innerCard);

        var instance = AddCardToHand(combat, HeroId, innerCardId);

        // Outer program: play inner card targeting goblin, then heal hero for 3
        var outerProgram = new EffectProgram<CardPlayContext>(
            new CausalSequenceEffectNode<CardPlayContext>([
                new PlayCardNode<CardPlayContext>(
                    playerSelector:    CombatantTargetSelectors.Source,
                    cardExpression:    new ExplicitCardInstanceExpression<CardPlayContext>(instance.Id),
                    cardTargetSelector: CombatantTargetSelectors.EventTarget),
                new HealNode<CardPlayContext>(
                    CombatantTargetSelectors.Source,
                    new ConstantExpression<CardPlayContext>(3)),
            ]));

        var buildCtx = MakeBuildContext(combat, HeroId, GoblinId);
        var ctx = new EffectExecutionContext<CardPlayContext>(
            new CardPlayContext(innerCard.Build(), null), buildCtx);

        EffectProgramExecutor.Execute(outerProgram, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, builder.Build());

        // Inner card: 5 damage to goblin (12 - 5 = 7)
        Assert.Equal(7, combat.GetCombatant(GoblinId).Health.Current);
        // Outer program heal: 10 + 3 = 13
        Assert.Equal(13, combat.GetCombatant(HeroId).Health.Current);
        // Inner card moved to discard
        Assert.Equal(CardZone.DiscardPile, instance.Zone);
    }

    // ── §11.9 integration tests ───────────────────────────────────────────────

    [Fact]
    public void Integration_StealBlock_MovesActualBlockAmount()
    {
        // §11.9 item 1: Steal Block
        // Program: ModifyDefensivePool(goblin, -N, resultKey) → ModifyDefensivePool(hero, +PreviousOutcomeSum(AppliedDelta))
        // Goblin has 5 block, steal all of it → hero gains 5 block

        var (builder, combat) = Setup();

        combat.GetCombatant(GoblinId).AddDefensivePool(
            StandardCombatIds.BlockDefensivePool,
            new ValuePoolState(5));

        var stealKey = new EffectResultKey<OrderedTargetOutcomes<PoolChangeOutcome>>("steal");

        var program = new EffectProgram<CardPlayContext>(
            new CausalSequenceEffectNode<CardPlayContext>([
                new ModifyDefensivePoolNode<CardPlayContext>(
                    CombatantTargetSelectors.EventTarget,
                    StandardCombatIds.BlockDefensivePool,
                    new ConstantExpression<CardPlayContext>(-10),  // try to remove 10, only 5 exist
                    resultKey: stealKey),
                new ModifyDefensivePoolNode<CardPlayContext>(
                    CombatantTargetSelectors.Source,
                    StandardCombatIds.BlockDefensivePool,
                    new PreviousOutcomeSumExpression<CardPlayContext, PoolChangeOutcome>(
                        stealKey, o => -o.AppliedDelta)),  // negate: applied was -5, we want +5
            ]));

        var buildCtx = MakeBuildContext(combat, HeroId, GoblinId);
        var ctx = new EffectExecutionContext<CardPlayContext>(
            new CardPlayContext(new CardDefinitionBuilder(new CardDefinitionId("steal.block"), new PackageId("test"), "n", "d").Build(), null),
            buildCtx);

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, builder.Build());

        // Goblin loses all 5 block
        Assert.False(combat.GetCombatant(GoblinId).DefensivePools.TryGetValue(
            StandardCombatIds.BlockDefensivePool, out var goblinPool) && goblinPool.Current > 0);

        // Hero gains exactly 5 block (actual removed, not the requested 10)
        Assert.True(combat.GetCombatant(HeroId).DefensivePools.TryGetValue(
            StandardCombatIds.BlockDefensivePool, out var heroPool));
        Assert.Equal(5, heroPool!.Current);
    }

    [Fact]
    public void Integration_DrawEqualToCardsMoved_DrawsCorrectCount()
    {
        // §11.9 item 5: MoveAllCards(discard→draw) + DrawCards(PreviousOutcomeSum(MovedCount))
        // Hero has 3 cards in discard; move all to draw, then draw that many

        var (builder, combat) = Setup();

        for (var i = 0; i < 3; i++)
        {
            var inst = new CardInstance(
                combat.CreateNextCardInstanceId(),
                StandardCombatIds.StrikeCard,
                HeroId,
                CardZone.DiscardPile);
            combat.GetCardZones(HeroId).AddCard(inst);
        }

        var moveKey = new EffectResultKey<OrderedTargetOutcomes<MoveAllCardsFromZoneOutcome>>("moved");

        var program = new EffectProgram<CardPlayContext>(
            new CausalSequenceEffectNode<CardPlayContext>([
                new MoveAllCardsFromZoneNode<CardPlayContext>(
                    CombatantTargetSelectors.Source,
                    CardZone.DiscardPile,
                    CardZone.DrawPile,
                    resultKey: moveKey),
                new DrawCardsNode<CardPlayContext>(
                    CombatantTargetSelectors.Source,
                    new PreviousOutcomeFieldExpression<CardPlayContext, MoveAllCardsFromZoneOutcome>(
                        moveKey, o => o.MovedCount)),
            ]));

        var buildCtx = MakeBuildContext(combat, HeroId, GoblinId);
        var ctx = new EffectExecutionContext<CardPlayContext>(
            new CardPlayContext(new CardDefinitionBuilder(new CardDefinitionId("test.recycle"), new PackageId("test"), "n", "d").Build(), null),
            buildCtx);

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, builder.Build());

        // 3 cards moved from discard to draw, then drawn → all 3 in hand
        Assert.Equal(3, combat.GetCardZones(HeroId).Hand.Count);
        Assert.Empty(combat.GetCardZones(HeroId).DiscardPile);
        Assert.Empty(combat.GetCardZones(HeroId).DrawPile);
    }

    [Fact]
    public void Integration_BranchIfStatusBlocked_BranchTaken()
    {
        // §11.9 item 8: ApplyStatus → ConditionalNode(PreviousOutcomeBoolField(Blocked))
        // When status is blocked by immunity, take the else branch (deal 0 damage).
        // When status is not blocked, take the then branch (deal 5 damage).

        var (builder, combat) = Setup();

        // Goblin has no immunity → status applies → Blocked=false → deal 5 damage
        var applyKey = new EffectResultKey<OrderedTargetOutcomes<ApplyStatusOutcome>>("apply");

        var program = new EffectProgram<CardPlayContext>(
            new CausalSequenceEffectNode<CardPlayContext>([
                new ApplyStatusNode<CardPlayContext>(
                    CombatantTargetSelectors.EventTarget,
                    StandardCombatIds.PoisonStatus,
                    stacks: new ConstantExpression<CardPlayContext>(1),
                    resultKey: applyKey),
                new ConditionalEffectNode<CardPlayContext>(
                    new PreviousOutcomeBoolFieldExpression<CardPlayContext, ApplyStatusOutcome>(
                        applyKey, o => !o.Blocked),  // "was NOT blocked" → then branch
                    then: new DealDamageNode<CardPlayContext>(
                        CombatantTargetSelectors.EventTarget,
                        new ConstantExpression<CardPlayContext>(5)),
                    @else: null),
            ]));

        var buildCtx = MakeBuildContext(combat, HeroId, GoblinId);
        var ctx = new EffectExecutionContext<CardPlayContext>(
            new CardPlayContext(new CardDefinitionBuilder(new CardDefinitionId("test.poison_then_hit"), new PackageId("test"), "n", "d").Build(), null),
            buildCtx);

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, builder.Build());

        // Status was applied (not blocked) → damage branch taken
        Assert.Equal(12 - 5, combat.GetCombatant(GoblinId).Health.Current);
        Assert.Single(combat.GetCombatant(GoblinId).Statuses);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (CombatDefinitionRegistryBuilder, CombatState) Setup()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        return (builder, combat);
    }

    private static CardDefinitionBuilder BuildFreeCard(
        CardDefinitionId id,
        EffectProgram<CardPlayContext> program)
    {
        var card = new CardDefinitionBuilder(id, new PackageId("test"), $"card.{id}.name", $"card.{id}.desc");
        card.Program = program;
        return card;
    }

    private static CardDefinitionBuilder BuildCardWithEnergyCost(
        CardDefinitionId id,
        int cost,
        EffectProgram<CardPlayContext> program)
    {
        var card = BuildFreeCard(id, program);
        card.Costs.Add(new ResourceCost(StandardCombatIds.EnergyResource, cost));
        return card;
    }

    private static CardInstance AddCardToHand(
        CombatState combat,
        CombatantId ownerId,
        CardDefinitionId definitionId)
    {
        var instance = new CardInstance(
            combat.CreateNextCardInstanceId(),
            definitionId,
            ownerId,
            CardZone.Hand);

        combat.GetCardZones(ownerId).AddCard(instance);
        return instance;
    }

    private static void GiveEnergy(
        CombatState combat,
        CombatantId combatantId,
        int current,
        int max)
    {
        var combatant = combat.GetCombatant(combatantId);

        if (combatant.Resources.TryGetValue(StandardCombatIds.EnergyResource, out var existing))
        {
            existing.SetMax(max);
            existing.SetCurrent(current);
        }
        else
        {
            combatant.AddResource(
                StandardCombatIds.EnergyResource,
                new ValuePoolState(current: current, max: max));
        }
    }

    private static TriggeredEffectActionBuildContext MakeBuildContext(
        CombatState combat,
        CombatantId sourceId,
        CombatantId eventTargetId)
    {
        var source = combat.GetCombatant(sourceId);
        return new TriggeredEffectActionBuildContext(
            new CombatantTargetSelectionContext(combat, source, eventTargetId),
            new TriggeredEffectActionSource(SourceCombatantId: sourceId));
    }

    private sealed class CaptureEventHandler<TEvent>(List<ICombatEvent> sink)
        : CombatEventHandler<TEvent>
        where TEvent : class, ICombatEvent
    {
        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            TEvent combatEvent) => sink.Add(combatEvent);
    }

    private sealed class NullCardInstanceExpression<TContext> : ICardInstanceExpression<TContext>
        where TContext : class
    {
        public CardInstanceId? Evaluate(EffectExecutionContext<TContext> context, CombatState combat) => null;
    }
}
