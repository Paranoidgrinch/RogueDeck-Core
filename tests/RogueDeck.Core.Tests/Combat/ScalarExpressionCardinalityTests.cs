using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Final Closure — Work package 3: scalar expressions must not silently use the first of many
// targets. A scalar (single-target) read through a multi-target selector is rejected; an explicit
// FirstTarget(...) reduction is the sanctioned way to take one.
public class ScalarExpressionCardinalityTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    [Fact]
    public void ScalarExpression_WithMultiTargetSelector_RejectedAtConstruction()
    {
        // AllEnemiesOfSource is a ZeroOrMore selector — a scalar health read is ambiguous and is
        // rejected when the expression is built (preflight), not deferred to evaluation.
        var ex = Assert.Throws<ArgumentException>(() =>
            new CombatantCurrentHealthExpression<Ctx>(CombatantTargetSelectors.AllEnemiesOfSource));

        Assert.Contains("scalar expression", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(TargetSelectorCardinality.OneOrMore)]
    [InlineData(TargetSelectorCardinality.ZeroOrMore)]
    [InlineData(TargetSelectorCardinality.Unknown)]
    public void ScalarExpression_RejectsNonAtMostOneCardinalities(TargetSelectorCardinality cardinality)
    {
        var selector = new FixedCardinalitySelector(cardinality);
        Assert.Throws<ArgumentException>(() =>
            new CombatantCurrentHealthExpression<Ctx>(selector));
    }

    [Theory]
    [InlineData(TargetSelectorCardinality.ExactlyOne)]
    [InlineData(TargetSelectorCardinality.ZeroOrOne)]
    public void ScalarExpression_AcceptsAtMostOneCardinalities(TargetSelectorCardinality cardinality)
    {
        var selector = new FixedCardinalitySelector(cardinality);
        // Construction must not throw for an at-most-one selector.
        _ = new CombatantCurrentHealthExpression<Ctx>(selector);
    }

    private sealed class FixedCardinalitySelector(TargetSelectorCardinality cardinality)
        : ICombatantTargetSelector
    {
        public TargetSelectorCardinality Cardinality => cardinality;

        public IReadOnlyCollection<CombatantId> ResolveTargets(CombatantTargetSelectionContext context) =>
            Array.Empty<CombatantId>();
    }

    [Fact]
    public void ScalarExpression_RuntimeGuard_StillCatchesAMisreportingSelector()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndTwoGoblins();
        var ctx = MakeContext(combat);

        // A custom selector that claims ExactlyOne but resolves many — construction passes, so the
        // evaluation-time guard is the defense-in-depth that still catches it.
        var expr = new CombatantCurrentHealthExpression<Ctx>(new LyingSingleSelector());

        Assert.Throws<InvalidOperationException>(() => expr.Evaluate(ctx, combat));
    }

    private sealed class LyingSingleSelector : ICombatantTargetSelector
    {
        public TargetSelectorCardinality Cardinality => TargetSelectorCardinality.ExactlyOne;

        public IReadOnlyCollection<CombatantId> ResolveTargets(CombatantTargetSelectionContext context) =>
            CombatantTargetSelectors.AllEnemiesOfSource.ResolveTargets(context);
    }

    [Fact]
    public void FirstTarget_ReducesMultiTargetToSingle_AndScalarReadSucceeds()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndTwoGoblins();
        var ctx = MakeContext(combat);

        var expr = new CombatantCurrentHealthExpression<Ctx>(
            CombatantTargetSelectors.FirstTarget(CombatantTargetSelectors.AllEnemiesOfSource));

        Assert.Equal(12, expr.Evaluate(ctx, combat));
    }

    [Fact]
    public void FirstTarget_HasZeroOrOneCardinality()
    {
        var selector = CombatantTargetSelectors.FirstTarget(
            CombatantTargetSelectors.AllEnemiesOfSource);

        Assert.Equal(TargetSelectorCardinality.ZeroOrOne, selector.Cardinality);
    }

    [Fact]
    public void ScalarExpression_WithSingleSelector_ReadsThatTarget()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);

        var expr = new CombatantCurrentHealthExpression<Ctx>(
            CombatantTargetSelectors.EventTarget);

        Assert.Equal(12, expr.Evaluate(ctx, combat));
    }

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
