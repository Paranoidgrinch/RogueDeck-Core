using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// P0.2 — selectors declare a five-way static target-cardinality contract
// (ExactlyOne / ZeroOrOne / OneOrMore / ZeroOrMore / Unknown) so preflight can reason about which
// results are safe to read as a single target and which are guaranteed non-empty.
public class TargetSelectorCardinalityTests
{
    [Theory]
    [InlineData(typeof(SourceCombatantTargetSelector))]
    [InlineData(typeof(EventTargetCombatantTargetSelector))]
    [InlineData(typeof(IterationTargetCombatantTargetSelector))]
    public void AtMostOneSelectorsReportZeroOrOne(Type selectorType)
    {
        var selector = (ICombatantTargetSelector)Activator.CreateInstance(selectorType)!;
        Assert.Equal(TargetSelectorCardinality.ZeroOrOne, selector.Cardinality);
    }

    [Fact]
    public void ExplicitSelectorReportsZeroOrOne() =>
        Assert.Equal(
            TargetSelectorCardinality.ZeroOrOne,
            CombatantTargetSelectors.Explicit(new CombatantId("x")).Cardinality);

    [Fact]
    public void HealthExtremeSelectorsReportZeroOrOne()
    {
        Assert.Equal(TargetSelectorCardinality.ZeroOrOne, CombatantTargetSelectors.LowestHealthEnemyOfSource.Cardinality);
        Assert.Equal(TargetSelectorCardinality.ZeroOrOne, CombatantTargetSelectors.HighestHealthAllyOfSource.Cardinality);
        Assert.Equal(
            TargetSelectorCardinality.ZeroOrOne,
            CombatantTargetSelectors.LowestHealth(CombatantTargetSelectors.AllEnemiesOfSource).Cardinality);
        Assert.Equal(
            TargetSelectorCardinality.ZeroOrOne,
            CombatantTargetSelectors.LowestHealthPercentage(CombatantTargetSelectors.AllEnemiesOfSource).Cardinality);
    }

    [Fact]
    public void MultiTargetSelectorsReportZeroOrMore()
    {
        Assert.Equal(TargetSelectorCardinality.ZeroOrMore, CombatantTargetSelectors.AllEnemiesOfSource.Cardinality);
        Assert.Equal(TargetSelectorCardinality.ZeroOrMore, CombatantTargetSelectors.AllAlliesOfSource.Cardinality);
        Assert.Equal(TargetSelectorCardinality.ZeroOrMore, CombatantTargetSelectors.AllAliveCombatants.Cardinality);
        Assert.Equal(
            TargetSelectorCardinality.ZeroOrMore,
            CombatantTargetSelectors.WithStatus(CombatantTargetSelectors.AllEnemiesOfSource, new StatusDefinitionId("s")).Cardinality);
    }

    [Theory]
    [InlineData(TargetSelectorCardinality.ExactlyOne, true)]
    [InlineData(TargetSelectorCardinality.ZeroOrOne, true)]
    [InlineData(TargetSelectorCardinality.OneOrMore, false)]
    [InlineData(TargetSelectorCardinality.ZeroOrMore, false)]
    [InlineData(TargetSelectorCardinality.Unknown, false)]
    public void IsAtMostOneTarget_CoversAllFiveCardinalities(
        TargetSelectorCardinality cardinality, bool expected) =>
        Assert.Equal(expected, cardinality.IsAtMostOneTarget());

    [Theory]
    [InlineData(TargetSelectorCardinality.ExactlyOne, true)]
    [InlineData(TargetSelectorCardinality.OneOrMore, true)]
    [InlineData(TargetSelectorCardinality.ZeroOrOne, false)]
    [InlineData(TargetSelectorCardinality.ZeroOrMore, false)]
    [InlineData(TargetSelectorCardinality.Unknown, false)]
    public void IsGuaranteedNonEmpty_CoversAllFiveCardinalities(
        TargetSelectorCardinality cardinality, bool expected) =>
        Assert.Equal(expected, cardinality.IsGuaranteedNonEmpty());
}
