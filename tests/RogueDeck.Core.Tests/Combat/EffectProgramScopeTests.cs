using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

/// <summary>
/// Regression tests for lexical scope behavior in Effect Program execution.
///
/// Results are stored per-scope: a child scope's results are not visible
/// after the scope closes. Parent-scope results remain readable from child
/// scopes.
/// </summary>
public class EffectProgramScopeTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinAId = new("goblin_001");
    private static readonly CombatantId GoblinBId = new("goblin_002");

    // ── Scenario D: Nested ForEach restores the outer target ─────────────────
    //
    // Structure:
    //   ForEach outer [goblin1, goblin2]
    //   └── Causal
    //       ├── ForEach inner [goblin1, goblin2]
    //       │   └── Record(inner target)
    //       └── Record(outer target)
    //
    // The final Record(outer) must read the current outer target, not null and
    // not the last inner target. Current bug: inner ForEach sets
    // IterationTarget = null at its end, so the outer Record sees null.

    [Fact]
    public void NestedForEachRestoresOuterTargetAfterInnerCompletes()
    {
        var outerTargetsAtRecord = new List<CombatantId?>();

        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndTwoGoblins();
        var ctx = MakeContext(combat);

        var program = new EffectProgram<Ctx>(new ForEachTargetEffectNode<Ctx>(
            CombatantTargetSelectors.AllEnemiesOfSource,
            new CausalSequenceEffectNode<Ctx>([
                // Inner ForEach iterates all enemies, then clears IterationTarget.
                new ForEachTargetEffectNode<Ctx>(
                    CombatantTargetSelectors.AllEnemiesOfSource,
                    new NoOpEffectNode<Ctx>()),
                // After the inner ForEach, capture what the outer target is.
                new SideEffectNode<Ctx>((ctx, _) => outerTargetsAtRecord.Add(ctx.IterationTarget)),
            ])));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // Outer ForEach has 2 iterations; each Record must see a non-null target.
        Assert.Equal(2, outerTargetsAtRecord.Count);
        Assert.All(outerTargetsAtRecord, t => Assert.NotNull(t));

        // The outer targets must be the two goblins in order.
        Assert.Equal([GoblinAId, GoblinBId], outerTargetsAtRecord!);
    }

    // ── ForEach clears iteration target after completion ──────────────────────
    //
    // After a top-level ForEach completes, the IterationTarget should be null/cleared.
    // This already works (via the explicit null-set in ExecuteForEachIteration).

    [Fact]
    public void IterationTargetIsClearedAfterForEachCompletes()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndTwoGoblins();
        var ctx = MakeContext(combat);

        // A recipe that checks IterationTarget after the ForEach
        var capturedAfter = new List<CombatantId?>();

        var program = new EffectProgram<Ctx>(new CausalSequenceEffectNode<Ctx>([
            new ForEachTargetEffectNode<Ctx>(
                CombatantTargetSelectors.AllEnemiesOfSource,
                new NoOpEffectNode<Ctx>()),
            new SideEffectNode<Ctx>((ctx, _) => capturedAfter.Add(ctx.IterationTarget)),
        ]));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Single(capturedAfter);
        Assert.Null(capturedAfter[0]);
    }

    // ── ForEach body sees the current iteration target ────────────────────────

    [Fact]
    public void ForEachBodySeesCorrectIterationTargetPerIteration()
    {
        var capturedTargets = new List<CombatantId?>();

        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndTwoGoblins();
        var ctx = MakeContext(combat);

        var program = new EffectProgram<Ctx>(new ForEachTargetEffectNode<Ctx>(
            CombatantTargetSelectors.AllEnemiesOfSource,
            new SideEffectNode<Ctx>((ctx, _) => capturedTargets.Add(ctx.IterationTarget))));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal([GoblinAId, GoblinBId], capturedTargets!);
    }

    // ── Result isolation ─────────────────────────────────────────────────────
    //
    // Results stored in a branch, repeat-iteration, or ForEach-iteration scope
    // must not be visible after that scope closes.

    [Fact]
    public void BranchLocalResultDoesNotEscapeConditional()
    {
        // Then-branch stores a damage result. After the conditional, TryGet must
        // return false because the branch scope has been closed.
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var damageKey = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("branch-damage");
        var found = new List<bool>();

        var buildCtx = new TriggeredEffectActionBuildContext(
            new CombatantTargetSelectionContext(
                Combat: combat, Source: combat.GetCombatant(HeroId), EventTargetId: GoblinAId),
            new TriggeredEffectActionSource(SourceCombatantId: HeroId));
        var execCtx = new EffectExecutionContext<CardPlayContext>(new CardPlayContext(null!), buildCtx);

        var program = new EffectProgram<CardPlayContext>(
            new CausalSequenceEffectNode<CardPlayContext>([
                new ConditionalEffectNode<CardPlayContext>(
                    new ComparisonExpression<CardPlayContext>(
                        new ConstantExpression<CardPlayContext>(1),
                        ComparisonOperator.Equal,
                        new ConstantExpression<CardPlayContext>(1)),
                    new DealDamageNode<CardPlayContext>(
                        CombatantTargetSelectors.EventTarget,
                        new ConstantExpression<CardPlayContext>(1),
                        resultKey: damageKey)),
                new SideEffectNode<CardPlayContext>((ctx, c) => found.Add(execCtx.TryGet<OrderedTargetOutcomes<DamageOutcome>>(damageKey, out _))),
            ]));

        EffectProgramExecutor.Execute(program, execCtx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Single(found);
        Assert.False(found[0],
            "Branch-local result must not be visible after the conditional scope closes.");
    }

    [Fact]
    public void RepeatIterationResultDoesNotLeakAfterLoopCompletes()
    {
        // Inside the repeat body, store a damage result. After the repeat,
        // TryGet for that result must return false — the iteration scope is closed.
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var damageKey = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("iter-damage");
        var foundAfter = new List<bool>();

        var buildCtx = new TriggeredEffectActionBuildContext(
            new CombatantTargetSelectionContext(
                Combat: combat, Source: combat.GetCombatant(HeroId), EventTargetId: GoblinAId),
            new TriggeredEffectActionSource(SourceCombatantId: HeroId));
        var execCtx = new EffectExecutionContext<CardPlayContext>(new CardPlayContext(null!), buildCtx);

        var program = new EffectProgram<CardPlayContext>(
            new CausalSequenceEffectNode<CardPlayContext>([
                new RepeatEffectNode<CardPlayContext>(
                    new ConstantExpression<CardPlayContext>(1),
                    new DealDamageNode<CardPlayContext>(
                        CombatantTargetSelectors.EventTarget,
                        new ConstantExpression<CardPlayContext>(1),
                        resultKey: damageKey)),
                // After the loop: the iteration scope is gone.
                new SideEffectNode<CardPlayContext>((ctx, c) => foundAfter.Add(execCtx.TryGet<OrderedTargetOutcomes<DamageOutcome>>(damageKey, out _))),
            ]));

        EffectProgramExecutor.Execute(program, execCtx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Single(foundAfter);
        Assert.False(foundAfter[0],
            "Result stored in an iteration scope must not be visible after the loop closes.");
    }

    [Fact]
    public void ParentScopeResultIsReadableFromChildScope()
    {
        // A result stored at program root must be readable inside a repeat body.
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var damageKey = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("root-damage");
        var foundInside = new List<bool>();

        var buildCtx = new TriggeredEffectActionBuildContext(
            new CombatantTargetSelectionContext(
                Combat: combat, Source: combat.GetCombatant(HeroId), EventTargetId: GoblinAId),
            new TriggeredEffectActionSource(SourceCombatantId: HeroId));
        var execCtx = new EffectExecutionContext<CardPlayContext>(new CardPlayContext(null!), buildCtx);

        var program = new EffectProgram<CardPlayContext>(
            new CausalSequenceEffectNode<CardPlayContext>([
                // Root scope: deal damage, store result.
                new DealDamageNode<CardPlayContext>(
                    CombatantTargetSelectors.EventTarget,
                    new ConstantExpression<CardPlayContext>(4),
                    resultKey: damageKey),
                // Child scope (repeat iteration): must be able to read root result.
                new RepeatEffectNode<CardPlayContext>(
                    new ConstantExpression<CardPlayContext>(1),
                    new SideEffectNode<CardPlayContext>((ctx, c) => foundInside.Add(execCtx.TryGet<OrderedTargetOutcomes<DamageOutcome>>(damageKey, out _)))),
            ]));

        EffectProgramExecutor.Execute(program, execCtx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Single(foundInside);
        Assert.True(foundInside[0],
            "Child scope must be able to read results stored in the parent scope.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static EffectExecutionContext<Ctx> MakeContext(CombatState combat) =>
        new(new Ctx(),
            new TriggeredEffectActionBuildContext(
                new CombatantTargetSelectionContext(
                    Combat: combat,
                    Source: combat.GetCombatant(HeroId),
                    EventTargetId: null),
                new TriggeredEffectActionSource(SourceCombatantId: HeroId)));

    private sealed record Ctx;
}
