using System.Reflection;
using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

/// <summary>
/// Architecture guard tests for the Effect Program dispatch mechanism.
///
/// Phase E (modular dispatch) requires that new node types can be added and
/// executed without modifying EffectProgramExecutor. The central switch has
/// been replaced with an EffectNodeExecutorRegistry.
/// </summary>
public class EffectProgramDispatchArchitectureTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    // ── Backward-compatibility guard ──────────────────────────────────────────
    //
    // The executor still exposes Execute as a public static method; call sites
    // that previously ignored the return value still compile.

    [Fact]
    public void EffectProgramExecutorCurrentlyContainsCentralNodeSwitch()
    {
        var executorType = typeof(EffectProgramExecutor);
        Assert.NotNull(executorType);

        var methods = executorType.GetMethods(BindingFlags.Public | BindingFlags.Static);
        Assert.Contains(methods, m => m.Name == "Execute");
    }

    // ── Phase E: test-only node can execute via the registry ─────────────────

    [Fact]
    public void TestOnlyNodeCanBeRegisteredAndExecutedWithoutEditingCentralExecutor()
    {
        // Register a probe node type in a custom registry that does NOT include
        // any other built-in nodes. Then execute a program containing only the
        // probe node and verify the executor was called.

        var probeLog = new List<string>();
        var probeExec = new ProbeNodeExecutor(probeLog);
        var registry = new EffectNodeExecutorRegistry();
        registry.Register(typeof(ProbeNode<Ctx>), probeExec);
        registry.Seal();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);

        var program = new EffectProgram<Ctx>(new ProbeNode<Ctx>("ping"));
        EffectProgramExecutor.Execute(program, ctx, combat, registry: registry);
        new CombatQueueProcessor().ResolvePendingQueues(combat, CombatTestFactory.CreateStandardRegistry());

        Assert.Equal(["ping"], probeLog);
    }

    // ── Phase E: duplicate executor registration ──────────────────────────────

    [Fact]
    public void DuplicateNodeExecutorRegistrationFails()
    {
        var registry = new EffectNodeExecutorRegistry();
        var executor = new ProbeNodeExecutor([]);

        registry.Register(typeof(ProbeNode<Ctx>), executor);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            registry.Register(typeof(ProbeNode<Ctx>), executor));

        Assert.Contains("ProbeNode", ex.Message);
    }

    // ── Phase E: missing executor caught by preflight ─────────────────────────

    [Fact]
    public void MissingNodeExecutorIsDetectedByPreflight()
    {
        // A registry with only the probe executor; built-in nodes are missing.
        // A program containing NoOp (which is NOT in this registry) must fail
        // at Execute time, before any continuations fire.
        var registry = new EffectNodeExecutorRegistry();
        registry.Register(typeof(ProbeNode<Ctx>), new ProbeNodeExecutor([]));
        registry.Seal();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);
        var program = new EffectProgram<Ctx>(new NoOpEffectNode<Ctx>());

        var ex = Assert.Throws<InvalidOperationException>(() =>
            EffectProgramExecutor.Execute(program, ctx, combat, registry: registry));

        Assert.Contains("NoOpEffectNode", ex.Message);
    }

    // ── Node executor contract: executors only queue, they never mutate ──────
    //
    // An executor that bypasses the queue and mutates CombatState directly would
    // change visible state before the queue processor ever runs. This behavioral
    // guard proves that executing a DealDamage node does NOT change combatant
    // health until the queue is explicitly processed.

    [Fact]
    public void NodeExecutorMustNotMutateCombatStateDirectly()
    {
        var fullRegistry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var healthBefore = combat.GetCombatant(GoblinId).Health.Current;

        var program = new EffectProgram<CardPlayContext>(
            new DealDamageNode<CardPlayContext>(
                CombatantTargetSelectors.EventTarget,
                new ConstantExpression<CardPlayContext>(5)),
            id: new EffectProgramId("test.executor_contract"));

        var ctx = new EffectExecutionContext<CardPlayContext>(
            new CardPlayContext(fullRegistry.GetCard(StandardCombatIds.StrikeCard)),
            new TriggeredEffectActionBuildContext(
                new CombatantTargetSelectionContext(
                    Combat: combat,
                    Source: combat.GetCombatant(HeroId),
                    EventTargetId: GoblinId),
                new TriggeredEffectActionSource(SourceCombatantId: HeroId)));

        // Execute the program but deliberately do NOT run the queue processor.
        EffectProgramExecutor.Execute(program, ctx, combat, registry: fullRegistry.EffectNodeExecutors);

        // The executor must have queued a damage request, not applied damage directly.
        Assert.True(combat.HasPendingEffects,
            "DealDamageNodeExecutor must enqueue an effect request, not mutate state directly.");
        Assert.Equal(healthBefore, combat.GetCombatant(GoblinId).Health.Current);

        // Confirm that processing the queue actually applies the damage.
        new CombatQueueProcessor().ResolvePendingQueues(combat, fullRegistry);
        Assert.Equal(healthBefore - 5, combat.GetCombatant(GoblinId).Health.Current);
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

    // A test-only node. No built-in executor exists for this type; the test
    // registers its own executor in a custom registry.
    private sealed class ProbeNode<TCtx>(string label) : IEffectNode<TCtx>
    {
        public string Label { get; } = label;
        public IReadOnlyList<IEffectNode<TCtx>> Children => [];
    }

    private sealed class ProbeNodeExecutor(List<string> log) : IEffectNodeExecutor
    {
        public void Execute(
            IEffectNode node,
            IEffectExecutionContextCore ctx,
            CombatState combat,
            Action<CombatState>? onComplete,
            Action<IEffectNode, CombatState, Action<CombatState>?> dispatch)
        {
            log.Add(((ProbeNode<Ctx>)node).Label);
            if (onComplete is not null)
                combat.EnqueueContinuation(onComplete);
        }
    }
}
