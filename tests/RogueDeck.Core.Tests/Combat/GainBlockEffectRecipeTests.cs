using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class GainBlockEffectRecipeTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");
    private static readonly CardDefinitionId CardId = new("card.test");

    [Fact]
    public void BuildEffectRequestsUsesContextAmountTargetsAndSource()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var context = new TestContext(Amount: 4);
        var buildContext = new TriggeredEffectActionBuildContext(
            new CombatantTargetSelectionContext(
                Combat: combat,
                Source: combat.GetCombatant(HeroId)),
            new TriggeredEffectActionSource(
                SourceCombatantId: HeroId,
                SourceCardId: CardId));
        var recipe = new GainBlockEffectRecipe<TestContext>(
            CombatantTargetSelectors.AllEnemiesOfSource,
            new TestContextAmountProvider());

        var requests = recipe.BuildEffectRequests(
            context,
            buildContext);

        var request = Assert.IsType<GainBlockEffectRequest>(
            Assert.Single(requests));
        Assert.Equal(GoblinId, request.TargetCombatantId);
        Assert.Equal(4, request.Amount);
        Assert.Equal(HeroId, request.SourceCombatantId);
        Assert.Equal(CardId, request.SourceCardId);
    }

    [Fact]
    public void BuildEffectRequestsReturnsEmptyForNonPositiveAmount()
    {
        var recipe = new GainBlockEffectRecipe<TestContext>(
            CombatantTargetSelectors.Source,
            new FixedCombatValue<int>(0));

        var requests = recipe.BuildEffectRequests(
            new TestContext(Amount: 99),
            CreateBuildContext());

        Assert.Empty(requests);
    }

    [Fact]
    public void BuildEffectRequestsReturnsEmptyWhenSelectorFindsNoTarget()
    {
        var recipe = new GainBlockEffectRecipe<TestContext>(
            CombatantTargetSelectors.EventTarget,
            new FixedCombatValue<int>(4));

        var requests = recipe.BuildEffectRequests(
            new TestContext(Amount: 99),
            CreateBuildContext());

        Assert.Empty(requests);
    }

    [Fact]
    public void ConstructorRejectsNullTargetSelector()
    {
        Assert.Throws<ArgumentNullException>(
            () => new GainBlockEffectRecipe<TestContext>(
                null!,
                new FixedCombatValue<int>(4)));
    }

    [Fact]
    public void ConstructorRejectsNullAmountProvider()
    {
        Assert.Throws<ArgumentNullException>(
            () => new GainBlockEffectRecipe<TestContext>(
                CombatantTargetSelectors.Source,
                null!));
    }

    [Fact]
    public void BuildEffectRequestsRejectsNullContext()
    {
        var recipe = new GainBlockEffectRecipe<TestContext>(
            CombatantTargetSelectors.Source,
            new FixedCombatValue<int>(4));

        Assert.Throws<ArgumentNullException>(
            () => recipe.BuildEffectRequests(
                null!,
                CreateBuildContext()));
    }

    [Fact]
    public void BuildEffectRequestsRejectsNullBuildContext()
    {
        var recipe = new GainBlockEffectRecipe<TestContext>(
            CombatantTargetSelectors.Source,
            new FixedCombatValue<int>(4));

        Assert.Throws<ArgumentNullException>(
            () => recipe.BuildEffectRequests(
                new TestContext(Amount: 4),
                null!));
    }

    private static TriggeredEffectActionBuildContext CreateBuildContext()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        return new TriggeredEffectActionBuildContext(
            new CombatantTargetSelectionContext(
                Combat: combat,
                Source: combat.GetCombatant(HeroId)),
            new TriggeredEffectActionSource(
                SourceCombatantId: HeroId,
                SourceCardId: CardId));
    }

    private sealed record TestContext(int Amount);

    private sealed class TestContextAmountProvider
        : ICombatValueProvider<TestContext, int>
    {
        public int Resolve(TestContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            return context.Amount;
        }
    }
}
