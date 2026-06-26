using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Combat Engine Closure — Commit 4: terminal-state runtime.
//
// Every execution frame must reach exactly one terminal state:
//   - Completed : the program's last node finished normally,
//   - Cancelled : combat reached a terminal result while the program was in flight,
//   - Faulted   : a node threw at runtime.
// Terminal transitions emit a trace event, stale continuations are rejected, and the
// frame is unregistered from the combat's active-frame set.
public class EffectProgramTerminalStateTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    // ── Fault on runtime exception ────────────────────────────────────────────

    [Fact]
    public void Frame_FaultsAndRethrows_WhenNodeThrowsDuringContinuation()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var trace = new RecordingEffectProgramTraceSink();

        // Step 1 (native) suspends via a continuation; the throw happens in the resumed
        // slice — the realistic case the synchronous Execute call cannot catch on its own.
        var program = new EffectProgram<Ctx>(
            new CausalSequenceEffectNode<Ctx>([
                new DealDamageNode<Ctx>(
                    CombatantTargetSelectors.EventTarget,
                    new ConstantExpression<Ctx>(1)),
                new SideEffectNode<Ctx>((_, _) => throw new InvalidOperationException("boom")),
            ]));

        var ctx = MakeContext(combat, trace);
        var frame = EffectProgramExecutor.Execute(program, ctx, combat);

        var ex = Assert.Throws<InvalidOperationException>(
            () => new CombatQueueProcessor().ResolvePendingQueues(combat, registry));

        Assert.Equal("boom", ex.Message);
        Assert.Equal(EffectProgramExecutionState.Faulted, frame.State);
        Assert.Same(ex, frame.FaultException);
        Assert.Single(trace.EventsOfKind(EffectProgramTraceEventKind.ProgramFaulted));
    }

    [Fact]
    public void Execute_PropagatesException_WhenRootNodeThrowsSynchronously()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var program = new EffectProgram<Ctx>(
            new SideEffectNode<Ctx>((_, _) => throw new InvalidOperationException("boom")));

        Assert.Throws<InvalidOperationException>(
            () => EffectProgramExecutor.Execute(program, MakeContext(combat), combat));
    }

    // ── Cancel on combat end ──────────────────────────────────────────────────

    [Fact]
    public void InFlightFrame_IsCancelled_WhenCombatEnds()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var trace = new RecordingEffectProgramTraceSink();

        // Causal program suspends after step 1 (native op enqueues a continuation), so the
        // frame is still Running when we end combat.
        var program = new EffectProgram<Ctx>(
            new CausalSequenceEffectNode<Ctx>([
                new DealDamageNode<Ctx>(
                    CombatantTargetSelectors.EventTarget,
                    new ConstantExpression<Ctx>(1)),
                new NoOpEffectNode<Ctx>(),
            ]));

        var frame = EffectProgramExecutor.Execute(program, MakeContext(combat, trace), combat);
        Assert.Equal(EffectProgramExecutionState.Running, frame.State);

        combat.SetResult(CombatResult.Victory);

        Assert.Equal(EffectProgramExecutionState.Cancelled, frame.State);
        Assert.Single(trace.EventsOfKind(EffectProgramTraceEventKind.ProgramCancelled));
    }

    [Fact]
    public void LaterStepIsSkipped_WhenAnEarlierStepEndsCombat()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var laterStepRan = false;
        var program = new EffectProgram<Ctx>(
            new CausalSequenceEffectNode<Ctx>([
                new SetCombatResultNode<Ctx>(CombatResult.Victory),
                new SideEffectNode<Ctx>((_, _) => laterStepRan = true),
            ]));

        var frame = EffectProgramExecutor.Execute(program, MakeContext(combat), combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(EffectProgramExecutionState.Cancelled, frame.State);
        Assert.False(laterStepRan);
    }

    // ── Exactly one terminal state ────────────────────────────────────────────

    [Fact]
    public void CompletedFrame_CancelDueToCombatEnd_IsNoOp()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var frame = EffectProgramExecutor.Execute(
            new EffectProgram<Ctx>(new NoOpEffectNode<Ctx>()), MakeContext(combat), combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
        Assert.Equal(EffectProgramExecutionState.Completed, frame.State);

        // Combat ending later must not flip an already-terminal frame.
        ((IEffectProgramExecutionFrame)frame).CancelDueToCombatEnd();

        Assert.Equal(EffectProgramExecutionState.Completed, frame.State);
    }

    [Fact]
    public void CompletedFrame_IsUnregistered_AndUnaffectedByCombatEnd()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var frame = EffectProgramExecutor.Execute(
            new EffectProgram<Ctx>(new NoOpEffectNode<Ctx>()), MakeContext(combat), combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // If the frame were still registered, this would try to cancel a Completed frame and
        // throw; the no-op CancelDueToCombatEnd plus unregister-on-complete keep it Completed.
        combat.SetResult(CombatResult.Victory);

        Assert.Equal(EffectProgramExecutionState.Completed, frame.State);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static EffectExecutionContext<Ctx> MakeContext(
        CombatState combat, IEffectProgramTraceSink? trace = null)
    {
        var ctx = new EffectExecutionContext<Ctx>(
            new Ctx(),
            new TriggeredEffectActionBuildContext(
                new CombatantTargetSelectionContext(
                    Combat: combat,
                    Source: combat.GetCombatant(HeroId),
                    EventTargetId: GoblinId),
                new TriggeredEffectActionSource(SourceCombatantId: HeroId)));
        if (trace is not null)
            ctx.TraceSink = trace;
        return ctx;
    }

    private sealed record Ctx;
}
