using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Final Closure — Work package 1: native-handler-fault binding.
//
// A native request enqueued by a program is resolved later, while the effect queue drains —
// outside EffectProgramExecutor.ExecuteNode's try/catch. If that handler throws, the owning
// program frame must still reach Faulted (with trace + terminal cleanup), not stay Running.
//
// A root SideEffectNode defers its onComplete via a continuation, and the queue orchestrator
// drains all effects before any continuation, so the frame is still Running when the enqueued
// request resolves — the exact queue-time fault path under test.
public class EffectProgramNativeFaultBindingTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    // ── Test-only throwing native operation ───────────────────────────────────

    private sealed record ThrowingNativeRequest : IEffectRequest;

    private sealed class ThrowingNativeHandler : EffectRequestHandler<ThrowingNativeRequest>
    {
        protected override void Resolve(
            CombatState combat, CombatDefinitionRegistry registry, ThrowingNativeRequest request) =>
            throw new InvalidOperationException("native boom");
    }

    // ── Frame faults on queue-time handler throw ──────────────────────────────

    [Fact]
    public void NativeHandlerFault_DuringQueueResolution_FaultsOwningFrame()
    {
        var registry = BuildRegistryWithThrowingHandler();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var trace = new RecordingEffectProgramTraceSink();

        var program = new EffectProgram<Ctx>(
            new SideEffectNode<Ctx>((_, c) => c.EnqueueEffect(new ThrowingNativeRequest())));

        var frame = EffectProgramExecutor.Execute(program, MakeContext(combat, trace), combat);
        Assert.Equal(EffectProgramExecutionState.Running, frame.State);

        var ex = Assert.Throws<InvalidOperationException>(
            () => new CombatQueueProcessor().ResolvePendingQueues(combat, registry));

        Assert.Equal("native boom", ex.Message);
        Assert.Equal(EffectProgramExecutionState.Faulted, frame.State);
        Assert.Same(ex, frame.FaultException);
        Assert.Single(trace.EventsOfKind(EffectProgramTraceEventKind.ProgramFaulted));
    }

    // ── Later causal step does not run after a queue-time fault ────────────────

    [Fact]
    public void StaleContinuation_IsRejected_AfterNativeHandlerFault()
    {
        var registry = BuildRegistryWithThrowingHandler();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var laterStepRan = false;
        var program = new EffectProgram<Ctx>(
            new CausalSequenceEffectNode<Ctx>([
                new SideEffectNode<Ctx>((_, c) => c.EnqueueEffect(new ThrowingNativeRequest())),
                new SideEffectNode<Ctx>((_, _) => laterStepRan = true),
            ]));

        var frame = EffectProgramExecutor.Execute(program, MakeContext(combat), combat);

        Assert.Throws<InvalidOperationException>(
            () => new CombatQueueProcessor().ResolvePendingQueues(combat, registry));

        Assert.Equal(EffectProgramExecutionState.Faulted, frame.State);
        Assert.False(laterStepRan);
    }

    // ── Nested frame faults ───────────────────────────────────────────────────

    [Fact]
    public void NestedFrame_Faults_WhenNativeHandlerThrows()
    {
        var registry = BuildRegistryWithThrowingHandler();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var program = new EffectProgram<Ctx>(
            new RepeatEffectNode<Ctx>(
                new ConstantExpression<Ctx>(3),
                new CausalSequenceEffectNode<Ctx>([
                    new SideEffectNode<Ctx>((_, c) => c.EnqueueEffect(new ThrowingNativeRequest())),
                    new NoOpEffectNode<Ctx>(),
                ])));

        var frame = EffectProgramExecutor.Execute(program, MakeContext(combat), combat);

        Assert.Throws<InvalidOperationException>(
            () => new CombatQueueProcessor().ResolvePendingQueues(combat, registry));

        Assert.Equal(EffectProgramExecutionState.Faulted, frame.State);
    }

    // ── Card-play cleanup runs for a queue-time fault ─────────────────────────

    [Fact]
    public void CardProgram_QueueTimeFault_MovesCardToDestination_NotStuckInHand()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.AllowUnsafeSideEffects = true;
        builder.RegisterEffectRequestHandler(new ThrowingNativeHandler());

        var cardId = new CardDefinitionId("test.queue_fault");
        builder.RegisterCard(new CardDefinitionBuilder(
            cardId,
            new PackageId("test"),
            displayNameKey: "card.qf.name",
            descriptionKey: "card.qf.description")
        {
            Program = new EffectProgram<CardPlayContext>(
                new SideEffectNode<CardPlayContext>((_, c) => c.EnqueueEffect(new ThrowingNativeRequest()))),
        });
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var card = new CardInstance(
            combat.CreateNextCardInstanceId(), cardId, HeroId, CardZone.Hand);
        combat.GetCardZones(HeroId).AddCard(card);

        Assert.Throws<InvalidOperationException>(() =>
            new CombatCardPlayProcessor().PlayCardInstance(
                combat, registry,
                new CardInstancePlayRequest(card.Id, HeroId, GoblinId)));

        // The broken play left the card in its destination zone, not replayable in hand.
        Assert.Equal(CardZone.DiscardPile, card.Zone);
        Assert.Empty(combat.GetCardZones(HeroId).Hand);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static CombatDefinitionRegistry BuildRegistryWithThrowingHandler()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterEffectRequestHandler(new ThrowingNativeHandler());
        return builder.Build();
    }

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
