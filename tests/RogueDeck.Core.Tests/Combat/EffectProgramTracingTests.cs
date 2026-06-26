using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

/// <summary>
/// Tests for Phase M deterministic execution tracing.
/// </summary>
public class EffectProgramTracingTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    // ── ProgramStarted / ProgramCompleted ────────────────────────────────────

    [Fact]
    public void SingleNodeProgramEmitsStartedAndCompleted()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var sink = new RecordingEffectProgramTraceSink();

        var progId = new EffectProgramId("test.trace");
        var program = new EffectProgram<Ctx>(new NoOpEffectNode<Ctx>(), id: progId);
        var ctx = MakeContext(combat, sink);

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var started = sink.EventsOfKind(EffectProgramTraceEventKind.ProgramStarted);
        var completed = sink.EventsOfKind(EffectProgramTraceEventKind.ProgramCompleted);

        Assert.Single(started);
        Assert.Single(completed);
        Assert.Equal(progId, started[0].ProgramId);
        Assert.Equal(progId, completed[0].ProgramId);
    }

    // ── NodeEntered order matches execution order ─────────────────────────────

    [Fact]
    public void CausalSequenceEmitsNodeEnteredInOrder()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var sink = new RecordingEffectProgramTraceSink();

        var program = new EffectProgram<Ctx>(new CausalSequenceEffectNode<Ctx>([
            new NoOpEffectNode<Ctx>(),
            new NoOpEffectNode<Ctx>(),
            new NoOpEffectNode<Ctx>(),
        ]));

        var ctx = MakeContext(combat, sink);
        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var entered = sink.EventsOfKind(EffectProgramTraceEventKind.NodeEntered)
            .Select(e => e.NodeTypeName)
            .ToList();

        Assert.Equal(
            ["CausalSequenceEffectNode`1", "NoOpEffectNode`1", "NoOpEffectNode`1", "NoOpEffectNode`1"],
            entered);
    }

    // ── Scope events: open and close balance ─────────────────────────────────

    [Fact]
    public void RepeatIterationsScopeEventsBalance()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var sink = new RecordingEffectProgramTraceSink();

        var program = new EffectProgram<Ctx>(new RepeatEffectNode<Ctx>(
            new ConstantExpression<Ctx>(3),
            new NoOpEffectNode<Ctx>()));

        var ctx = MakeContext(combat, sink);
        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var opened = sink.EventsOfKind(EffectProgramTraceEventKind.ScopeOpened).Count;
        var closed = sink.EventsOfKind(EffectProgramTraceEventKind.ScopeClosed).Count;

        Assert.Equal(3, opened);
        Assert.Equal(3, closed);
    }

    // ── LimitExceeded before exception ───────────────────────────────────────

    [Fact]
    public void ActiveScopeLimitEmitsLimitExceededBeforeThrowing()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var sink = new RecordingEffectProgramTraceSink();

        var ctx = MakeContext(combat, sink);
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

        var exceeded = sink.EventsOfKind(EffectProgramTraceEventKind.LimitExceeded);
        Assert.Single(exceeded);
        Assert.Contains("MaxActiveScopes=2", exceeded[0].Detail);
    }

    // ── Scope depth is correct at NodeEntered ────────────────────────────────

    [Fact]
    public void NodeEnteredReportsScopeDepthCorrectly()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var sink = new RecordingEffectProgramTraceSink();

        // RepeatNode body (NoOp) should see scope depth 2 (root + loop scope)
        var program = new EffectProgram<Ctx>(new RepeatEffectNode<Ctx>(
            new ConstantExpression<Ctx>(1),
            new NoOpEffectNode<Ctx>()));

        var ctx = MakeContext(combat, sink);
        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var entered = sink.EventsOfKind(EffectProgramTraceEventKind.NodeEntered);
        var repeatEntry = entered.First(e => e.NodeTypeName!.StartsWith("RepeatEffectNode"));
        var noOpEntry = entered.First(e => e.NodeTypeName!.StartsWith("NoOpEffectNode"));

        Assert.Equal(1, repeatEntry.ScopeDepth);
        Assert.Equal(2, noOpEntry.ScopeDepth);
    }

    // ── Tracing can be disabled (null sink does not allocate per call) ────────

    [Fact]
    public void NullSinkIsDefaultAndDoesNotThrow()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var program = new EffectProgram<Ctx>(new CausalSequenceEffectNode<Ctx>([
            new NoOpEffectNode<Ctx>(),
            new NoOpEffectNode<Ctx>(),
        ]));

        // Default context uses NullEffectProgramTraceSink — no exception should be thrown
        var ctx = MakeContext(combat);
        var ex = Record.Exception(() =>
        {
            EffectProgramExecutor.Execute(program, ctx, combat);
            new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
        });

        Assert.Null(ex);
        Assert.Same(NullEffectProgramTraceSink.Instance, ctx.TraceSink);
    }

    // ── Commit 10: execution / node / scope / chain identities ────────────────

    [Fact]
    public void AllTraceEventsShareTheExecutionAndChainIdentity()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var sink = new RecordingEffectProgramTraceSink();

        var program = new EffectProgram<Ctx>(new CausalSequenceEffectNode<Ctx>([
            new NoOpEffectNode<Ctx>(),
        ]));

        var ctx = MakeContext(combat, sink);
        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var executionIds = sink.Events.Select(e => e.ExecutionId).Distinct().ToList();
        Assert.Single(executionIds);

        // The chain identity is bound and non-null on every event.
        Assert.All(sink.Events, e => Assert.NotNull(e.ChainId));
        var chainIds = sink.Events.Select(e => e.ChainId).Distinct().ToList();
        Assert.Single(chainIds);
    }

    [Fact]
    public void NodeEnteredReportsStructuralNodePath()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var sink = new RecordingEffectProgramTraceSink();

        var program = new EffectProgram<Ctx>(new CausalSequenceEffectNode<Ctx>([
            new NoOpEffectNode<Ctx>(),
            new ConditionalEffectNode<Ctx>(
                new ConstantBoolExpression<Ctx>(true),
                new NoOpEffectNode<Ctx>()),
        ]));

        var ctx = MakeContext(combat, sink);
        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var paths = sink.EventsOfKind(EffectProgramTraceEventKind.NodeEntered)
            .Select(e => e.NodePath)
            .ToList();

        Assert.Equal(
            ["root", "root.causal[0]", "root.causal[1]", "root.causal[1].conditional.then"],
            paths);
    }

    [Fact]
    public void NestedIterationsGetDistinctScopeIds()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var sink = new RecordingEffectProgramTraceSink();

        var program = new EffectProgram<Ctx>(new RepeatEffectNode<Ctx>(
            new ConstantExpression<Ctx>(3),
            new NoOpEffectNode<Ctx>()));

        var ctx = MakeContext(combat, sink);
        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var openedIds = sink.EventsOfKind(EffectProgramTraceEventKind.ScopeOpened)
            .Select(e => e.ScopeId)
            .ToList();

        // Each iteration opens a fresh scope with a distinct, monotonic id; the root scope is 0.
        Assert.Equal([1L, 2L, 3L], openedIds);
        Assert.All(sink.Events, e => Assert.NotEqual(0, e.ExecutionId.Value));
    }

    [Fact]
    public void IdenticalProgramsOnFreshCombatsProduceIdenticalTraces()
    {
        static IReadOnlyList<(EffectProgramTraceEventKind, string?, long?, long, string?)> Run()
        {
            var registry = CombatTestFactory.CreateStandardRegistry();
            var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
            var sink = new RecordingEffectProgramTraceSink();

            var program = new EffectProgram<Ctx>(new CausalSequenceEffectNode<Ctx>([
                new NoOpEffectNode<Ctx>(),
                new RepeatEffectNode<Ctx>(
                    new ConstantExpression<Ctx>(2),
                    new NoOpEffectNode<Ctx>()),
            ]), id: new EffectProgramId("test.replay"));

            var ctx = new EffectExecutionContext<Ctx>(
                new Ctx(),
                new TriggeredEffectActionBuildContext(
                    new CombatantTargetSelectionContext(
                        Combat: combat,
                        Source: combat.GetCombatant(HeroId),
                        EventTargetId: GoblinId),
                    new TriggeredEffectActionSource(SourceCombatantId: HeroId)))
            { TraceSink = sink };

            EffectProgramExecutor.Execute(program, ctx, combat);
            new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

            return sink.Events
                .Select(e => (e.Kind, e.NodePath, e.ScopeId, e.ExecutionId.Value, e.NodeTypeName))
                .ToList();
        }

        // Same initial state + same program → identical trace, identities included.
        Assert.Equal(Run(), Run());
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed class ConstantBoolExpression<TCtx>(bool value)
        : ICombatExpression<TCtx, bool>
        where TCtx : class
    {
        public bool Evaluate(EffectExecutionContext<TCtx> context, CombatState combat) => value;
    }


    private static EffectExecutionContext<Ctx> MakeContext(
        CombatState combat,
        IEffectProgramTraceSink? sink = null)
    {
        var ctx = new EffectExecutionContext<Ctx>(
            new Ctx(),
            new TriggeredEffectActionBuildContext(
                new CombatantTargetSelectionContext(
                    Combat: combat,
                    Source: combat.GetCombatant(HeroId),
                    EventTargetId: GoblinId),
                new TriggeredEffectActionSource(SourceCombatantId: HeroId)));
        if (sink is not null)
            ctx.TraceSink = sink;
        return ctx;
    }

    private sealed record Ctx;
}
