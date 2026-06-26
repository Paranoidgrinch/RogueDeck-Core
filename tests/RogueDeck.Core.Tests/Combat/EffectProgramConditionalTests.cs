using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class EffectProgramConditionalTests
{
    // ── Arithmetic expressions ────────────────────────────────────────────────

    [Fact]
    public void AbsExpressionReturnsAbsoluteValueOfNegativeInput()
    {
        var expr = new AbsExpression<Ctx>(new ConstantExpression<Ctx>(-3));

        Assert.Equal(3, Eval(expr));
    }

    [Fact]
    public void AbsExpressionPreservesPositiveInput()
    {
        var expr = new AbsExpression<Ctx>(new ConstantExpression<Ctx>(5));

        Assert.Equal(5, Eval(expr));
    }

    [Fact]
    public void AddExpressionSumsOperands()
    {
        var expr = new AddExpression<Ctx>(Const(3), Const(4));

        Assert.Equal(7, Eval(expr));
    }

    [Fact]
    public void SubtractExpressionSubtractsRightFromLeft()
    {
        var expr = new SubtractExpression<Ctx>(Const(10), Const(4));

        Assert.Equal(6, Eval(expr));
    }

    [Fact]
    public void MultiplyExpressionMultipliesOperands()
    {
        var expr = new MultiplyExpression<Ctx>(Const(3), Const(4));

        Assert.Equal(12, Eval(expr));
    }

    [Fact]
    public void MinExpressionReturnsSmallerValue()
    {
        var expr = new MinExpression<Ctx>(Const(7), Const(3));

        Assert.Equal(3, Eval(expr));
    }

    [Fact]
    public void MaxExpressionReturnsLargerValue()
    {
        var expr = new MaxExpression<Ctx>(Const(7), Const(3));

        Assert.Equal(7, Eval(expr));
    }

    // ── Comparison expressions ─────────────────────────────────────────────────

    [Theory]
    [InlineData(5, 3, ComparisonOperator.Greater, true)]
    [InlineData(3, 5, ComparisonOperator.Greater, false)]
    [InlineData(5, 5, ComparisonOperator.Greater, false)]
    [InlineData(5, 5, ComparisonOperator.GreaterOrEqual, true)]
    [InlineData(4, 5, ComparisonOperator.GreaterOrEqual, false)]
    [InlineData(3, 5, ComparisonOperator.Less, true)]
    [InlineData(5, 3, ComparisonOperator.Less, false)]
    [InlineData(5, 5, ComparisonOperator.Less, false)]
    [InlineData(5, 5, ComparisonOperator.LessOrEqual, true)]
    [InlineData(6, 5, ComparisonOperator.LessOrEqual, false)]
    [InlineData(5, 5, ComparisonOperator.Equal, true)]
    [InlineData(4, 5, ComparisonOperator.Equal, false)]
    [InlineData(4, 5, ComparisonOperator.NotEqual, true)]
    [InlineData(5, 5, ComparisonOperator.NotEqual, false)]
    public void ComparisonExpressionProducesCorrectResult(
        int left, int right, ComparisonOperator op, bool expected)
    {
        var expr = new ComparisonExpression<Ctx>(Const(left), op, Const(right));

        Assert.Equal(expected, EvalBool(expr));
    }

    // ── Boolean expressions ───────────────────────────────────────────────────

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void AndExpressionFollowsTruthTable(bool l, bool r, bool expected)
    {
        var expr = new AndExpression<Ctx>(BoolConst(l), BoolConst(r));

        Assert.Equal(expected, EvalBool(expr));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void OrExpressionFollowsTruthTable(bool l, bool r, bool expected)
    {
        var expr = new OrExpression<Ctx>(BoolConst(l), BoolConst(r));

        Assert.Equal(expected, EvalBool(expr));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void NotExpressionInvertsOperand(bool input, bool expected)
    {
        var expr = new NotExpression<Ctx>(BoolConst(input));

        Assert.Equal(expected, EvalBool(expr));
    }

    // ── AndExpression short-circuits ──────────────────────────────────────────

    [Fact]
    public void AndExpressionDoesNotEvaluateRightWhenLeftIsFalse()
    {
        var evaluated = false;
        var right = new SideEffectExpression<Ctx>(() => { evaluated = true; return true; });
        var expr = new AndExpression<Ctx>(BoolConst(false), right);

        EvalBool(expr);

        Assert.False(evaluated);
    }

    [Fact]
    public void OrExpressionDoesNotEvaluateRightWhenLeftIsTrue()
    {
        var evaluated = false;
        var right = new SideEffectExpression<Ctx>(() => { evaluated = true; return false; });
        var expr = new OrExpression<Ctx>(BoolConst(true), right);

        EvalBool(expr);

        Assert.False(evaluated);
    }

    // ── ConditionalEffectNode: branch selection ────────────────────────────────

    [Fact]
    public void ThenBranchExecutesWhenConditionIsTrue()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var heroId = new CombatantId("hero_001");
        var goblinId = new CombatantId("goblin_001");
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var program = new EffectProgram<Ctx>(new ConditionalEffectNode<Ctx>(
            BoolConst(true),
            then: new DealDamageNode<Ctx>(
                CombatantTargetSelectors.EventTarget,
                Const(6))));

        Execute(program, combat, eventTargetId: goblinId, sourceId: heroId);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // Then branch ran: goblin took 6 damage (started at 12)
        Assert.Equal(6, combat.GetCombatant(goblinId).Health.Current);
    }

    [Fact]
    public void ElseBranchExecutesWhenConditionIsFalse()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var heroId = new CombatantId("hero_001");
        var goblinId = new CombatantId("goblin_001");
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var program = new EffectProgram<Ctx>(new ConditionalEffectNode<Ctx>(
            BoolConst(false),
            then: new DealDamageNode<Ctx>(CombatantTargetSelectors.EventTarget, Const(6)),
            @else: new DealDamageNode<Ctx>(CombatantTargetSelectors.EventTarget, Const(2))));

        Execute(program, combat, eventTargetId: goblinId, sourceId: heroId);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // Else branch ran: goblin took 2 damage; then branch did NOT run
        Assert.Equal(10, combat.GetCombatant(goblinId).Health.Current);
    }

    [Fact]
    public void NeitherBranchFiresWhenFalseAndNoElse()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var goblinId = new CombatantId("goblin_001");
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var program = new EffectProgram<Ctx>(new ConditionalEffectNode<Ctx>(
            BoolConst(false),
            then: new DealDamageNode<Ctx>(CombatantTargetSelectors.EventTarget, Const(6))));

        Execute(program, combat, eventTargetId: goblinId, sourceId: null);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(12, combat.GetCombatant(goblinId).Health.Current);
    }

    [Fact]
    public void OnlySelectedBranchResolvesEffect()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var heroId = new CombatantId("hero_001");
        var goblinId = new CombatantId("goblin_001");
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var heroInitial = combat.GetCombatant(heroId).Health.Current;

        // Then: damage goblin; Else: heal hero — only one should fire
        var program = new EffectProgram<Ctx>(new ConditionalEffectNode<Ctx>(
            BoolConst(true),
            then: new DealDamageNode<Ctx>(CombatantTargetSelectors.EventTarget, Const(4)),
            @else: new HealNode<Ctx>(CombatantTargetSelectors.Source, Const(10))));

        Execute(program, combat, eventTargetId: goblinId, sourceId: heroId);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // Then ran (goblin damaged), Else did not run (hero unchanged)
        Assert.Equal(8, combat.GetCombatant(goblinId).Health.Current);
        Assert.Equal(heroInitial, combat.GetCombatant(heroId).Health.Current);
    }

    // ── Shatter proof card (Step 5 completion criterion B) ────────────────────
    //
    // removed = Modify target Block by -5
    // If abs(removed.AppliedDelta) > 0:
    //     Deal abs(removed.AppliedDelta) Damage

    [Fact]
    public void ShatterDealsDamageEqualToBlockActuallyRemoved()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var heroId = new CombatantId("hero_001");
        var goblinId = new CombatantId("goblin_001");
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        // Goblin has 3 block; Shatter removes 3 (clamped from -5) → deal 3 damage
        combat.GetCombatant(goblinId).AddDefensivePool(
            StandardCombatIds.BlockDefensivePool,
            new ValuePoolState(3));

        var program = BuildShatterProgram();

        Execute(program, combat, eventTargetId: goblinId, sourceId: heroId);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // 3 block removed → 3 damage → goblin = 12 - 3 = 9
        Assert.Equal(9, combat.GetCombatant(goblinId).Health.Current);
    }

    [Fact]
    public void ShatterDealsFullFiveDamageWhenTargetHasExactlyFiveBlock()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var heroId = new CombatantId("hero_001");
        var goblinId = new CombatantId("goblin_001");
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        // Goblin has exactly 5 block; full -5 applied → 0 remaining → 5 damage hits health
        combat.GetCombatant(goblinId).AddDefensivePool(
            StandardCombatIds.BlockDefensivePool,
            new ValuePoolState(5));

        var program = BuildShatterProgram();

        Execute(program, combat, eventTargetId: goblinId, sourceId: heroId);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // 5 block removed → 0 remaining → deal 5 damage unblocked → goblin = 12 - 5 = 7
        Assert.Equal(7, combat.GetCombatant(goblinId).Health.Current);
    }

    [Fact]
    public void ShatterDealsNoDamageWhenTargetHasNoBlock()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var heroId = new CombatantId("hero_001");
        var goblinId = new CombatantId("goblin_001");
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var program = BuildShatterProgram();

        Execute(program, combat, eventTargetId: goblinId, sourceId: heroId);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // 0 block removed → condition false → no damage
        Assert.Equal(12, combat.GetCombatant(goblinId).Health.Current);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static EffectProgram<Ctx> BuildShatterProgram()
    {
        var removedKey = new EffectResultKey<OrderedTargetOutcomes<PoolChangeOutcome>>("removed");
        var absDelta = new AbsExpression<Ctx>(
            new PreviousOutcomeFieldExpression<Ctx, PoolChangeOutcome>(
                removedKey, o => o.AppliedDelta));

        return new EffectProgram<Ctx>(new CausalSequenceEffectNode<Ctx>([
            new ModifyDefensivePoolNode<Ctx>(
                CombatantTargetSelectors.EventTarget,
                StandardCombatIds.BlockDefensivePool,
                Const(-5),
                resultKey: removedKey),
            new ConditionalEffectNode<Ctx>(
                new ComparisonExpression<Ctx>(absDelta, ComparisonOperator.Greater, Const(0)),
                then: new DealDamageNode<Ctx>(
                    CombatantTargetSelectors.EventTarget,
                    absDelta)),
        ]));
    }

    private static void Execute(
        EffectProgram<Ctx> program,
        CombatState combat,
        CombatantId? eventTargetId,
        CombatantId? sourceId)
    {
        var source = sourceId.HasValue
            ? combat.GetCombatant(sourceId.Value)
            : null;

        EffectProgramExecutor.Execute(
            program,
            new Ctx(),
            new TriggeredEffectActionBuildContext(
                new CombatantTargetSelectionContext(
                    Combat: combat,
                    Source: source,
                    EventTargetId: eventTargetId),
                new TriggeredEffectActionSource(SourceCombatantId: sourceId)),
            combat);
    }

    private static int Eval(ICombatExpression<Ctx, int> expr)
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);
        return expr.Evaluate(ctx, combat);
    }

    private static bool EvalBool(ICombatExpression<Ctx, bool> expr)
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);
        return expr.Evaluate(ctx, combat);
    }

    private static EffectExecutionContext<Ctx> MakeContext(CombatState combat) =>
        new(new Ctx(),
            new TriggeredEffectActionBuildContext(
                new CombatantTargetSelectionContext(combat, Source: null, EventTargetId: null),
                TriggeredEffectActionSource.None));

    private static ConstantExpression<Ctx> Const(int value) => new(value);
    private static BoolConstantExpression<Ctx> BoolConst(bool val) => new(val);

    private sealed record Ctx;

    // Test-only constant bool expression
    private sealed class BoolConstantExpression<T>(bool value) : ICombatExpression<T, bool>
        where T : class
    {
        public bool Evaluate(EffectExecutionContext<T> context, CombatState combat) => value;
    }

    // Test-only side-effect expression for verifying short-circuit behavior
    private sealed class SideEffectExpression<T>(Func<bool> body) : ICombatExpression<T, bool>
        where T : class
    {
        public bool Evaluate(EffectExecutionContext<T> context, CombatState combat) => body();
    }
}
