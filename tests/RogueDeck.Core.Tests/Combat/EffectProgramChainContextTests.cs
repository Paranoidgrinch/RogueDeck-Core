using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

/// <summary>
/// Regression tests for Scenarios F and G: chain context must survive across
/// program continuation boundaries.
///
/// Current bug: when a causal program continuation fires, combat.CurrentEffectChain
/// is null (continuations execute outside the EnterEffectChain scope of the
/// queue processor). Each continuation step therefore creates a NEW root chain
/// via the public EnqueueEffect overload (CurrentEffectChain ?? CreateRootEffectChain()).
///
/// This causes two problems:
///   F — Two consecutive causal steps share no chain identity; they appear as
///       two independent root events.
///   G — Trigger depth resets to zero at the chain boundary, undermining depth
///       limits and re-entry policies that span program steps.
///
/// Tests marked Skip will pass after chain context is preserved across
/// continuations (9.5E).
/// </summary>
public class EffectProgramChainContextTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    // ── Scenario F: chain identity survives continuation ─────────────────────
    //
    // Two causal program steps each enqueue a ChainRecordingRequest.
    // Both effects must be processed within the SAME chain context (same chain ID).
    // Currently they each receive a fresh root chain.

    [Fact]
    public void TwoCausalProgramStepsShareTheSameChainIdentity()
    {
        var observedChainIds = new List<CombatEffectChainId>();

        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterEffectRequestHandler(
            new DelegateEffectHandler<ChainRecordingRequest>(combat =>
                observedChainIds.Add(combat.CurrentEffectChain!.Id)));
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);

        var program = new EffectProgram<Ctx>(new CausalSequenceEffectNode<Ctx>([
            new SideEffectNode<Ctx>((ctx, combat) => combat.EnqueueEffect(new ChainRecordingRequest(), ctx.EffectChain!)),
            new SideEffectNode<Ctx>((ctx, combat) => combat.EnqueueEffect(new ChainRecordingRequest(), ctx.EffectChain!)),
        ]));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(2, observedChainIds.Count);
        Assert.Equal(observedChainIds[0], observedChainIds[1]);
    }

    // ── Scenario G: trigger depth does not reset across continuations ─────────
    //
    // A causal program has two steps. Step 1 fires a triggered effect.
    // Step 2 also fires a triggered effect. Both triggered effects are within
    // the SAME program chain, so they should be at the same trigger depth
    // relative to the program root. If the chain is reset between steps,
    // trigger-depth enforcement does not work as a cross-program-step budget.
    //
    // Specifically: a recursive trigger that is blocked by depth must also be
    // blocked when the recursion occurs across a program continuation boundary.

    [Fact]
    public void TriggerDepthBudgetSpansProgramSteps()
    {
        var depths = new List<int>();

        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterEffectRequestHandler(
            new DelegateEffectHandler<ChainRecordingRequest>(combat =>
            {
                depths.Add(combat.CurrentEffectChain!.TriggerDepth);
                // Fire a triggered event so we can measure depth inside a reaction.
                combat.EnqueueEvent(new ChainDepthProbeEvent());
            }));

        builder.RegisterCombatEventHandler(
            new DelegateEventHandler<ChainDepthProbeEvent>(combat =>
                depths.Add(combat.CurrentEffectChain!.TriggerDepth)));
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);

        // Program step 1 → depth 0 for the program step effect, depth 1 inside reaction
        // Program step 2 → depth 0 again? Or does it continue from the program chain?
        var program = new EffectProgram<Ctx>(new CausalSequenceEffectNode<Ctx>([
            new SideEffectNode<Ctx>((ctx, combat) => combat.EnqueueEffect(new ChainRecordingRequest(), ctx.EffectChain!)),
            new SideEffectNode<Ctx>((ctx, combat) => combat.EnqueueEffect(new ChainRecordingRequest(), ctx.EffectChain!)),
        ]));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // There should be 4 depth readings (2 effects + 2 event handlers).
        Assert.Equal(4, depths.Count);

        var step1EffectDepth = depths[0];
        var step1HandlerDepth = depths[1];
        var step2EffectDepth = depths[2];
        var step2HandlerDepth = depths[3];

        // Direct CombatEventHandlers run at the same depth as the event's chain,
        // which is the same chain as the effect that fired it.
        // (Only TriggeredEffectDefinition effects create depth+1 child chains.)
        Assert.Equal(step1EffectDepth, step1HandlerDepth);
        Assert.Equal(step2EffectDepth, step2HandlerDepth);

        // Both program steps must be at the same depth (they share the same program chain).
        // This verifies that the chain is NOT re-created fresh per step.
        Assert.Equal(step1EffectDepth, step2EffectDepth);
    }

    // ── Regression guard: independently enqueued effects create new chains ────
    //
    // Two effects enqueued independently (not via a program) must have DIFFERENT
    // chain IDs. This is existing behavior and must not regress.

    [Fact]
    public void IndependentEffectsReceiveDifferentChainIds()
    {
        var observedChainIds = new List<CombatEffectChainId>();

        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterEffectRequestHandler(
            new DelegateEffectHandler<ChainRecordingRequest>(combat =>
                observedChainIds.Add(combat.CurrentEffectChain!.Id)));
        var registry = builder.Build();

        var combat = new CombatState(new CombatId("combat_001"), randomSeed: 12345);

        combat.EnqueueEffect(new ChainRecordingRequest());
        combat.EnqueueEffect(new ChainRecordingRequest());

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(2, observedChainIds.Count);
        Assert.NotEqual(observedChainIds[0], observedChainIds[1]);
    }

    // ── Chain scope cleanup ───────────────────────────────────────────────────

    [Fact]
    public void ChainScopeIsCleanedUpAfterProgramCompletes()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);

        var program = new EffectProgram<Ctx>(new CausalSequenceEffectNode<Ctx>([
            new NoOpEffectNode<Ctx>(),
            new NoOpEffectNode<Ctx>(),
        ]));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Null(combat.CurrentEffectChain);
    }

    [Fact]
    public void ChainScopeIsCleanedUpWhenCombatEndsInsideProgram()
    {
        // End combat during the first step; second step must not run.
        // After the queue drains, CurrentEffectChain must be null.
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterEffectRequestHandler(
            new DelegateEffectHandler<ChainRecordingRequest>(combat =>
                combat.SetResult(CombatResult.Victory)));
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);

        var program = new EffectProgram<Ctx>(new CausalSequenceEffectNode<Ctx>([
            new SideEffectNode<Ctx>((ctx, combat) => combat.EnqueueEffect(new ChainRecordingRequest(), ctx.EffectChain!)),
            new SideEffectNode<Ctx>((ctx, combat) => combat.EnqueueEffect(new ChainRecordingRequest(), ctx.EffectChain!)),
        ]));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Null(combat.CurrentEffectChain);
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

    private sealed record ChainRecordingRequest : IEffectRequest;

    private sealed record ChainDepthProbeEvent : ICombatEvent;

    private sealed class DelegateEffectHandler<TRequest>(Action<CombatState> onResolve)
        : EffectRequestHandler<TRequest>
        where TRequest : class, IEffectRequest
    {
        protected override void Resolve(
            CombatState combat,
            CombatDefinitionRegistry registry,
            TRequest request) => onResolve(combat);
    }

    private sealed class DelegateEventHandler<TEvent>(Action<CombatState> onHandle)
        : CombatEventHandler<TEvent>
        where TEvent : ICombatEvent
    {
        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            TEvent combatEvent) => onHandle(combat);
    }
}
