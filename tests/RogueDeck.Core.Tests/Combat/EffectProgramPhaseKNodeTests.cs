using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

/// <summary>
/// Tests for Phase K native nodes: ModifyResourceNode, MoveCardToZoneNode, SetCombatResultNode.
/// </summary>
public class EffectProgramPhaseKNodeTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");
    private static readonly ResourceId EnergyId = new("energy");

    // ── ModifyResourceNode ────────────────────────────────────────────────────

    [Fact]
    public void ModifyResourcePositiveDeltaIncreasesResource()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        combat.GetCombatant(HeroId).AddResource(EnergyId, new ValuePoolState(2, max: 5));

        var key = new EffectResultKey<OrderedTargetOutcomes<ModifyResourceOutcome>>("modify");
        var program = new EffectProgram<Ctx>(new ModifyResourceNode<Ctx>(
            CombatantTargetSelectors.Source,
            EnergyId,
            new ConstantExpression<Ctx>(3),
            resultKey: key));

        var ctx = MakeContext(combat);
        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(5, combat.GetCombatant(HeroId).Resources[EnergyId].Current);
        Assert.True(ctx.TryGet(key, out var ordered));
        var outcome = ordered!.Single();
        Assert.Equal(3, outcome.RequestedDelta);
        Assert.Equal(3, outcome.AppliedDelta);
        Assert.Equal(2, outcome.PreviousValue);
        Assert.Equal(5, outcome.CurrentValue);
        Assert.True(outcome.WasChanged);
    }

    [Fact]
    public void ModifyResourceNegativeDeltaDecreasesResource()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        combat.GetCombatant(HeroId).AddResource(EnergyId, new ValuePoolState(4, max: 5));

        var key = new EffectResultKey<OrderedTargetOutcomes<ModifyResourceOutcome>>("modify");
        var program = new EffectProgram<Ctx>(new ModifyResourceNode<Ctx>(
            CombatantTargetSelectors.Source,
            EnergyId,
            new ConstantExpression<Ctx>(-2),
            resultKey: key));

        var ctx = MakeContext(combat);
        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(2, combat.GetCombatant(HeroId).Resources[EnergyId].Current);
        Assert.True(ctx.TryGet(key, out var ordered));
        var outcome = ordered!.Single();
        Assert.Equal(-2, outcome.RequestedDelta);
        Assert.Equal(-2, outcome.AppliedDelta);
        Assert.Equal(4, outcome.PreviousValue);
        Assert.Equal(2, outcome.CurrentValue);
        Assert.True(outcome.WasChanged);
    }

    [Fact]
    public void ModifyResourceClampedByMaxProducesDefinedOutcome()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        combat.GetCombatant(HeroId).AddResource(EnergyId, new ValuePoolState(3, max: 5));

        var key = new EffectResultKey<OrderedTargetOutcomes<ModifyResourceOutcome>>("modify");
        var program = new EffectProgram<Ctx>(new ModifyResourceNode<Ctx>(
            CombatantTargetSelectors.Source,
            EnergyId,
            new ConstantExpression<Ctx>(10),
            max: 5,
            resultKey: key));

        var ctx = MakeContext(combat);
        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(5, combat.GetCombatant(HeroId).Resources[EnergyId].Current);
        Assert.True(ctx.TryGet(key, out var ordered));
        var outcome = ordered!.Single();
        Assert.Equal(10, outcome.RequestedDelta);
        Assert.Equal(2, outcome.AppliedDelta);
        Assert.True(outcome.ReachedMaximum);
        Assert.False(outcome.ReachedMinimum);
    }

    [Fact]
    public void ModifyResourceClampedToZeroByNegativeDelta()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        combat.GetCombatant(HeroId).AddResource(EnergyId, new ValuePoolState(2, max: 5));

        var key = new EffectResultKey<OrderedTargetOutcomes<ModifyResourceOutcome>>("modify");
        var program = new EffectProgram<Ctx>(new ModifyResourceNode<Ctx>(
            CombatantTargetSelectors.Source,
            EnergyId,
            new ConstantExpression<Ctx>(-10),
            resultKey: key));

        var ctx = MakeContext(combat);
        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(0, combat.GetCombatant(HeroId).Resources[EnergyId].Current);
        Assert.True(ctx.TryGet(key, out var ordered));
        var outcome = ordered!.Single();
        Assert.True(outcome.WasChanged);
        Assert.Equal(0, outcome.CurrentValue);
    }

    // ── SetCombatResultNode ───────────────────────────────────────────────────

    [Fact]
    public void SetCombatResultChangesResultAndProducesOutcome()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        Assert.Equal(CombatResult.Ongoing, combat.Result);

        var key = new EffectResultKey<SetCombatResultOutcome>("result");
        var program = new EffectProgram<Ctx>(new SetCombatResultNode<Ctx>(
            CombatResult.Victory, resultKey: key));

        var ctx = MakeContext(combat);
        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(CombatResult.Victory, combat.Result);
        Assert.True(ctx.TryGet(key, out var outcome));
        Assert.Equal(CombatResult.Ongoing, outcome!.PreviousResult);
        Assert.Equal(CombatResult.Victory, outcome.CurrentResult);
        Assert.True(outcome.WasChanged);
    }

    [Fact]
    public void SetCombatResultWithSameResultProducesNoOpOutcome()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var key = new EffectResultKey<SetCombatResultOutcome>("result");
        var program = new EffectProgram<Ctx>(new SetCombatResultNode<Ctx>(
            CombatResult.Ongoing, resultKey: key));

        var ctx = MakeContext(combat);
        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(CombatResult.Ongoing, combat.Result);
        Assert.True(ctx.TryGet(key, out var outcome));
        Assert.Equal(CombatResult.Ongoing, outcome!.PreviousResult);
        Assert.Equal(CombatResult.Ongoing, outcome.CurrentResult);
        Assert.False(outcome.WasChanged);
    }

    // ── MoveCardToZoneNode ────────────────────────────────────────────────────

    [Fact]
    public void MoveCardToZoneMovesCreatedCardToHand()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var cardId = new CardDefinitionId("test.card");
        builder.RegisterCard(new CardDefinitionBuilder(
            cardId,
            new PackageId("test"),
            displayNameKey: "test.card.name",
            descriptionKey: "test.card.desc"));
        var registry = builder.Build();

        var createKey = new EffectResultKey<OrderedTargetOutcomes<CreateCardInstanceOutcome>>("create");
        var moveKey = new EffectResultKey<MoveCardToZoneOutcome>("move");

        var program = new EffectProgram<Ctx>(new CausalSequenceEffectNode<Ctx>([
            new CreateCardInstanceNode<Ctx>(
                CombatantTargetSelectors.Source,
                cardId,
                CardZone.DrawPile,
                new ConstantExpression<Ctx>(1),
                resultKey: createKey),
            new MoveCardToZoneNode<Ctx>(
                CombatantTargetSelectors.Source,
                new CreateCardOutcomeExpression<Ctx>(createKey, 0),
                CardZone.Hand,
                resultKey: moveKey),
        ]));

        var ctx = MakeContext(combat);
        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.True(ctx.TryGet(moveKey, out var outcome));
        Assert.True(outcome!.WasMoved);
        Assert.Equal(CardZone.DrawPile, outcome.PreviousZone);
        Assert.Equal(CardZone.Hand, outcome.CurrentZone);

        var zones = combat.GetCardZones(HeroId);
        Assert.Single(zones.Hand);
        Assert.Empty(zones.DrawPile);
    }

    [Fact]
    public void MoveCardToZoneSameZoneProducesNoOpOutcome()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var cardId = new CardDefinitionId("test.card");
        builder.RegisterCard(new CardDefinitionBuilder(
            cardId,
            new PackageId("test"),
            displayNameKey: "test.card.name",
            descriptionKey: "test.card.desc"));
        var registry = builder.Build();

        var createKey = new EffectResultKey<OrderedTargetOutcomes<CreateCardInstanceOutcome>>("create");
        var moveKey = new EffectResultKey<MoveCardToZoneOutcome>("move");

        var program = new EffectProgram<Ctx>(new CausalSequenceEffectNode<Ctx>([
            new CreateCardInstanceNode<Ctx>(
                CombatantTargetSelectors.Source,
                cardId,
                CardZone.Hand,
                new ConstantExpression<Ctx>(1),
                resultKey: createKey),
            new MoveCardToZoneNode<Ctx>(
                CombatantTargetSelectors.Source,
                new CreateCardOutcomeExpression<Ctx>(createKey, 0),
                CardZone.Hand,
                resultKey: moveKey),
        ]));

        var ctx = MakeContext(combat);
        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.True(ctx.TryGet(moveKey, out var outcome));
        Assert.False(outcome!.WasMoved);
        Assert.Equal(CardZone.Hand, outcome.PreviousZone);
        Assert.Equal(CardZone.Hand, outcome.CurrentZone);

        Assert.Single(combat.GetCardZones(HeroId).Hand);
    }

    [Fact]
    public void MoveCardToZoneWithNullCardProducesDefinedOutcome()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var createKey = new EffectResultKey<OrderedTargetOutcomes<CreateCardInstanceOutcome>>("create");
        var moveKey = new EffectResultKey<MoveCardToZoneOutcome>("move");

        var program = new EffectProgram<Ctx>(new MoveCardToZoneNode<Ctx>(
            CombatantTargetSelectors.Source,
            new CreateCardOutcomeExpression<Ctx>(createKey, 0),
            CardZone.Hand,
            resultKey: moveKey));

        var ctx = MakeContext(combat);
        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.True(ctx.TryGet(moveKey, out var outcome));
        Assert.False(outcome!.WasMoved);
        Assert.Null(outcome.CardInstanceId);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static EffectExecutionContext<Ctx> MakeContext(CombatState combat) =>
        new(new Ctx(),
            new TriggeredEffectActionBuildContext(
                new CombatantTargetSelectionContext(
                    Combat: combat,
                    Source: combat.GetCombatant(HeroId),
                    EventTargetId: GoblinId),
                new TriggeredEffectActionSource(SourceCombatantId: HeroId)));

    private sealed record Ctx;
}
