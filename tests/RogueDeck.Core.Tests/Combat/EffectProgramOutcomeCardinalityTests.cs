using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

/// <summary>
/// Regression tests for outcome cardinality: zero targets, one target, and multiple targets.
///
/// Current gaps:
///   H – The "firstSlot ??= slot" pattern in every native-op handler silently discards
///       the second, third, ... outcomes when multiple targets are resolved.
///   I – When a target selector returns empty, the result key is never set and a
///       downstream Get() throws instead of returning a defined empty/absent outcome.
///
/// Tests marked Skip will pass after cardinality is redesigned (9.5H).
/// </summary>
public class EffectProgramOutcomeCardinalityTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinAId = new("goblin_001");
    private static readonly CombatantId GoblinBId = new("goblin_002");

    // ── Scenario H: Multi-target outcome retains all results ──────────────────

    [Fact]
    public void MultiTargetDamageOutcomeRetainsAllResults()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndTwoGoblins();

        // Use a selector that hits both goblins.
        var damageKey = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("damage");
        var program = new EffectProgram<Ctx>(new DealDamageNode<Ctx>(
            CombatantTargetSelectors.AllEnemiesOfSource,
            new ConstantExpression<Ctx>(5),
            resultKey: damageKey));

        var ctx = MakeContext(combat);

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // Both goblins should have taken 5 damage.
        Assert.Equal(7, combat.GetCombatant(GoblinAId).Health.Current);
        Assert.Equal(7, combat.GetCombatant(GoblinBId).Health.Current);

        // Both outcomes are stored in order; neither is silently dropped.
        var outcomes = ctx.Get(damageKey);
        Assert.Equal(2, outcomes.Results.Count);
        Assert.All(outcomes.Results, r => Assert.Equal(5, r.Outcome.RequestedAmount));
        Assert.All(outcomes.Results, r => Assert.Equal(5, r.Outcome.HealthLost));
    }

    // ── Single-target selector produces single outcome ────────────────────────
    //
    // When exactly one target is selected, the outcome is stored correctly.
    // This already works and acts as a regression guard.

    [Fact]
    public void SingleTargetDamageOutcomeIsStoredCorrectly()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var damageKey = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("damage");
        var program = new EffectProgram<Ctx>(new DealDamageNode<Ctx>(
            CombatantTargetSelectors.EventTarget,
            new ConstantExpression<Ctx>(5),
            resultKey: damageKey));

        var ctx = MakeContext(combat, eventTargetId: GoblinAId);

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.True(ctx.TryGet(damageKey, out var ordered));
        var outcome = ordered!.Single();
        Assert.Equal(5, outcome.RequestedAmount);
        Assert.Equal(5, outcome.HealthLost);
    }

    // ── Scenario I: Empty target selection — optional semantics ───────────────
    //
    // When the target selector returns empty, no effect is enqueued and no
    // outcome slot is filled. The downstream Get() currently throws.
    // After 9.5H, an empty/absent result is a defined outcome (not an error).

    [Fact]
    public void EmptyTargetSelectionProducesDefinedAbsentOutcome()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        // EventTargetId is null → EventTarget selector returns empty
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var damageKey = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("damage");
        var program = new EffectProgram<Ctx>(new DealDamageNode<Ctx>(
            CombatantTargetSelectors.EventTarget,
            new ConstantExpression<Ctx>(5),
            resultKey: damageKey));

        var ctx = new EffectExecutionContext<Ctx>(
            new Ctx(),
            new TriggeredEffectActionBuildContext(
                new CombatantTargetSelectionContext(
                    Combat: combat,
                    Source: combat.GetCombatant(HeroId),
                    EventTargetId: null),
                new TriggeredEffectActionSource(SourceCombatantId: HeroId)));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // No damage should have been dealt.
        Assert.Equal(12, combat.GetCombatant(GoblinAId).Health.Current);

        // The result must be explicitly absent (not a missing-key error).
        Assert.True(ctx.TryGet(damageKey, out _),
            "Empty target selection should produce a defined absent outcome, not a missing key.");
    }

    // ── Empty target selection without a result key does not throw ────────────
    //
    // When no result key is requested and the selector is empty, nothing should
    // happen and no exception should be thrown. This already works.

    [Fact]
    public void EmptyTargetSelectionWithoutResultKeyDoesNotThrow()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var program = new EffectProgram<Ctx>(new DealDamageNode<Ctx>(
            CombatantTargetSelectors.EventTarget,
            new ConstantExpression<Ctx>(5)));

        var ctx = new EffectExecutionContext<Ctx>(
            new Ctx(),
            new TriggeredEffectActionBuildContext(
                new CombatantTargetSelectionContext(
                    Combat: combat,
                    Source: combat.GetCombatant(HeroId),
                    EventTargetId: null),
                new TriggeredEffectActionSource(SourceCombatantId: HeroId)));

        var exception = Record.Exception(() =>
        {
            EffectProgramExecutor.Execute(program, ctx, combat);
            new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
        });

        Assert.Null(exception);
        Assert.Equal(12, combat.GetCombatant(GoblinAId).Health.Current);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static EffectExecutionContext<Ctx> MakeContext(
        CombatState combat,
        CombatantId? eventTargetId = null) =>
        new(new Ctx(),
            new TriggeredEffectActionBuildContext(
                new CombatantTargetSelectionContext(
                    Combat: combat,
                    Source: combat.GetCombatant(HeroId),
                    EventTargetId: eventTargetId),
                new TriggeredEffectActionSource(SourceCombatantId: HeroId)));

    private sealed record Ctx;
}
