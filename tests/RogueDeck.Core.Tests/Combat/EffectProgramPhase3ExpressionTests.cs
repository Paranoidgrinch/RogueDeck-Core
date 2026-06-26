using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

/// <summary>
/// Tests for Phase 3 expression vocabulary additions:
/// arithmetic completions, combat-value expressions, boolean state expressions,
/// and new combatant target selectors.
/// </summary>
public class EffectProgramPhase3ExpressionTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    // ── Arithmetic expressions ───────────────────────────────────────────────

    [Fact]
    public void NegateExpression_ReturnsNegatedValue()
    {
        var expr = new NegateExpression<Ctx>(new ConstantExpression<Ctx>(5));
        Assert.Equal(-5, Eval(expr));
    }

    [Fact]
    public void DivideExpression_DividesCorrectly()
    {
        var expr = new DivideExpression<Ctx>(
            new ConstantExpression<Ctx>(10),
            new ConstantExpression<Ctx>(3));
        Assert.Equal(3, Eval(expr)); // integer division
    }

    [Fact]
    public void DivideExpression_ReturnsZeroOnZeroDivisorByDefault()
    {
        var expr = new DivideExpression<Ctx>(
            new ConstantExpression<Ctx>(10),
            new ConstantExpression<Ctx>(0));
        Assert.Equal(0, Eval(expr));
    }

    [Fact]
    public void DivideExpression_FaultsOnZeroDivisorWhenPolicyIsFault()
    {
        var expr = new DivideExpression<Ctx>(
            new ConstantExpression<Ctx>(10),
            new ConstantExpression<Ctx>(0),
            DivideByZeroPolicy.Fault);
        Assert.Throws<InvalidOperationException>(() => Eval(expr));
    }

    [Fact]
    public void RemainderExpression_ReturnsRemainder()
    {
        var expr = new RemainderExpression<Ctx>(
            new ConstantExpression<Ctx>(10),
            new ConstantExpression<Ctx>(3));
        Assert.Equal(1, Eval(expr));
    }

    [Fact]
    public void RemainderExpression_ReturnsZeroOnZeroDivisorByDefault()
    {
        var expr = new RemainderExpression<Ctx>(
            new ConstantExpression<Ctx>(10),
            new ConstantExpression<Ctx>(0));
        Assert.Equal(0, Eval(expr));
    }

    [Fact]
    public void ClampExpression_ClampsToMin()
    {
        var expr = new ClampExpression<Ctx>(
            new ConstantExpression<Ctx>(-5),
            new ConstantExpression<Ctx>(0),
            new ConstantExpression<Ctx>(10));
        Assert.Equal(0, Eval(expr));
    }

    [Fact]
    public void ClampExpression_ClampsToMax()
    {
        var expr = new ClampExpression<Ctx>(
            new ConstantExpression<Ctx>(15),
            new ConstantExpression<Ctx>(0),
            new ConstantExpression<Ctx>(10));
        Assert.Equal(10, Eval(expr));
    }

    [Fact]
    public void ClampExpression_RetainsValueInRange()
    {
        var expr = new ClampExpression<Ctx>(
            new ConstantExpression<Ctx>(7),
            new ConstantExpression<Ctx>(0),
            new ConstantExpression<Ctx>(10));
        Assert.Equal(7, Eval(expr));
    }

    // ── Combat-value expressions ─────────────────────────────────────────────

    [Fact]
    public void CombatantCurrentHealthExpression_ReturnsCurrentHealth()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var expr = new CombatantCurrentHealthExpression<Ctx>(CombatantTargetSelectors.Source);
        Assert.Equal(combat.GetCombatant(HeroId).Health.Current, Eval(expr, combat, HeroId));
    }

    [Fact]
    public void CombatantMaxHealthExpression_ReturnsMaxHealth()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var expr = new CombatantMaxHealthExpression<Ctx>(CombatantTargetSelectors.EventTarget);
        Assert.Equal(combat.GetCombatant(GoblinId).Health.Max, Eval(expr, combat, HeroId, GoblinId));
    }

    [Fact]
    public void CombatantMissingHealthExpression_ReturnsMissingHealth()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var registry = CombatTestFactory.CreateStandardRegistry();

        // Damage hero for 3
        combat.EnqueueEffect(new DealDamageEffectRequest(HeroId, 3));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var hero = combat.GetCombatant(HeroId);
        var expected = hero.Health.Max - hero.Health.Current;

        var expr = new CombatantMissingHealthExpression<Ctx>(CombatantTargetSelectors.Source);
        Assert.Equal(expected, Eval(expr, combat, HeroId));
    }

    [Fact]
    public void CombatantStatusStacksExpression_ReturnsTotalStacks()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var registry = CombatTestFactory.CreateStandardRegistry();

        combat.EnqueueEffect(new ApplyStatusEffectRequest(
            GoblinId, StandardCombatIds.PoisonStatus, Stacks: 3, DurationTurns: 0, Charges: 0));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var expr = new CombatantStatusStacksExpression<Ctx>(
            CombatantTargetSelectors.EventTarget,
            StandardCombatIds.PoisonStatus);
        Assert.Equal(3, Eval(expr, combat, HeroId, GoblinId));
    }

    [Fact]
    public void CombatantCurrentResourceExpression_ReturnsZeroWhenResourceAbsent()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var expr = new CombatantCurrentResourceExpression<Ctx>(
            CombatantTargetSelectors.Source,
            StandardCombatIds.EnergyResource);
        Assert.Equal(0, Eval(expr, combat, HeroId));
    }

    [Fact]
    public void CombatantCurrentResourceExpression_ReturnsCurrentResource()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        // Give hero an energy pool
        combat.GetCombatant(HeroId).SetResource(StandardCombatIds.EnergyResource,
            new ValuePoolState(current: 3, max: 5));

        var expr = new CombatantCurrentResourceExpression<Ctx>(
            CombatantTargetSelectors.Source,
            StandardCombatIds.EnergyResource);
        Assert.Equal(3, Eval(expr, combat, HeroId));
    }

    [Fact]
    public void RoundNumberExpression_ReturnsCurrentRound()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var expr = new RoundNumberExpression<Ctx>();
        Assert.Equal(combat.CurrentRound, Eval(expr, combat, HeroId));
    }

    [Fact]
    public void TurnNumberExpression_ReturnsCurrentTurn()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var expr = new TurnNumberExpression<Ctx>();
        Assert.Equal(combat.CurrentTurn, Eval(expr, combat, HeroId));
    }

    // ── Boolean state expressions ────────────────────────────────────────────

    [Fact]
    public void TargetHasStatusExpression_ReturnsTrueWhenStatusPresent()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var registry = CombatTestFactory.CreateStandardRegistry();

        combat.EnqueueEffect(new ApplyStatusEffectRequest(
            GoblinId, StandardCombatIds.PoisonStatus, Stacks: 1, DurationTurns: 0, Charges: 0));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var expr = new TargetHasStatusExpression<Ctx>(
            CombatantTargetSelectors.EventTarget,
            StandardCombatIds.PoisonStatus);
        Assert.True(EvalBool(expr, combat, HeroId, GoblinId));
    }

    [Fact]
    public void TargetHasStatusExpression_ReturnsFalseWhenStatusAbsent()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var expr = new TargetHasStatusExpression<Ctx>(
            CombatantTargetSelectors.EventTarget,
            StandardCombatIds.PoisonStatus);
        Assert.False(EvalBool(expr, combat, HeroId, GoblinId));
    }

    [Fact]
    public void TargetIsAliveExpression_ReturnsTrueForLivingTarget()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var expr = new TargetIsAliveExpression<Ctx>(CombatantTargetSelectors.EventTarget);
        Assert.True(EvalBool(expr, combat, HeroId, GoblinId));
    }

    [Fact]
    public void TargetIsAliveExpression_ReturnsFalseForEmptySelector()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var expr = new TargetIsAliveExpression<Ctx>(CombatantTargetSelectors.EventTarget);
        // No event target set → selector returns empty → false
        Assert.False(EvalBool(expr, combat, HeroId, eventTarget: null));
    }

    // ── New selectors ────────────────────────────────────────────────────────

    [Fact]
    public void WithoutStatus_ExcludesCombatantsWithStatus()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var registry = CombatTestFactory.CreateStandardRegistry();

        combat.EnqueueEffect(new ApplyStatusEffectRequest(
            GoblinId, StandardCombatIds.PoisonStatus, Stacks: 1, DurationTurns: 0, Charges: 0));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var selector = CombatantTargetSelectors.WithoutStatus(
            CombatantTargetSelectors.AllEnemiesOfSource,
            StandardCombatIds.PoisonStatus);

        var ctx = MakeSelCtx(combat, HeroId);
        Assert.Empty(selector.ResolveTargets(ctx));
    }

    [Fact]
    public void WithoutStatus_IncludesCombatantsWithoutStatus()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var selector = CombatantTargetSelectors.WithoutStatus(
            CombatantTargetSelectors.AllEnemiesOfSource,
            StandardCombatIds.PoisonStatus);

        var ctx = MakeSelCtx(combat, HeroId);
        Assert.Single(selector.ResolveTargets(ctx));
    }

    [Fact]
    public void LowestHealthAllyOfSource_ReturnsAllyWithLowestHealth()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var registry = CombatTestFactory.CreateStandardRegistry();

        // Damage hero slightly
        combat.EnqueueEffect(new DealDamageEffectRequest(HeroId, 5));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var ctx = MakeSelCtx(combat, HeroId);
        var targets = CombatantTargetSelectors.LowestHealthAllyOfSource.ResolveTargets(ctx);

        Assert.Equal(HeroId, Assert.Single(targets));
    }

    [Fact]
    public void ExplicitCombatantSelector_ReturnsNamedCombatant()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var selector = CombatantTargetSelectors.Explicit(GoblinId);
        var ctx = MakeSelCtx(combat, HeroId);
        Assert.Equal(GoblinId, Assert.Single(selector.ResolveTargets(ctx)));
    }

    [Fact]
    public void ExplicitCombatantSelector_ReturnsDownedCombatant()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var registry = CombatTestFactory.CreateStandardRegistry();

        // Kill goblin — explicit selector still returns it (for state-check/revival use cases)
        combat.EnqueueEffect(new DealDamageEffectRequest(GoblinId, 9999));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var selector = CombatantTargetSelectors.Explicit(GoblinId);
        var ctx = MakeSelCtx(combat, HeroId);
        Assert.Equal(GoblinId, Assert.Single(selector.ResolveTargets(ctx)));
    }

    [Fact]
    public void ExplicitCombatantSelector_ReturnsEmptyForNonExistentCombatant()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var unknownId = new CombatantId("unknown_999");
        var selector = CombatantTargetSelectors.Explicit(unknownId);
        var ctx = MakeSelCtx(combat, HeroId);
        Assert.Empty(selector.ResolveTargets(ctx));
    }

    // ── Sign expression ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(5, 1)]
    [InlineData(-3, -1)]
    [InlineData(0, 0)]
    public void SignExpression_ReturnsSign(int input, int expected)
    {
        var expr = new SignExpression<Ctx>(new ConstantExpression<Ctx>(input));
        Assert.Equal(expected, Eval(expr));
    }

    // ── Health percentage ────────────────────────────────────────────────────

    [Fact]
    public void CombatantHealthPercentageExpression_ReturnsPercentage()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var registry = CombatTestFactory.CreateStandardRegistry();

        // Hero starts at 20/20 = 100%. Damage 10 → 10/20 = 50%.
        combat.EnqueueEffect(new DealDamageEffectRequest(HeroId, 10));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var expr = new CombatantHealthPercentageExpression<Ctx>(CombatantTargetSelectors.Source);
        Assert.Equal(50, Eval(expr, combat, HeroId));
    }

    // ── Defensive pool ───────────────────────────────────────────────────────

    [Fact]
    public void CombatantDefensivePoolExpression_ReturnsZeroWhenPoolAbsent()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var expr = new CombatantDefensivePoolExpression<Ctx>(
            CombatantTargetSelectors.Source, StandardCombatIds.BlockDefensivePool);
        Assert.Equal(0, Eval(expr, combat, HeroId));
    }

    [Fact]
    public void CombatantDefensivePoolExpression_ReturnsPoolValue()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var registry = CombatTestFactory.CreateStandardRegistry();

        combat.EnqueueEffect(new ModifyDefensivePoolEffectRequest(
            HeroId, StandardCombatIds.BlockDefensivePool, 8));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var expr = new CombatantDefensivePoolExpression<Ctx>(
            CombatantTargetSelectors.Source, StandardCombatIds.BlockDefensivePool);
        Assert.Equal(8, Eval(expr, combat, HeroId));
    }

    // ── Resource max / missing ───────────────────────────────────────────────

    [Fact]
    public void CombatantMaxResourceExpression_ReturnsZeroWhenResourceAbsent()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var expr = new CombatantMaxResourceExpression<Ctx>(
            CombatantTargetSelectors.Source, StandardCombatIds.EnergyResource);
        Assert.Equal(0, Eval(expr, combat, HeroId));
    }

    [Fact]
    public void CombatantMaxResourceExpression_ReturnsMax()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.GetCombatant(HeroId).SetResource(StandardCombatIds.EnergyResource,
            new ValuePoolState(current: 2, max: 5));
        var expr = new CombatantMaxResourceExpression<Ctx>(
            CombatantTargetSelectors.Source, StandardCombatIds.EnergyResource);
        Assert.Equal(5, Eval(expr, combat, HeroId));
    }

    [Fact]
    public void CombatantMissingResourceExpression_ReturnsDifference()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.GetCombatant(HeroId).SetResource(StandardCombatIds.EnergyResource,
            new ValuePoolState(current: 2, max: 5));
        var expr = new CombatantMissingResourceExpression<Ctx>(
            CombatantTargetSelectors.Source, StandardCombatIds.EnergyResource);
        Assert.Equal(3, Eval(expr, combat, HeroId));
    }

    // ── Status duration / charges ────────────────────────────────────────────

    [Fact]
    public void CombatantStatusDurationExpression_ReturnsDuration()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var registry = CombatTestFactory.CreateStandardRegistry();

        combat.EnqueueEffect(new ApplyStatusEffectRequest(
            HeroId, StandardCombatIds.PoisonStatus, Stacks: 1, DurationTurns: 3, Charges: 0));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var expr = new CombatantStatusDurationExpression<Ctx>(
            CombatantTargetSelectors.Source, StandardCombatIds.PoisonStatus);
        Assert.Equal(3, Eval(expr, combat, HeroId));
    }

    [Fact]
    public void CombatantStatusChargesExpression_ReturnsCharges()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var registry = CombatTestFactory.CreateStandardRegistry();

        combat.EnqueueEffect(new ApplyStatusEffectRequest(
            HeroId, StandardCombatIds.PoisonStatus, Stacks: 1, DurationTurns: 0, Charges: 4));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var expr = new CombatantStatusChargesExpression<Ctx>(
            CombatantTargetSelectors.Source, StandardCombatIds.PoisonStatus);
        Assert.Equal(4, Eval(expr, combat, HeroId));
    }

    // ── TargetDowned / TargetExists ──────────────────────────────────────────

    [Fact]
    public void TargetDownedExpression_ReturnsFalseForLivingTarget()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var expr = new TargetDownedExpression<Ctx>(CombatantTargetSelectors.EventTarget);
        Assert.False(EvalBool(expr, combat, HeroId, GoblinId));
    }

    [Fact]
    public void TargetDownedExpression_ReturnsTrueAfterDeath()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var registry = CombatTestFactory.CreateStandardRegistry();

        combat.EnqueueEffect(new DealDamageEffectRequest(GoblinId, 9999));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // Use explicit selector so downed combatant is resolved (not filtered out by alive-check)
        var expr = new TargetDownedExpression<Ctx>(CombatantTargetSelectors.Explicit(GoblinId));
        Assert.True(EvalBool(expr, combat, HeroId));
    }

    [Fact]
    public void TargetExistsExpression_ReturnsTrueWhenSelectorReturnsTargets()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var expr = new TargetExistsExpression<Ctx>(CombatantTargetSelectors.AllEnemiesOfSource);
        Assert.True(EvalBool(expr, combat, HeroId));
    }

    [Fact]
    public void TargetExistsExpression_ReturnsFalseWhenSelectorEmpty()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var expr = new TargetExistsExpression<Ctx>(CombatantTargetSelectors.EventTarget);
        // No event target → empty selector
        Assert.False(EvalBool(expr, combat, HeroId, eventTarget: null));
    }

    // ── Collection aggregate expressions ────────────────────────────────────

    [Fact]
    public void CountTargetsExpression_ReturnsSelectorCount()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndTwoGoblins();
        var expr = new CountTargetsExpression<Ctx>(CombatantTargetSelectors.AllEnemiesOfSource);
        Assert.Equal(2, Eval(expr, combat, HeroId));
    }

    [Fact]
    public void SumOverTargetsExpression_SumsPerTargetExpression()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var registry = CombatTestFactory.CreateStandardRegistry();

        // Damage goblin for 4 → missing health = 4 (12 max - 8 current)
        combat.EnqueueEffect(new DealDamageEffectRequest(GoblinId, 4));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // Sum of missing-health over all enemies = 4
        var expr = new SumOverTargetsExpression<Ctx>(
            CombatantTargetSelectors.AllEnemiesOfSource,
            new CombatantMissingHealthExpression<Ctx>(CombatantTargetSelectors.IterationTarget));
        Assert.Equal(4, Eval(expr, combat, HeroId));
    }

    [Fact]
    public void AnyTargetMatchesExpression_ReturnsTrueWhenOneMatches()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var registry = CombatTestFactory.CreateStandardRegistry();

        combat.EnqueueEffect(new ApplyStatusEffectRequest(
            GoblinId, StandardCombatIds.PoisonStatus, Stacks: 1, DurationTurns: 0, Charges: 0));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var expr = new AnyTargetMatchesExpression<Ctx>(
            CombatantTargetSelectors.AllEnemiesOfSource,
            new TargetHasStatusExpression<Ctx>(
                CombatantTargetSelectors.IterationTarget,
                StandardCombatIds.PoisonStatus));
        Assert.True(EvalBool(expr, combat, HeroId));
    }

    [Fact]
    public void AnyTargetMatchesExpression_ReturnsFalseWhenNoneMatch()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var expr = new AnyTargetMatchesExpression<Ctx>(
            CombatantTargetSelectors.AllEnemiesOfSource,
            new TargetHasStatusExpression<Ctx>(
                CombatantTargetSelectors.IterationTarget,
                StandardCombatIds.PoisonStatus));
        Assert.False(EvalBool(expr, combat, HeroId));
    }

    [Fact]
    public void AllTargetsMatchExpression_ReturnsTrueWhenAllMatch()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        // Every enemy is alive → all match IsAlive
        var expr = new AllTargetsMatchExpression<Ctx>(
            CombatantTargetSelectors.AllEnemiesOfSource,
            new TargetIsAliveExpression<Ctx>(CombatantTargetSelectors.IterationTarget));
        Assert.True(EvalBool(expr, combat, HeroId));
    }

    [Fact]
    public void AllTargetsMatchExpression_ReturnsFalseWhenOneDoesNotMatch()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndTwoGoblins();
        var registry = CombatTestFactory.CreateStandardRegistry();

        // Kill only first goblin
        combat.EnqueueEffect(new DealDamageEffectRequest(GoblinId, 9999));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // AllAliveCombatants excludes dead, so checking IsAlive over AllAliveCombatants
        // would always be true. Instead use Explicit selectors on all enemies:
        // AllEnemiesOfSource (AliveOnly=true) will skip the dead goblin → returns 1 target.
        // Use AllCombatants and IterationTarget to test all including dead:
        var expr = new AllTargetsMatchExpression<Ctx>(
            new AllCombatantsTargetSelector(AliveOnly: false),
            new TargetIsAliveExpression<Ctx>(CombatantTargetSelectors.IterationTarget));
        Assert.False(EvalBool(expr, combat, HeroId));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private sealed record Ctx;

    private static int Eval(
        ICombatExpression<Ctx, int> expr,
        CombatState? combat = null,
        CombatantId? source = null,
        CombatantId? eventTarget = null)
    {
        combat ??= CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat, source ?? HeroId, eventTarget);
        return expr.Evaluate(ctx, combat);
    }

    private static bool EvalBool(
        ICombatExpression<Ctx, bool> expr,
        CombatState combat,
        CombatantId source,
        CombatantId? eventTarget = null)
    {
        var ctx = MakeContext(combat, source, eventTarget);
        return expr.Evaluate(ctx, combat);
    }

    private static EffectExecutionContext<Ctx> MakeContext(
        CombatState combat,
        CombatantId sourceId,
        CombatantId? eventTargetId = null) =>
        new(new Ctx(),
            new TriggeredEffectActionBuildContext(
                new CombatantTargetSelectionContext(
                    Combat: combat,
                    Source: combat.GetCombatant(sourceId),
                    EventTargetId: eventTargetId),
                new TriggeredEffectActionSource(SourceCombatantId: sourceId)));

    private static CombatantTargetSelectionContext MakeSelCtx(
        CombatState combat,
        CombatantId sourceId,
        CombatantId? eventTargetId = null) =>
        new(Combat: combat,
            Source: combat.GetCombatant(sourceId),
            EventTargetId: eventTargetId);
}
