using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class EffectProgramRepeatTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    // ── RepeatEffectNode construction ─────────────────────────────────────────

    [Fact]
    public void RepeatNodeRejectsZeroMaxCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RepeatEffectNode<Ctx>(
                new ConstantExpression<Ctx>(3),
                new NoOpEffectNode<Ctx>(),
                maxCount: 0));
    }

    [Fact]
    public void RepeatNodeRejectsNegativeMaxCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RepeatEffectNode<Ctx>(
                new ConstantExpression<Ctx>(3),
                new NoOpEffectNode<Ctx>(),
                maxCount: -1));
    }

    [Fact]
    public void RepeatNodeExposesBodyAsChild()
    {
        var body = new NoOpEffectNode<Ctx>();
        var node = new RepeatEffectNode<Ctx>(new ConstantExpression<Ctx>(3), body);

        Assert.Equal(body, Assert.Single(node.Children));
    }

    // ── Zero and one repetitions ──────────────────────────────────────────────

    [Fact]
    public void RepeatZeroTimesDoesNothing()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);

        var program = new EffectProgram<Ctx>(new RepeatEffectNode<Ctx>(
            new ConstantExpression<Ctx>(0),
            new DealDamageNode<Ctx>(
                CombatantTargetSelectors.EventTarget,
                new ConstantExpression<Ctx>(5))));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(12, combat.GetCombatant(GoblinId).Health.Current);
    }

    [Fact]
    public void RepeatOnceAppliesBodyExactlyOnce()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);

        var program = new EffectProgram<Ctx>(new RepeatEffectNode<Ctx>(
            new ConstantExpression<Ctx>(1),
            new DealDamageNode<Ctx>(
                CombatantTargetSelectors.EventTarget,
                new ConstantExpression<Ctx>(3))));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(12 - 3, combat.GetCombatant(GoblinId).Health.Current);
    }

    // ── Demonstration card: Rapid Strike ─────────────────────────────────────
    //
    // Repeat 3 times:
    //     Deal 2 Damage

    [Fact]
    public void RapidStrikeDealsDamageExactlyThreeTimes()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);

        var program = RapidStrikeProgram(count: 3, damagePerHit: 2);

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(12 - 6, combat.GetCombatant(GoblinId).Health.Current);
    }

    [Fact]
    public void RepeatFiveTimesAppliesBodyFiveTimes()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);

        var program = RapidStrikeProgram(count: 5, damagePerHit: 1);

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(12 - 5, combat.GetCombatant(GoblinId).Health.Current);
    }

    // ── Count is evaluated once ───────────────────────────────────────────────

    [Fact]
    public void CountExpressionIsEvaluatedOnceBeforeFirstIteration()
    {
        // Use a mutable counter as the count source. Even if the counter
        // changes between iterations the repeat count must stay fixed.
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);

        var callCount = 0;
        var countExpr = new LambdaExpression<Ctx>((_, _) =>
        {
            callCount++;
            return 3;
        });

        var program = new EffectProgram<Ctx>(new RepeatEffectNode<Ctx>(
            countExpr,
            new DealDamageNode<Ctx>(
                CombatantTargetSelectors.EventTarget,
                new ConstantExpression<Ctx>(1))));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(1, callCount);
        Assert.Equal(12 - 3, combat.GetCombatant(GoblinId).Health.Current);
    }

    // ── Causal ordering ───────────────────────────────────────────────────────

    [Fact]
    public void EachRepeatIterationIsACausalStep()
    {
        // Three hits of 1 damage each. Verify each iteration sees the
        // updated health, i.e. they are sequential not simultaneous.
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        combat.GetCombatant(GoblinId).Health.SetCurrent(3);

        var ctx = MakeContext(combat);
        var program = RapidStrikeProgram(count: 3, damagePerHit: 1);

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // All three hits land sequentially; goblin ends at 0 (downed)
        Assert.False(combat.GetCombatant(GoblinId).IsAlive);
    }

    // ── Combat end stops remaining iterations ─────────────────────────────────

    [Fact]
    public void RemainingIterationsAreSkippedWhenCombatEnds()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        // One hit of 999 damage kills the goblin and ends combat
        combat.GetCombatant(GoblinId).Health.SetCurrent(1);

        var ctx = MakeContext(combat);
        var program = RapidStrikeProgram(count: 5, damagePerHit: 999);

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.NotEqual(CombatResult.Ongoing, combat.Result);
        // No exception from trying to damage a dead combatant
    }

    // ── Limit enforcement ─────────────────────────────────────────────────────

    [Fact]
    public void NegativeCountThrows()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);

        var program = new EffectProgram<Ctx>(new RepeatEffectNode<Ctx>(
            new ConstantExpression<Ctx>(-1),
            new NoOpEffectNode<Ctx>()));

        Assert.Throws<InvalidOperationException>(() =>
        {
            EffectProgramExecutor.Execute(program, ctx, combat);
            new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
        });
    }

    [Fact]
    public void CountExceedingMaxCountThrows()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);

        var program = new EffectProgram<Ctx>(new RepeatEffectNode<Ctx>(
            new ConstantExpression<Ctx>(10),
            new NoOpEffectNode<Ctx>(),
            maxCount: 5));

        Assert.Throws<InvalidOperationException>(() =>
        {
            EffectProgramExecutor.Execute(program, ctx, combat);
            new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
        });
    }

    [Fact]
    public void CountAtExactMaxCountDoesNotThrow()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);

        var program = new EffectProgram<Ctx>(new RepeatEffectNode<Ctx>(
            new ConstantExpression<Ctx>(5),
            new DealDamageNode<Ctx>(
                CombatantTargetSelectors.EventTarget,
                new ConstantExpression<Ctx>(1)),
            maxCount: 5));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(12 - 5, combat.GetCombatant(GoblinId).Health.Current);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static EffectProgram<Ctx> RapidStrikeProgram(int count, int damagePerHit) =>
        new(new RepeatEffectNode<Ctx>(
            new ConstantExpression<Ctx>(count),
            new DealDamageNode<Ctx>(
                CombatantTargetSelectors.EventTarget,
                new ConstantExpression<Ctx>(damagePerHit))));

    private static EffectExecutionContext<Ctx> MakeContext(CombatState combat) =>
        new(new Ctx(),
            new TriggeredEffectActionBuildContext(
                new CombatantTargetSelectionContext(
                    Combat: combat,
                    Source: combat.GetCombatant(HeroId),
                    EventTargetId: GoblinId),
                new TriggeredEffectActionSource(SourceCombatantId: HeroId)));

    private sealed record Ctx;

    // Minimal test-only expression that delegates to a lambda
    private sealed class LambdaExpression<TCtx> : ICombatExpression<TCtx, int>
        where TCtx : class
    {
        private readonly Func<EffectExecutionContext<TCtx>, CombatState, int> _fn;

        public LambdaExpression(Func<EffectExecutionContext<TCtx>, CombatState, int> fn) =>
            _fn = fn;

        public int Evaluate(EffectExecutionContext<TCtx> context, CombatState combat) =>
            _fn(context, combat);
    }
}
