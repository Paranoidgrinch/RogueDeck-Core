using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

/// <summary>
/// Regression tests for correct completion ordering across all mixed-nesting combinations.
///
/// Tests that are currently failing on the callback-based runtime are marked Skip with
/// the step that will fix them. Tests without Skip already pass and serve as
/// regression guards against future regressions.
/// </summary>
public class EffectProgramNestedCompletionTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    // ── Scenario A: Sequence containing a causal descendant ───────────────────
    //
    // Structure:
    //   Causal
    //   ├── Sequence
    //   │   └── Causal
    //   │       ├── A
    //   │       ├── B
    //   │       └── C
    //   └── D
    //
    // Current bug: SequenceEffectNode enqueues its onComplete (→ D) immediately
    // after starting the inner Causal's first child. The inner Causal's remaining
    // children (B, C) complete via callbacks that arrive AFTER D has already fired.
    // Actual trace: A, B, D, C
    // Expected trace: A, B, C, D

    [Fact]
    public void SequenceContainingCausalDescendantExecutesInCorrectOrder()
    {
        var log = new List<string>();
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);

        var program = new EffectProgram<Ctx>(new CausalSequenceEffectNode<Ctx>([
            new SequenceEffectNode<Ctx>([
                new CausalSequenceEffectNode<Ctx>([
                    Log("A", log), Log("B", log), Log("C", log),
                ]),
            ]),
            Log("D", log),
        ]));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(["A", "B", "C", "D"], log);
    }

    // ── Causal → Sequence (sibling batch inside causal sequence) ─────────────
    //
    // Structure:
    //   Causal
    //   ├── Sequence [A, B]
    //   └── C
    //
    // Expected: A, B, C  (Sequence children are immediate; C must still wait)

    [Fact]
    public void CausalWithBatchSequenceChildExecutesChildrenBeforeNextSibling()
    {
        var log = new List<string>();
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);

        var program = new EffectProgram<Ctx>(new CausalSequenceEffectNode<Ctx>([
            new SequenceEffectNode<Ctx>([Log("A", log), Log("B", log)]),
            Log("C", log),
        ]));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(["A", "B", "C"], log);
    }

    // ── Repeat → ForEach ─────────────────────────────────────────────────────
    //
    // Structure:
    //   Repeat 2
    //   └── ForEach [goblin1, goblin2]
    //       └── Log
    //
    // Expected: 4 log entries, all targets visited before repeat advances

    [Fact]
    public void RepeatContainingForEachCompletesEachRepeatBeforeAdvancing()
    {
        var log = new List<string>();
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndTwoGoblins();
        var ctx = MakeContext(combat);

        var program = new EffectProgram<Ctx>(new RepeatEffectNode<Ctx>(
            new ConstantExpression<Ctx>(2),
            new ForEachTargetEffectNode<Ctx>(
                CombatantTargetSelectors.AllEnemiesOfSource,
                Log("X", log))));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(4, log.Count);
        Assert.All(log, entry => Assert.Equal("X", entry));
    }

    // ── Repeat → Repeat ──────────────────────────────────────────────────────
    //
    // Structure:
    //   Repeat 2
    //   └── Repeat 3
    //       └── Log
    //
    // Expected: 6 log entries

    [Fact]
    public void NestedRepeatExecutesCorrectTotalIterations()
    {
        var log = new List<string>();
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);

        var program = new EffectProgram<Ctx>(new RepeatEffectNode<Ctx>(
            new ConstantExpression<Ctx>(2),
            new RepeatEffectNode<Ctx>(
                new ConstantExpression<Ctx>(3),
                Log("X", log))));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(6, log.Count);
    }

    // ── Conditional → Repeat ─────────────────────────────────────────────────

    [Fact]
    public void ConditionalWithRepeatBodyCompletesBeforeParentContinues()
    {
        var log = new List<string>();
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);

        var program = new EffectProgram<Ctx>(new CausalSequenceEffectNode<Ctx>([
            new ConditionalEffectNode<Ctx>(
                new TrueExpression<Ctx>(),
                new RepeatEffectNode<Ctx>(new ConstantExpression<Ctx>(2), Log("A", log))),
            Log("B", log),
        ]));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(["A", "A", "B"], log);
    }

    // ── Conditional → ForEach ────────────────────────────────────────────────

    [Fact]
    public void ConditionalWithForEachBodyCompletesBeforeParentContinues()
    {
        var log = new List<string>();
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndTwoGoblins();
        var ctx = MakeContext(combat);

        var program = new EffectProgram<Ctx>(new CausalSequenceEffectNode<Ctx>([
            new ConditionalEffectNode<Ctx>(
                new TrueExpression<Ctx>(),
                new ForEachTargetEffectNode<Ctx>(
                    CombatantTargetSelectors.AllEnemiesOfSource,
                    Log("A", log))),
            Log("B", log),
        ]));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(["A", "A", "B"], log);
    }

    // ── Combat-end inside nested body halts execution ─────────────────────────

    [Fact]
    public void CombatEndInsideNestedBodyHaltsRemainingWork()
    {
        var log = new List<string>();
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);

        // Repeat 3 with body: [Kill goblin (sets combat to ended), Log("A")]
        // After goblin dies, combat ends; remaining iterations must not run.
        var program = new EffectProgram<Ctx>(new RepeatEffectNode<Ctx>(
            new ConstantExpression<Ctx>(3),
            new CausalSequenceEffectNode<Ctx>([
                new DealDamageNode<Ctx>(new ExplicitCombatantTargetSelector(GoblinId), new ConstantExpression<Ctx>(999)),
                Log("A", log),
            ])));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // Once combat ends the queue processor stops; A may or may not appear
        // depending on ordering, but the program must not produce more than 1 "A".
        Assert.True(log.Count <= 1,
            $"Expected at most 1 log entry after combat end, got {log.Count}.");
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

    private sealed class TrueExpression<TCtx> : ICombatExpression<TCtx, bool>
        where TCtx : class
    {
        public bool Evaluate(EffectExecutionContext<TCtx> context, CombatState combat) => true;
    }
}
