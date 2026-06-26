using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

/// <summary>
/// Regression tests for Scenario J: every legal native no-op operation must
/// produce a defined outcome, not a missing result slot.
///
/// Current gaps: several handlers return early without populating the OutcomeSlot
/// when the operation has zero effect:
///   - GainResourceEffectHandler: skips slot when gainedAmount ≤ 0
///   - DrawCardsEffectHandler: skips slot when drawnCards.Count == 0
///   - CreateCardInstanceEffectHandler: skips slot when count == 0 (TBD)
///
/// Tests marked Skip will pass after handlers are made total (9.5I).
/// Tests without Skip already pass and act as regression guards.
/// </summary>
public class EffectProgramNoOpOutcomeTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    private static readonly ResourceId EnergyId = new("energy");

    // ── GainResource: pool is full ────────────────────────────────────────────
    //
    // When the pool is already at its max, gainedAmount == 0 and the handler
    // returns early without filling the OutcomeSlot. Downstream Get() throws.

    [Fact]
    public void GainResourceWhenPoolFullProducesZeroGainOutcome()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        // Give the hero a full energy pool (max 3, current 3).
        combat.GetCombatant(HeroId).AddResource(EnergyId, new ValuePoolState(3, max: 3));

        var gainKey = new EffectResultKey<OrderedTargetOutcomes<GainResourceOutcome>>("gain");
        var program = new EffectProgram<Ctx>(new GainResourceNode<Ctx>(
            CombatantTargetSelectors.Source,
            EnergyId,
            new ConstantExpression<Ctx>(5),
            defaultMax: 3,
            resultKey: gainKey));

        var ctx = MakeContext(combat);

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // Pool should remain full.
        Assert.Equal(3, combat.GetCombatant(HeroId).Resources[EnergyId].Current);

        // Result must be defined (zero gain) rather than missing.
        Assert.True(ctx.TryGet(gainKey, out var outcome),
            "GainResource no-op must produce a defined outcome, not a missing key.");
        Assert.Equal(5, outcome!.Single().RequestedAmount);
        Assert.Equal(0, outcome.Single().GainedAmount);
        Assert.Equal(3, outcome.Single().PreviousCurrent);
        Assert.Equal(3, outcome.Single().NewCurrent);
    }

    // ── GainResource: normal gain produces outcome ────────────────────────────
    //
    // Regression guard: the happy path already stores the outcome slot.

    [Fact]
    public void GainResourceNormalGainProducesOutcome()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        // Hero starts with 1 energy out of 5 max.
        combat.GetCombatant(HeroId).AddResource(EnergyId, new ValuePoolState(1, max: 5));

        var gainKey = new EffectResultKey<OrderedTargetOutcomes<GainResourceOutcome>>("gain");
        var program = new EffectProgram<Ctx>(new GainResourceNode<Ctx>(
            CombatantTargetSelectors.Source,
            EnergyId,
            new ConstantExpression<Ctx>(2),
            defaultMax: 5,
            resultKey: gainKey));

        var ctx = MakeContext(combat);

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.True(ctx.TryGet(gainKey, out var outcome));
        Assert.Equal(2, outcome!.Single().GainedAmount);
        Assert.Equal(1, outcome.Single().PreviousCurrent);
        Assert.Equal(3, outcome.Single().NewCurrent);
    }

    // ── RefillResource: already full ─────────────────────────────────────────
    //
    // RefillResource always fills the outcome slot (even when already full),
    // so this tests the existing behaviour as a regression guard.

    [Fact]
    public void RefillResourceWhenAlreadyFullProducesOutcome()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        combat.GetCombatant(HeroId).AddResource(EnergyId, new ValuePoolState(3, max: 3));

        var refillKey = new EffectResultKey<OrderedTargetOutcomes<RefillResourceOutcome>>("refill");
        var program = new EffectProgram<Ctx>(new RefillResourceNode<Ctx>(
            CombatantTargetSelectors.Source,
            EnergyId,
            defaultMax: 3,
            resultKey: refillKey));

        var ctx = MakeContext(combat);

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.True(ctx.TryGet(refillKey, out var outcome));
        Assert.Equal(3, outcome!.Single().PreviousCurrent);
        Assert.Equal(3, outcome.Single().NewCurrent);
    }

    // ── DrawCards: deck and discard are both empty ────────────────────────────
    //
    // When no cards are available the handler returns early without filling
    // the OutcomeSlot. Downstream Get() throws.

    [Fact]
    public void DrawCardsFromEmptyDeckProducesZeroDrawnOutcome()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        // Hero has no cards in draw pile or discard pile.
        var drawKey = new EffectResultKey<OrderedTargetOutcomes<DrawCardsOutcome>>("draw");
        var program = new EffectProgram<Ctx>(new DrawCardsNode<Ctx>(
            CombatantTargetSelectors.Source,
            new ConstantExpression<Ctx>(3),
            resultKey: drawKey));

        var ctx = MakeContext(combat);

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.True(ctx.TryGet(drawKey, out var outcome),
            "DrawCards no-op must produce a defined outcome, not a missing key.");
        Assert.Equal(3, outcome!.Single().RequestedCount);
        Assert.Equal(0, outcome.Single().DrawnCount);
        Assert.Empty(outcome.Single().DrawnCardInstanceIds);
    }

    // ── MoveAllCards: source zone is empty ───────────────────────────────────
    //
    // Moving all cards from an empty zone should produce a defined zero-moved outcome.

    [Fact]
    public void MoveAllCardsFromEmptyZoneProducesZeroMovedOutcome()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        // Hero has no cards in the discard pile.
        var moveKey = new EffectResultKey<OrderedTargetOutcomes<MoveAllCardsFromZoneOutcome>>("move");
        var program = new EffectProgram<Ctx>(new MoveAllCardsFromZoneNode<Ctx>(
            CombatantTargetSelectors.Source,
            CardZone.DiscardPile,
            CardZone.Hand,
            resultKey: moveKey));

        var ctx = MakeContext(combat);

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.True(ctx.TryGet(moveKey, out var outcome),
            "MoveAllCards no-op must produce a defined outcome, not a missing key.");
        Assert.Equal(0, outcome!.Single().MovedCount);
        Assert.Empty(outcome.Single().MovedCardInstanceIds);
    }

    // ── RemoveStatus: status is absent from combatant ────────────────────────
    //
    // Removing a registered status that the target combatant doesn't currently
    // have should produce a defined zero-removed outcome. The handler already
    // fills the slot in this case; this is a regression guard.

    [Fact]
    public void RemoveAbsentStatusProducesDefinedZeroRemovedOutcome()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        // Register the status so the handler doesn't throw on lookup;
        // but do NOT apply it to the goblin, so the "not found" branch fires.
        var statusId = new StatusDefinitionId("test.absent_status");
        builder.RegisterStatus(new StatusDefinition(
            id: statusId,
            packageId: new PackageId("test"),
            displayNameKey: "test.absent_status.name",
            descriptionKey: "test.absent_status.desc"));
        var registry = builder.Build();

        var removeKey = new EffectResultKey<OrderedTargetOutcomes<RemoveStatusOutcome>>("removed");
        var program = new EffectProgram<Ctx>(new RemoveStatusNode<Ctx>(
            CombatantTargetSelectors.EventTarget,
            statusId,
            resultKey: removeKey));

        var ctx = MakeContext(combat, eventTargetId: GoblinId);

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.True(ctx.TryGet(removeKey, out var outcome));
        Assert.Equal(0, outcome!.Single().RemovedCount);
        Assert.Empty(outcome.Single().RemovedInstanceIds);
    }

    // ── SetCombatantLifecycleState: already in requested state ───────────────
    //
    // When the combatant is already in the requested lifecycle state, the
    // outcome must reflect WasChanged = false with consistent prev/current values.

    [Fact]
    public void SetLifecycleStateWhenAlreadySetProducesDefinedOutcome()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        // Goblin is alive (Active); request to set it to Active again.
        var lifecycleKey = new EffectResultKey<OrderedTargetOutcomes<SetCombatantLifecycleStateOutcome>>("lifecycle");
        var program = new EffectProgram<Ctx>(new SetCombatantLifecycleStateNode<Ctx>(
            CombatantTargetSelectors.EventTarget,
            CombatantLifecycleState.Alive,
            resultKey: lifecycleKey));

        var ctx = MakeContext(combat, eventTargetId: GoblinId);

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.True(ctx.TryGet(lifecycleKey, out var outcome));
        Assert.Equal(CombatantLifecycleState.Alive, outcome!.Single().PreviousState);
        Assert.Equal(CombatantLifecycleState.Alive, outcome.Single().NewState);
        Assert.False(outcome.Single().WasChanged);
    }

    // ── DrawCards: explicit zero count ───────────────────────────────────────

    [Fact]
    public void DrawZeroCardsProducesZeroDrawnOutcome()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var drawKey = new EffectResultKey<OrderedTargetOutcomes<DrawCardsOutcome>>("draw");
        var program = new EffectProgram<Ctx>(new DrawCardsNode<Ctx>(
            CombatantTargetSelectors.Source,
            new ConstantExpression<Ctx>(0),
            resultKey: drawKey));

        var ctx = MakeContext(combat);

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.True(ctx.TryGet(drawKey, out var outcome),
            "DrawCards(0) must produce a defined outcome, not a missing key.");
        Assert.Equal(0, outcome!.Single().RequestedCount);
        Assert.Equal(0, outcome.Single().DrawnCount);
        Assert.Empty(outcome.Single().DrawnCardInstanceIds);
    }

    // ── GainResource: explicit zero amount ───────────────────────────────────

    [Fact]
    public void GainZeroResourceProducesZeroGainOutcome()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        combat.GetCombatant(HeroId).AddResource(EnergyId, new ValuePoolState(2, max: 5));

        var gainKey = new EffectResultKey<OrderedTargetOutcomes<GainResourceOutcome>>("gain");
        var program = new EffectProgram<Ctx>(new GainResourceNode<Ctx>(
            CombatantTargetSelectors.Source,
            EnergyId,
            new ConstantExpression<Ctx>(0),
            defaultMax: 5,
            resultKey: gainKey));

        var ctx = MakeContext(combat);

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(2, combat.GetCombatant(HeroId).Resources[EnergyId].Current);

        Assert.True(ctx.TryGet(gainKey, out var outcome),
            "GainResource(0) must produce a defined outcome, not a missing key.");
        Assert.Equal(0, outcome!.Single().RequestedAmount);
        Assert.Equal(0, outcome.Single().GainedAmount);
        Assert.Equal(2, outcome.Single().PreviousCurrent);
        Assert.Equal(2, outcome.Single().NewCurrent);
    }

    // ── CreateCardInstance: explicit zero count ───────────────────────────────

    [Fact]
    public void CreateZeroCardInstancesProducesZeroCreatedOutcome()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var cardId = new CardDefinitionId("test.strike");
        builder.RegisterCard(new CardDefinitionBuilder(
            cardId,
            new PackageId("test"),
            displayNameKey: "test.strike.name",
            descriptionKey: "test.strike.desc"));
        var registry = builder.Build();

        var createKey = new EffectResultKey<OrderedTargetOutcomes<CreateCardInstanceOutcome>>("create");
        var program = new EffectProgram<Ctx>(new CreateCardInstanceNode<Ctx>(
            CombatantTargetSelectors.Source,
            cardId,
            CardZone.Hand,
            new ConstantExpression<Ctx>(0),
            resultKey: createKey));

        var ctx = MakeContext(combat);

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.True(ctx.TryGet(createKey, out var outcome),
            "CreateCardInstance(0) must produce a defined outcome, not a missing key.");
        Assert.Equal(0, outcome!.Single().CreatedCount);
        Assert.Empty(outcome.Single().CreatedCardInstanceIds);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static EffectExecutionContext<Ctx> MakeContext(
        CombatState combat,
        CombatantId? eventTargetId = null) =>
        new(new Ctx(),
            new TriggeredEffectActionBuildContext(
                new CombatantTargetSelectionContext(
                    Combat: combat,
                    Source: combat.GetCombatant(HeroId),
                    EventTargetId: eventTargetId),
                new TriggeredEffectActionSource(SourceCombatantId: HeroId)));

    private sealed record Ctx;
}
