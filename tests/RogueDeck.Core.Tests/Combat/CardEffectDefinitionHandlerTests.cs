using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class CardEffectDefinitionHandlerTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    [Fact]
    public void DealDamageRecipeBuildsDamageRequest()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);

        var card = new CardDefinitionBuilder(
            new CardDefinitionId("test.attack"),
            new PackageId("test"),
            displayNameKey: "card.test.attack.name",
            descriptionKey: "card.test.attack.description");

        var buildContext = new TriggeredEffectActionBuildContext(
            new CombatantTargetSelectionContext(
                Combat: combat,
                Source: hero,
                EventTargetId: GoblinId),
            new TriggeredEffectActionSource(
                SourceCombatantId: hero.Id,
                SourceCardId: card.Id));

        var recipe = new DealDamageEffectRecipe<CardPlayContext>(
            CombatantTargetSelectors.EventTarget,
            new FixedCombatValue<int>(7));

        var requests = recipe.BuildEffectRequests(new CardPlayContext(card.Build()), buildContext);

        var request = Assert.IsType<DealDamageEffectRequest>(Assert.Single(requests));

        Assert.Equal(GoblinId, request.TargetCombatantId);
        Assert.Equal(7, request.Amount);
        Assert.Equal(hero.Id, request.SourceCombatantId);
        Assert.Equal(card.Id, request.SourceCardId);
        Assert.Equal(DamageKind.Direct, request.Kind);
    }

    [Fact]
    public void RecipesExposeTheirTargetSelectorViaInterface()
    {
        ICombatEffectRecipe<CardPlayContext> damageRecipe =
            new DealDamageEffectRecipe<CardPlayContext>(
                CombatantTargetSelectors.EventTarget,
                new FixedCombatValue<int>(5));

        ICombatEffectRecipe<CardPlayContext> blockRecipe =
            new GainBlockEffectRecipe<CardPlayContext>(
                CombatantTargetSelectors.Source,
                new FixedCombatValue<int>(3));

        Assert.Same(CombatantTargetSelectors.EventTarget, damageRecipe.TargetSelector);
        Assert.Same(CombatantTargetSelectors.Source, blockRecipe.TargetSelector);
    }

    [Fact]
    public void DealDamageRecipeProducesNoRequestsWhenEventTargetIsAbsent()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);

        var card = new CardDefinitionBuilder(
            new CardDefinitionId("test.attack"),
            new PackageId("test"),
            displayNameKey: "card.test.attack.name",
            descriptionKey: "card.test.attack.description");

        var buildContext = new TriggeredEffectActionBuildContext(
            new CombatantTargetSelectionContext(
                Combat: combat,
                Source: hero,
                EventTargetId: null),
            new TriggeredEffectActionSource(
                SourceCombatantId: hero.Id,
                SourceCardId: card.Id));

        var recipe = new DealDamageEffectRecipe<CardPlayContext>(
            CombatantTargetSelectors.EventTarget,
            new FixedCombatValue<int>(7));

        Assert.Empty(recipe.BuildEffectRequests(new CardPlayContext(card.Build()), buildContext));
    }

    [Fact]
    public void GainBlockRecipeBuildsBlockRequest()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);

        var card = new CardDefinitionBuilder(
            new CardDefinitionId("test.block"),
            new PackageId("test"),
            displayNameKey: "card.test.block.name",
            descriptionKey: "card.test.block.description");

        var buildContext = new TriggeredEffectActionBuildContext(
            new CombatantTargetSelectionContext(
                Combat: combat,
                Source: hero),
            new TriggeredEffectActionSource(
                SourceCombatantId: hero.Id,
                SourceCardId: card.Id));

        var recipe = new GainBlockEffectRecipe<CardPlayContext>(
            CombatantTargetSelectors.Source,
            new FixedCombatValue<int>(4));

        var requests = recipe.BuildEffectRequests(new CardPlayContext(card.Build()), buildContext);

        var request = Assert.IsType<GainBlockEffectRequest>(Assert.Single(requests));

        Assert.Equal(hero.Id, request.TargetCombatantId);
        Assert.Equal(4, request.Amount);
        Assert.Equal(hero.Id, request.SourceCombatantId);
        Assert.Equal(card.Id, request.SourceCardId);
    }
}
