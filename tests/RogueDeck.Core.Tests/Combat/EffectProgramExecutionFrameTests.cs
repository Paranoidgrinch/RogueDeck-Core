using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

/// <summary>
/// Phase B — Explicit execution frames.
///
/// Each EffectProgramExecutor.Execute call must produce a distinct
/// EffectProgramExecutionFrame that tracks the invocation's identity
/// and lifecycle state independently of the program definition.
/// </summary>
public class EffectProgramExecutionFrameTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    // ── Identity ──────────────────────────────────────────────────────────────

    [Fact]
    public void EachExecutionReceivesAFreshUniqueExecutionId()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var program = MakeNoOpProgram();

        var ctx1 = MakeContext(combat);
        var ctx2 = MakeContext(combat);

        var frame1 = EffectProgramExecutor.Execute(program, ctx1, combat);
        var frame2 = EffectProgramExecutor.Execute(program, ctx2, combat);

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.NotEqual(frame1.ExecutionId, frame2.ExecutionId);
    }

    [Fact]
    public void FrameExposesTheProgramIdFromTheDefinition()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var programId = new EffectProgramId("test.prog.identity");

        var program = new EffectProgram<Ctx>(
            new NoOpEffectNode<Ctx>(),
            id: programId);

        var ctx = MakeContext(combat);
        var frame = EffectProgramExecutor.Execute(program, ctx, combat);

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(programId, frame.ProgramId);
    }

    [Fact]
    public void FrameExposesTheSameExecutionContextPassedToExecute()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var program = MakeNoOpProgram();
        var ctx = MakeContext(combat);

        var frame = EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Same(ctx, frame.ExecutionContext);
    }

    // ── State transitions ─────────────────────────────────────────────────────

    [Fact]
    public void FrameIsInRunningStateImmediatelyAfterExecuteReturns()
    {
        // Execute is synchronous up to the first continuation. The frame must
        // transition to Running before Execute returns.
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var program = MakeNoOpProgram();
        var ctx = MakeContext(combat);

        var frame = EffectProgramExecutor.Execute(program, ctx, combat);

        // Frame is Running immediately after Execute (before queue processing).
        Assert.Equal(EffectProgramExecutionState.Running, frame.State);
    }

    [Fact]
    public void FrameIsInCompletedStateAfterQueueSettles()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var program = MakeNoOpProgram();
        var ctx = MakeContext(combat);

        var frame = EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(EffectProgramExecutionState.Completed, frame.State);
    }

    [Fact]
    public void CausalProgramFrameCompletesAfterAllStepsSettle()
    {
        // A 2-step causal program. The frame must be Completed only after the
        // second step's continuation fires, not after Execute returns.
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var damageKey = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("dmg");

        var program = new EffectProgram<CardPlayContext>(
            new CausalSequenceEffectNode<CardPlayContext>([
                new DealDamageNode<CardPlayContext>(
                    CombatantTargetSelectors.EventTarget,
                    new ConstantExpression<CardPlayContext>(3),
                    resultKey: damageKey),
                new HealNode<CardPlayContext>(
                    CombatantTargetSelectors.Source,
                    new PreviousOutcomeFieldExpression<CardPlayContext, DamageOutcome>(
                        damageKey, o => o.HealthLost)),
            ]));

        var source = combat.GetCombatant(HeroId);
        var buildContext = new TriggeredEffectActionBuildContext(
            new CombatantTargetSelectionContext(combat, source, GoblinId),
            new TriggeredEffectActionSource(HeroId));

        var ctx = new EffectExecutionContext<CardPlayContext>(new CardPlayContext(null!), buildContext);
        var frame = EffectProgramExecutor.Execute(program, ctx, combat);

        // Not yet completed — causal steps run asynchronously.
        Assert.Equal(EffectProgramExecutionState.Running, frame.State);

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // Now fully complete.
        Assert.Equal(EffectProgramExecutionState.Completed, frame.State);
    }

    [Fact]
    public void CountersDoNotLeakBetweenSeparateInvocations()
    {
        // Run a 4-step program, then run a 1-step program on a fresh context.
        // The second invocation's step count must start at 0, not carry over.
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var program4 = new EffectProgram<Ctx>(
            new RepeatEffectNode<Ctx>(
                new ConstantExpression<Ctx>(3),
                new NoOpEffectNode<Ctx>()),
            maxProgramSteps: 4);

        var program1 = new EffectProgram<Ctx>(
            new NoOpEffectNode<Ctx>(),
            maxProgramSteps: 4);

        var ctx1 = MakeContext(combat);
        EffectProgramExecutor.Execute(program4, ctx1, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // First invocation used 4 steps.
        Assert.Equal(4, ctx1.ProgramStepCount);

        var ctx2 = MakeContext(combat);
        EffectProgramExecutor.Execute(program1, ctx2, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // Second invocation starts at 0, uses only 1 step.
        Assert.Equal(1, ctx2.ProgramStepCount);
    }

    // ── Terminal state enforcement ────────────────────────────────────────────

    [Fact]
    public void CompletedFrameCannotBeCompletedAgain()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var frame = EffectProgramExecutor.Execute(MakeNoOpProgram(), MakeContext(combat), combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(EffectProgramExecutionState.Completed, frame.State);
        Assert.Throws<InvalidOperationException>(() => frame.MarkCompleted());
    }

    [Fact]
    public void CancelledFrameCannotBeCompletedSuccessfully()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var frame = EffectProgramExecutor.Execute(MakeNoOpProgram(), MakeContext(combat), combat);
        // Cancel before the queue processes the completion continuation.
        frame.MarkCancelled();

        Assert.Equal(EffectProgramExecutionState.Cancelled, frame.State);
        Assert.Throws<InvalidOperationException>(() => frame.MarkCompleted());
    }

    [Fact]
    public void FaultedFrameCannotTransitionToCompleted()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var frame = EffectProgramExecutor.Execute(MakeNoOpProgram(), MakeContext(combat), combat);
        frame.MarkFaulted(new InvalidOperationException("test fault"));

        Assert.Equal(EffectProgramExecutionState.Faulted, frame.State);
        Assert.Throws<InvalidOperationException>(() => frame.MarkCompleted());
    }

    [Fact]
    public void CompletedFrameCannotBeFaulted()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var frame = EffectProgramExecutor.Execute(MakeNoOpProgram(), MakeContext(combat), combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(EffectProgramExecutionState.Completed, frame.State);
        Assert.Throws<InvalidOperationException>(() =>
            frame.MarkFaulted(new InvalidOperationException("post-completion fault")));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static EffectProgram<Ctx> MakeNoOpProgram() =>
        new(new NoOpEffectNode<Ctx>());

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
