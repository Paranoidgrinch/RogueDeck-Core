using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class EffectProgramResourceTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");
    private static readonly ResourceId ManaId = new("resource.mana");

    // ── GainResourceNode ──────────────────────────────────────────────────────

    [Fact]
    public void GainResourceNodeAddsAmountToNewPool()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);

        var program = new EffectProgram<Ctx>(new GainResourceNode<Ctx>(
            CombatantTargetSelectors.Source,
            ManaId,
            new ConstantExpression<Ctx>(3)));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(3, combat.GetCombatant(HeroId).Resources[ManaId].Current);
    }

    [Fact]
    public void GainResourceNodeAddsAmountToExistingPool()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        combat.GetCombatant(HeroId).AddResource(ManaId, new ValuePoolState(2, max: 10));

        var ctx = MakeContext(combat);
        var program = new EffectProgram<Ctx>(new GainResourceNode<Ctx>(
            CombatantTargetSelectors.Source,
            ManaId,
            new ConstantExpression<Ctx>(4)));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(6, combat.GetCombatant(HeroId).Resources[ManaId].Current);
    }

    [Fact]
    public void GainResourceNodeCapsGainAtMax()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        combat.GetCombatant(HeroId).AddResource(ManaId, new ValuePoolState(3, max: 5));

        var ctx = MakeContext(combat);
        var program = new EffectProgram<Ctx>(new GainResourceNode<Ctx>(
            CombatantTargetSelectors.Source,
            ManaId,
            new ConstantExpression<Ctx>(10)));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(5, combat.GetCombatant(HeroId).Resources[ManaId].Current);
    }

    [Fact]
    public void GainResourceNodeOutcomeRecordsGainedAmount()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);
        var resultKey = new EffectResultKey<OrderedTargetOutcomes<GainResourceOutcome>>("mana_gain");

        combat.GetCombatant(HeroId).AddResource(ManaId, new ValuePoolState(2, max: 5));

        var program = new EffectProgram<Ctx>(new GainResourceNode<Ctx>(
            CombatantTargetSelectors.Source,
            ManaId,
            new ConstantExpression<Ctx>(4),   // capped at 5, actual gain = 3
            resultKey: resultKey));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var outcome = ctx.Get(resultKey).Single();
        Assert.Equal(4, outcome.RequestedAmount);
        Assert.Equal(3, outcome.GainedAmount);
        Assert.Equal(2, outcome.PreviousCurrent);
        Assert.Equal(5, outcome.NewCurrent);
        Assert.True(outcome.ReachedMaximum);
    }

    [Fact]
    public void GainResourceOutcomeIsDefinedEvenWhenGainedAmountIsZero()
    {
        // Pool already at max: gain does nothing, but outcome must still be defined (zero-gain).
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);
        var resultKey = new EffectResultKey<OrderedTargetOutcomes<GainResourceOutcome>>("mana_gain");

        combat.GetCombatant(HeroId).AddResource(ManaId, new ValuePoolState(5, max: 5));

        var program = new EffectProgram<Ctx>(new GainResourceNode<Ctx>(
            CombatantTargetSelectors.Source,
            ManaId,
            new ConstantExpression<Ctx>(3),
            resultKey: resultKey));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.True(ctx.TryGet(resultKey, out var ordered));
        var outcome2 = ordered!.Single();
        Assert.Equal(0, outcome2.GainedAmount);
        Assert.Equal(5, outcome2.PreviousCurrent);
        Assert.Equal(5, outcome2.NewCurrent);
    }

    [Fact]
    public void GainResourceNodeRejectsNegativeDefaultMax()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GainResourceNode<Ctx>(
                CombatantTargetSelectors.Source,
                ManaId,
                new ConstantExpression<Ctx>(1),
                defaultMax: -1));
    }

    // ── RefillResourceNode ────────────────────────────────────────────────────

    [Fact]
    public void RefillResourceNodeSetsPoolToMax()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        combat.GetCombatant(HeroId).AddResource(ManaId, new ValuePoolState(1, max: 5));

        var ctx = MakeContext(combat);
        var program = new EffectProgram<Ctx>(new RefillResourceNode<Ctx>(
            CombatantTargetSelectors.Source,
            ManaId,
            defaultMax: 5));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(5, combat.GetCombatant(HeroId).Resources[ManaId].Current);
    }

    [Fact]
    public void RefillResourceNodeCreatesPoolWhenAbsent()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);

        var program = new EffectProgram<Ctx>(new RefillResourceNode<Ctx>(
            CombatantTargetSelectors.Source,
            ManaId,
            defaultMax: 3));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(3, combat.GetCombatant(HeroId).Resources[ManaId].Current);
    }

    [Fact]
    public void RefillResourceNodeOutcomeRecordsValues()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);
        var resultKey = new EffectResultKey<OrderedTargetOutcomes<RefillResourceOutcome>>("mana_refill");

        combat.GetCombatant(HeroId).AddResource(ManaId, new ValuePoolState(2, max: 4));

        var program = new EffectProgram<Ctx>(new RefillResourceNode<Ctx>(
            CombatantTargetSelectors.Source,
            ManaId,
            defaultMax: 4,
            resultKey: resultKey));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var outcome = ctx.Get(resultKey).Single();
        Assert.Equal(2, outcome.PreviousCurrent);
        Assert.Equal(4, outcome.NewCurrent);
        Assert.Equal(4, outcome.DefaultMax);
    }

    [Fact]
    public void RefillResourceNodeRejectsNegativeDefaultMax()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RefillResourceNode<Ctx>(
                CombatantTargetSelectors.Source,
                ManaId,
                defaultMax: -1));
    }

    // ── Composite: Power Strike ───────────────────────────────────────────────
    //
    // CausalSequence [
    //   GainResource(self, mana, 3) → resultKey=gained
    //   DealDamage(target, gained.GainedAmount)
    // ]
    //
    // Self gains up to 3 mana (may be capped). The actual gained amount is
    // then dealt as damage to the goblin. This proves the outcome of one
    // resource operation can drive the amount of a subsequent damage operation.

    [Fact]
    public void PowerStrikeDealsActualGainedAmountAsDamage()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);
        var gainedKey = new EffectResultKey<OrderedTargetOutcomes<GainResourceOutcome>>("gained");

        // Hero has no mana pool yet; DefaultMax=2, request=3 → gained=2
        var program = PowerStrikeProgram(gainedKey, requestedGain: 3, manaMax: 2);

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(2, combat.GetCombatant(HeroId).Resources[ManaId].Current);
        Assert.Equal(12 - 2, combat.GetCombatant(GoblinId).Health.Current);
    }

    [Fact]
    public void PowerStrikeDealsFullGainAsDamageWhenUnderMax()
    {
        // Hero has 1 mana of 5 max. Gain 3 → actual gain = 3 (not capped).
        // DealDamage(3) → goblin at 12-3 = 9 HP.
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        combat.GetCombatant(HeroId).AddResource(ManaId, new ValuePoolState(1, max: 5));

        var ctx = MakeContext(combat);
        var gainedKey = new EffectResultKey<OrderedTargetOutcomes<GainResourceOutcome>>("gained");
        var program = PowerStrikeProgram(gainedKey, requestedGain: 3, manaMax: 5);

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(4, combat.GetCombatant(HeroId).Resources[ManaId].Current);
        Assert.Equal(12 - 3, combat.GetCombatant(GoblinId).Health.Current);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static EffectProgram<Ctx> PowerStrikeProgram(
        EffectResultKey<OrderedTargetOutcomes<GainResourceOutcome>> gainedKey,
        int requestedGain,
        int manaMax) =>
        new(new CausalSequenceEffectNode<Ctx>([
            new GainResourceNode<Ctx>(
                CombatantTargetSelectors.Source,
                ManaId,
                new ConstantExpression<Ctx>(requestedGain),
                defaultMax: manaMax,
                resultKey: gainedKey),
            new DealDamageNode<Ctx>(
                CombatantTargetSelectors.EventTarget,
                new PreviousOutcomeFieldExpression<Ctx, GainResourceOutcome>(
                    gainedKey, o => o.GainedAmount)),
        ]));

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
