using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

/// <summary>
/// Regression tests for program-level execution limits (Step 9.5K).
///
/// CombatExecutionLimits currently only bounds effect/event queue processing.
/// Program-specific limits (MaxProgramSteps, MaxRepeatIterations, etc.) do not
/// exist yet. The existing per-node MaxCount/MaxIterations fields provide
/// per-node bounds but not global program-wide limits.
///
/// Tests marked Skip will pass after program-level limits are added (9.5K).
/// Tests without Skip document existing per-node enforcement (regression guards).
/// </summary>
public class EffectProgramExecutionLimitTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    // ── Existing per-node bounds (regression guards) ──────────────────────────

    [Fact]
    public void RepeatNodeRejectsNegativeCount()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            var registry = CombatTestFactory.CreateStandardRegistry();
            var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
            var ctx = MakeContext(combat);

            var program = new EffectProgram<Ctx>(new RepeatEffectNode<Ctx>(
                new ConstantExpression<Ctx>(-1),
                new NoOpEffectNode<Ctx>()));

            EffectProgramExecutor.Execute(program, ctx, combat);
            new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
        });
    }

    [Fact]
    public void RepeatNodeRejectsCountExceedingMaxCount()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            var registry = CombatTestFactory.CreateStandardRegistry();
            var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
            var ctx = MakeContext(combat);

            var program = new EffectProgram<Ctx>(new RepeatEffectNode<Ctx>(
                new ConstantExpression<Ctx>(33),
                new NoOpEffectNode<Ctx>(),
                maxCount: 32));

            EffectProgramExecutor.Execute(program, ctx, combat);
            new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
        });
    }

    [Fact]
    public void RepeatNodeAtExactMaxCountSucceeds()
    {
        var log = new List<string>();
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);

        var program = new EffectProgram<Ctx>(new RepeatEffectNode<Ctx>(
            new ConstantExpression<Ctx>(32),
            new SideEffectNode<Ctx>((ctx, _) => log.Add("X")),
            maxCount: 32));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(32, log.Count);
    }

    [Fact]
    public void ForEachNodeRejectsTargetCountExceedingMaxIterations()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            var registry = CombatTestFactory.CreateStandardRegistry();
            var combat = CombatTestFactory.CreateCombatWithHeroAndTwoGoblins();
            var ctx = MakeContext(combat);

            // MaxIterations=1 but two enemies exist → must throw.
            var program = new EffectProgram<Ctx>(new ForEachTargetEffectNode<Ctx>(
                CombatantTargetSelectors.AllEnemiesOfSource,
                new NoOpEffectNode<Ctx>(),
                maxIterations: 1));

            EffectProgramExecutor.Execute(program, ctx, combat);
            new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
        });
    }

    // ── Global program step limit ─────────────────────────────────────────────

    [Fact]
    public void GlobalProgramStepLimitIsEnforcedAcrossNestedLoops()
    {
        // Repeat 4 × Repeat 4 = many node entries.
        // With maxProgramSteps=10 this must fault before completing.
        // Note: subsequent repeat iterations execute inside continuations, so
        // ResolvePendingQueues must also be inside the Assert.Throws block.
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);

        var program = new EffectProgram<Ctx>(
            new RepeatEffectNode<Ctx>(
                new ConstantExpression<Ctx>(4),
                new RepeatEffectNode<Ctx>(
                    new ConstantExpression<Ctx>(4),
                    new NoOpEffectNode<Ctx>())),
            maxProgramSteps: 10);

        Assert.Throws<InvalidOperationException>(() =>
        {
            EffectProgramExecutor.Execute(program, ctx, combat);
            new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
        });
    }

    [Fact]
    public void ExactGlobalStepMaximumSucceeds()
    {
        // A Repeat 3 with NoOp body:
        //   1 step for the RepeatNode root
        //   3 steps for the 3 NoOp bodies = 4 total
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);

        var program = new EffectProgram<Ctx>(
            new RepeatEffectNode<Ctx>(
                new ConstantExpression<Ctx>(3),
                new NoOpEffectNode<Ctx>()),
            maxProgramSteps: 4);

        // Should NOT throw.
        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
        Assert.Equal(4, ctx.ProgramStepCount);
    }

    [Fact]
    public void LimitViolationDiagnosticIdentifiesProgramAndNodePath()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);

        var progId = new EffectProgramId("test.limit_prog");
        var program = new EffectProgram<Ctx>(
            new RepeatEffectNode<Ctx>(
                new ConstantExpression<Ctx>(10),
                new NoOpEffectNode<Ctx>()),
            maxProgramSteps: 5,
            id: progId);

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            EffectProgramExecutor.Execute(program, ctx, combat);
            new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
        });

        Assert.Contains("test.limit_prog", ex.Message);
        Assert.Contains("5", ex.Message); // max steps
    }

    [Fact]
    public void ActiveScopeLimitIsEnforced()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);

        // Limit to 2 active scopes (root + 1 nested).
        // A doubly-nested repeat (root + outer + inner = 3) must exceed it.
        ctx.MaxActiveScopes = 2;

        var program = new EffectProgram<Ctx>(new RepeatEffectNode<Ctx>(
            new ConstantExpression<Ctx>(1),
            new RepeatEffectNode<Ctx>(
                new ConstantExpression<Ctx>(1),
                new NoOpEffectNode<Ctx>())));

        Assert.Throws<InvalidOperationException>(() =>
        {
            EffectProgramExecutor.Execute(program, ctx, combat);
            new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
        });
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
