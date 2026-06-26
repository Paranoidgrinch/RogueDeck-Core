using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

/// <summary>
/// Regression tests for correct nested causal execution order (P0.1 fix).
///
/// Before the onComplete fix, a parent CausalSequenceEffectNode would
/// enqueue its next child BEFORE the current child's own continuations
/// finished, producing wrong ordering such as A → B → D → C instead of
/// A → B → C → D.
/// </summary>
public class EffectProgramNestingTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    // ── Trace-based ordering tests ────────────────────────────────────────────
    //
    // A LoggingRecipe appends a label when BuildEffectRequests is called.
    // Since recipe building is synchronous, the label order directly reflects
    // execution order.

    [Fact]
    public void NestedCausalSequenceExecutesInnerChildrenBeforeOuterNext()
    {
        // Outer Causal [Inner Causal [A, B, C], D]
        // Expected: A → B → C → D
        var log = new List<string>();
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);

        var program = new EffectProgram<Ctx>(new CausalSequenceEffectNode<Ctx>([
            new CausalSequenceEffectNode<Ctx>([
                Log("A", log), Log("B", log), Log("C", log),
            ]),
            Log("D", log),
        ]));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(["A", "B", "C", "D"], log);
    }

    [Fact]
    public void RepeatWithCausalBodyCompletesEachIterationBeforeStartingNext()
    {
        // Repeat 2: Causal [A, B]
        // Expected: A → B → A → B
        var log = new List<string>();
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);

        var program = new EffectProgram<Ctx>(new RepeatEffectNode<Ctx>(
            new ConstantExpression<Ctx>(2),
            new CausalSequenceEffectNode<Ctx>([
                Log("A", log), Log("B", log),
            ])));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(["A", "B", "A", "B"], log);
    }

    [Fact]
    public void ForEachWithCausalBodyCompletesEachTargetBeforeStartingNext()
    {
        // ForEach [goblin1, goblin2]: Causal [A, B]
        // Expected: A → B → A → B  (target 1 fully done before target 2 starts)
        var log = new List<string>();
        var registry = CombatTestFactory.CreateStandardRegistry();

        var combat = CombatTestFactory.CreateCombatWithHeroAndTwoGoblins();
        var ctx = MakeContext(combat);

        var program = new EffectProgram<Ctx>(new ForEachTargetEffectNode<Ctx>(
            CombatantTargetSelectors.AllEnemiesOfSource,
            new CausalSequenceEffectNode<Ctx>([
                Log("A", log), Log("B", log),
            ])));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(["A", "B", "A", "B"], log);
    }

    [Fact]
    public void ForEachTargetOrderIsStableAndMatchesInsertionOrder()
    {
        // Three goblins added in order A, B, C. The ForEach must visit them
        // in that same deterministic order, not an arbitrary hash order.
        var visitOrder = new List<CombatantId>();

        var goblinA = new CombatantId("goblin_001");
        var goblinB = new CombatantId("goblin_002");
        var goblinC = new CombatantId("goblin_003");

        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndTwoGoblins();
        combat.AddCombatant(new CombatantState(
            goblinC,
            new CombatantDefinitionId("standard.goblin"),
            "combatant.goblin",
            StandardCombatIds.EnemyTeam,
            new HealthState(current: 12, max: 12)));

        var ctx = MakeContext(combat);

        // Use a recipe that records the current IterationTarget at execution time
        var program = new EffectProgram<Ctx>(new ForEachTargetEffectNode<Ctx>(
            CombatantTargetSelectors.AllEnemiesOfSource,
            new SideEffectNode<Ctx>((ctx, _) => { if (ctx.IterationTarget is { } t) visitOrder.Add(t); })));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal([goblinA, goblinB, goblinC], visitOrder);
    }

    // ── State-dependent causal ordering ──────────────────────────────────────
    //
    // These tests prove that each causal step observes the settled state from
    // all preceding steps. If ordering were wrong, the block values available
    // to each damage step would differ and produce a different final HP.

    [Fact]
    public void InnerCausalStepSeesBlockAddedByPreviousInnerStep()
    {
        // Outer Causal [Inner Causal [A: +5 block, B: Deal 3 damage], C: Deal 6 damage]
        //
        // Correct order (A → B → C):
        //   A: goblin gets 5 block
        //   B: 3 absorbed, 0 HP damage, 2 block left
        //   C: 2 absorbed, 4 HP damage → goblin at 8 HP
        //
        // Wrong order (A → C → B):
        //   A: 5 block
        //   C: 5 absorbed, 1 HP damage, 0 block left → goblin at 11 HP
        //   B: 3 HP damage → goblin at 8 HP
        //
        // The final HP differs only if C fires before B consumes block first.

        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);

        var program = new EffectProgram<Ctx>(new CausalSequenceEffectNode<Ctx>([
            new CausalSequenceEffectNode<Ctx>([
                new ModifyDefensivePoolNode<Ctx>(
                    CombatantTargetSelectors.EventTarget,
                    StandardCombatIds.BlockDefensivePool,
                    new ConstantExpression<Ctx>(5)),
                new DealDamageNode<Ctx>(
                    CombatantTargetSelectors.EventTarget,
                    new ConstantExpression<Ctx>(3)),
            ]),
            new DealDamageNode<Ctx>(
                CombatantTargetSelectors.EventTarget,
                new ConstantExpression<Ctx>(6)),
        ]));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // Correct order: B consumes 3 of the 5 block, leaving 2.
        // C then consumes 2, dealing 4 HP damage. Goblin = 12 - 4 = 8 HP.
        Assert.Equal(8, combat.GetCombatant(GoblinId).Health.Current);
    }

    [Fact]
    public void RepeatBodyReactionIsSettledBeforeNextIteration()
    {
        // Repeat 2: Deal 10 damage (goblin has 8 block that absorbs first hit fully).
        // After hit 1: block drops from 8 to 0. Hit 2 sees 0 block.
        //
        // If iterations were not properly separated, both hits might see the
        // initial 8 block and each be fully absorbed.

        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        combat.GetCombatant(GoblinId).AddDefensivePool(
            StandardCombatIds.BlockDefensivePool,
            new ValuePoolState(8));

        var ctx = MakeContext(combat);
        var program = new EffectProgram<Ctx>(new RepeatEffectNode<Ctx>(
            new ConstantExpression<Ctx>(2),
            new DealDamageNode<Ctx>(
                CombatantTargetSelectors.EventTarget,
                new ConstantExpression<Ctx>(10))));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // Iteration 1: 10 damage, 8 blocked → 2 HP damage. Block = 0.
        // Iteration 2: 10 damage, 0 blocked → 10 HP damage.
        // Total: 12 HP - 12 damage = 0 HP (dead, but clamped at 0).
        Assert.False(combat.GetCombatant(GoblinId).IsAlive);
    }

    [Fact]
    public void ConditionalWithCausalThenBranchCompletesBeforeParentContinues()
    {
        // Outer Causal [Conditional {true → Causal [A, B]}, C]
        // Expected: A → B → C  (not A → C → B)
        var log = new List<string>();
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);

        var program = new EffectProgram<Ctx>(new CausalSequenceEffectNode<Ctx>([
            new ConditionalEffectNode<Ctx>(
                new TrueExpression<Ctx>(),
                new CausalSequenceEffectNode<Ctx>([Log("A", log), Log("B", log)])),
            Log("C", log),
        ]));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(["A", "B", "C"], log);
    }

    [Fact]
    public void NestedRepeatInsideForEachCompletesEachRepeatBeforeNextTarget()
    {
        // ForEach enemies: Repeat 2: Log
        // Expected: A → A → A → A (two enemies × two repeats)
        // Key property: each full repeat-pair completes before moving to next target.
        var log = new List<string>();
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndTwoGoblins();
        var ctx = MakeContext(combat);

        var program = new EffectProgram<Ctx>(new ForEachTargetEffectNode<Ctx>(
            CombatantTargetSelectors.AllEnemiesOfSource,
            new RepeatEffectNode<Ctx>(
                new ConstantExpression<Ctx>(2),
                Log("X", log))));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(["X", "X", "X", "X"], log);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IEffectNode<Ctx> Log(string label, List<string> log) =>
        new SideEffectNode<Ctx>((ctx, _) => log.Add(label));

    private static EffectExecutionContext<Ctx> MakeContext(CombatState combat) =>
        new(new Ctx(),
            new TriggeredEffectActionBuildContext(
                new CombatantTargetSelectionContext(
                    Combat: combat,
                    Source: combat.GetCombatant(HeroId),
                    EventTargetId: GoblinId),
                new TriggeredEffectActionSource(SourceCombatantId: HeroId)));

    private sealed record Ctx;

    // Always-true boolean expression for use in tests.
    private sealed class TrueExpression<TCtx> : ICombatExpression<TCtx, bool>
        where TCtx : class
    {
        public bool Evaluate(EffectExecutionContext<TCtx> context, CombatState combat) => true;
    }
}
