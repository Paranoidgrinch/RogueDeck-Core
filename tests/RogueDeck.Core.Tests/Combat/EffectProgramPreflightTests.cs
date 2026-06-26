using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

/// <summary>
/// Regression tests for Scenario K: complete preflight validation.
/// </summary>
public class EffectProgramPreflightTests
{
    // ── Existing structural validation (already works) ────────────────────────

    [Fact]
    public void ProgramWithNullRootIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new EffectProgram<Ctx>(null!));
    }

    [Fact]
    public void ProgramWithNullChildNodeIsRejectedWithPathInMessage()
    {
        var node = new CausalSequenceEffectNode<Ctx>(
            new IEffectNode<Ctx>[] { new NoOpEffectNode<Ctx>(), null! });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new EffectProgram<Ctx>(node));

        Assert.Contains("root", ex.Message);
    }

    [Fact]
    public void ProgramExceedingMaxNodeDepthIsRejected()
    {
        // Build a chain of depth 65 (default max = 64).
        IEffectNode<Ctx> node = new NoOpEffectNode<Ctx>();
        for (var i = 0; i < 65; i++)
            node = new CausalSequenceEffectNode<Ctx>([node]);

        Assert.Throws<InvalidOperationException>(() =>
            new EffectProgram<Ctx>(node));
    }

    // ── Scenario K: preflight data-flow validation (not yet implemented) ──────
    //
    // These tests document the required behavior. They will pass once a full
    // preflight validator is introduced (9.5J).

    [Fact]
    public void PreflightRejectsConsumerBeforeProducer()
    {
        var damageKey = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("damage");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new EffectProgram<Ctx>(new CausalSequenceEffectNode<Ctx>([
                // Step 1 reads from damageKey before it is produced.
                new HealNode<Ctx>(
                    CombatantTargetSelectors.Source,
                    new PreviousOutcomeFieldExpression<Ctx, DamageOutcome>(damageKey, o => o.HealthLost)),
                // Step 2 produces damageKey — but too late.
                new DealDamageNode<Ctx>(
                    CombatantTargetSelectors.EventTarget,
                    new ConstantExpression<Ctx>(5),
                    resultKey: damageKey),
            ])));

        Assert.Contains("damage", ex.Message);
    }

    [Fact]
    public void PreflightRejectsMissingResultKey()
    {
        var missingKey = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("nonexistent");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new EffectProgram<Ctx>(new HealNode<Ctx>(
                CombatantTargetSelectors.Source,
                new PreviousOutcomeFieldExpression<Ctx, DamageOutcome>(missingKey, o => o.HealthLost))));

        Assert.Contains("nonexistent", ex.Message);
    }

    [Fact]
    public void PreflightRejectsBranchLocalResultUsedOutsideBranch()
    {
        var damageKey = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("damage");

        // damage is produced inside the then-branch, then consumed after the conditional.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new EffectProgram<Ctx>(new CausalSequenceEffectNode<Ctx>([
                new ConditionalEffectNode<Ctx>(
                    new ConstantBoolExpression<Ctx>(true),
                    new DealDamageNode<Ctx>(
                        CombatantTargetSelectors.EventTarget,
                        new ConstantExpression<Ctx>(5),
                        resultKey: damageKey)),
                new HealNode<Ctx>(
                    CombatantTargetSelectors.Source,
                    new PreviousOutcomeFieldExpression<Ctx, DamageOutcome>(damageKey, o => o.HealthLost)),
            ])));

        Assert.Contains("damage", ex.Message);
    }

    [Fact]
    public void PreflightRejectsNodeWithNoRegisteredExecutor()
    {
        // An empty registry has no executors, so even NoOp should be rejected.
        var emptyRegistry = new EffectNodeExecutorRegistry();
        emptyRegistry.Seal();

        var unknownNode = new UnknownNode<Ctx>();
        var program = new EffectProgram<Ctx>(unknownNode);
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            EffectProgramExecutor.Execute(program, ctx, combat, registry: emptyRegistry));

        Assert.Contains("UnknownNode", ex.Message);
    }

    [Fact]
    public void PreflightReturnAllDiagnosticsTogetherNotJustFirst()
    {
        var key1 = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("key1");
        var key2 = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("key2");

        // Two independent missing-key errors in a causal sequence.
        // Both must appear in the same exception message.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new EffectProgram<Ctx>(new CausalSequenceEffectNode<Ctx>([
                new HealNode<Ctx>(
                    CombatantTargetSelectors.Source,
                    new PreviousOutcomeFieldExpression<Ctx, DamageOutcome>(key1, o => o.HealthLost)),
                new HealNode<Ctx>(
                    CombatantTargetSelectors.Source,
                    new PreviousOutcomeFieldExpression<Ctx, DamageOutcome>(key2, o => o.HealthLost)),
            ])));

        Assert.Contains("key1", ex.Message);
        Assert.Contains("key2", ex.Message);
    }

    // ── Commit 8: typed result identity ───────────────────────────────────────

    [Fact]
    public void PreflightRejectsResultKeyConsumedAtDifferentType()
    {
        // Both keys share the name "shared" but store different outcome types.
        var producedAsDamage = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("shared");
        var consumedAsHeal = new EffectResultKey<OrderedTargetOutcomes<HealOutcome>>("shared");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new EffectProgram<Ctx>(new CausalSequenceEffectNode<Ctx>([
                new DealDamageNode<Ctx>(
                    CombatantTargetSelectors.EventTarget,
                    new ConstantExpression<Ctx>(5),
                    resultKey: producedAsDamage),
                new HealNode<Ctx>(
                    CombatantTargetSelectors.Source,
                    new PreviousOutcomeSumExpression<Ctx, HealOutcome>(consumedAsHeal, _ => 0)),
            ])));

        Assert.Contains("shared", ex.Message);
        Assert.Contains("produced as", ex.Message);
        Assert.Contains("consumed as", ex.Message);
    }

    [Fact]
    public void PreflightAcceptsResultKeyConsumedAtMatchingType()
    {
        var damageKey = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("damage");

        // Same name, same stored type — valid; must not throw.
        var program = new EffectProgram<Ctx>(new CausalSequenceEffectNode<Ctx>([
            new DealDamageNode<Ctx>(
                CombatantTargetSelectors.EventTarget,
                new ConstantExpression<Ctx>(5),
                resultKey: damageKey),
            new HealNode<Ctx>(
                CombatantTargetSelectors.Source,
                new PreviousOutcomeFieldExpression<Ctx, DamageOutcome>(damageKey, o => o.HealthLost)),
        ]));

        Assert.NotNull(program);
    }

    // ── Commit 8: outcome cardinality (one producer per result key) ───────────

    [Fact]
    public void PreflightRejectsDuplicateProducerInCausalSequence()
    {
        var dup = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("dup");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new EffectProgram<Ctx>(new CausalSequenceEffectNode<Ctx>([
                new DealDamageNode<Ctx>(
                    CombatantTargetSelectors.EventTarget,
                    new ConstantExpression<Ctx>(5),
                    resultKey: dup),
                new DealDamageNode<Ctx>(
                    CombatantTargetSelectors.EventTarget,
                    new ConstantExpression<Ctx>(3),
                    resultKey: dup),
            ])));

        Assert.Contains("dup", ex.Message);
        Assert.Contains("more than one", ex.Message);
    }

    [Fact]
    public void PreflightRejectsDuplicateProducerInBatchSequence()
    {
        var dup = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("dup");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new EffectProgram<Ctx>(new SequenceEffectNode<Ctx>([
                new DealDamageNode<Ctx>(
                    CombatantTargetSelectors.EventTarget,
                    new ConstantExpression<Ctx>(5),
                    resultKey: dup),
                new DealDamageNode<Ctx>(
                    CombatantTargetSelectors.EventTarget,
                    new ConstantExpression<Ctx>(3),
                    resultKey: dup),
            ])));

        Assert.Contains("more than one", ex.Message);
    }

    [Fact]
    public void PreflightAllowsSameResultKeyInMutuallyExclusiveBranches()
    {
        var key = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("branch");

        // The then- and else-branches are mutually exclusive, so producing the same key in
        // both is not a duplicate producer; branch-local keys never escape the conditional.
        var program = new EffectProgram<Ctx>(new ConditionalEffectNode<Ctx>(
            new ConstantBoolExpression<Ctx>(true),
            new DealDamageNode<Ctx>(
                CombatantTargetSelectors.EventTarget,
                new ConstantExpression<Ctx>(5),
                resultKey: key),
            new DealDamageNode<Ctx>(
                CombatantTargetSelectors.EventTarget,
                new ConstantExpression<Ctx>(3),
                resultKey: key)));

        Assert.NotNull(program);
    }

    // ── Commit 9: scalar reads require a single-target producer ───────────────

    [Fact]
    public void PreflightRejectsScalarReadOfMultiTargetProducer()
    {
        var damageKey = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("damage");

        // Produced over all enemies (multi-target), then read as a single target's field.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new EffectProgram<Ctx>(new CausalSequenceEffectNode<Ctx>([
                new DealDamageNode<Ctx>(
                    CombatantTargetSelectors.AllEnemiesOfSource,
                    new ConstantExpression<Ctx>(5),
                    resultKey: damageKey),
                new HealNode<Ctx>(
                    CombatantTargetSelectors.Source,
                    new PreviousOutcomeFieldExpression<Ctx, DamageOutcome>(damageKey, o => o.HealthLost)),
            ])));

        Assert.Contains("damage", ex.Message);
        Assert.Contains("single target", ex.Message);
    }

    [Fact]
    public void PreflightAcceptsScalarReadOfSingleTargetProducer()
    {
        var damageKey = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("damage");

        // EventTarget is a single-target selector, so a scalar read is unambiguous.
        var program = new EffectProgram<Ctx>(new CausalSequenceEffectNode<Ctx>([
            new DealDamageNode<Ctx>(
                CombatantTargetSelectors.EventTarget,
                new ConstantExpression<Ctx>(5),
                resultKey: damageKey),
            new HealNode<Ctx>(
                CombatantTargetSelectors.Source,
                new PreviousOutcomeFieldExpression<Ctx, DamageOutcome>(damageKey, o => o.HealthLost)),
        ]));

        Assert.NotNull(program);
    }

    [Fact]
    public void PreflightAcceptsAggregateReadOfMultiTargetProducer()
    {
        var damageKey = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("damage");

        // A sum aggregates across all targets, so a multi-target producer is valid.
        var program = new EffectProgram<Ctx>(new CausalSequenceEffectNode<Ctx>([
            new DealDamageNode<Ctx>(
                CombatantTargetSelectors.AllEnemiesOfSource,
                new ConstantExpression<Ctx>(5),
                resultKey: damageKey),
            new HealNode<Ctx>(
                CombatantTargetSelectors.Source,
                new PreviousOutcomeSumExpression<Ctx, DamageOutcome>(damageKey, o => o.HealthLost)),
        ]));

        Assert.NotNull(program);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    private sealed record Ctx;

    private static EffectExecutionContext<Ctx> MakeContext(CombatState combat) =>
        new(new Ctx(),
            new TriggeredEffectActionBuildContext(
                new CombatantTargetSelectionContext(
                    Combat: combat,
                    Source: combat.GetCombatant(HeroId),
                    EventTargetId: GoblinId),
                new TriggeredEffectActionSource(SourceCombatantId: HeroId)));

    private sealed class ConstantBoolExpression<TCtx>(bool value)
        : ICombatExpression<TCtx, bool>
        where TCtx : class
    {
        public bool Evaluate(EffectExecutionContext<TCtx> context, CombatState combat) => value;
    }

    private sealed class UnknownNode<TCtx> : IEffectNode<TCtx>
    {
        public IReadOnlyList<IEffectNode<TCtx>> Children => [];
    }
}
