using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class CardStatusEffectDefinitionTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    [Fact]
    public void ApplyStatusRecipeBuildsApplyStatusRequest()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);

        var sourceCard = new CardDefinitionBuilder(
            new CardDefinitionId("test.poison_sting"),
            new PackageId("test"),
            displayNameKey: "card.test.poison_sting.name",
            descriptionKey: "card.test.poison_sting.description");

        var buildContext = new TriggeredEffectActionBuildContext(
            new CombatantTargetSelectionContext(
                Combat: combat,
                Source: hero,
                EventTargetId: GoblinId),
            new TriggeredEffectActionSource(
                SourceCombatantId: hero.Id,
                SourceCardId: sourceCard.Id));

        var recipe = new ApplyStatusCardEffectRecipe(
            CombatantTargetSelectors.EventTarget,
            statusDefinitionId: StandardCombatIds.PoisonStatus,
            stacks: 4,
            durationTurns: 0,
            charges: 0);

        var requests = recipe.BuildEffectRequests(new CardPlayContext(sourceCard.Build()), buildContext);

        var request = Assert.IsType<ApplyStatusEffectRequest>(Assert.Single(requests));

        Assert.Equal(GoblinId, request.TargetCombatantId);
        Assert.Equal(StandardCombatIds.PoisonStatus, request.StatusDefinitionId);
        Assert.Equal(HeroId, request.SourceCombatantId);
        Assert.Equal(sourceCard.Id, request.SourceCardId);
        Assert.Equal(4, request.Stacks);
        Assert.Equal(0, request.DurationTurns);
        Assert.Equal(0, request.Charges);
    }

    [Fact]
    public void PlayingCardWithApplyStatusDefinitionAppliesStatusThroughRegisteredHandler()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var poisonCardId = new CardDefinitionId("test.poison_sting");
        var poisonCard = new CardDefinitionBuilder(
            poisonCardId,
            new PackageId("test"),
            displayNameKey: "card.test.poison_sting.name",
            descriptionKey: "card.test.poison_sting.description");

        poisonCard.Effects.Add(new ApplyStatusCardEffectRecipe(
            CombatantTargetSelectors.EventTarget,
            statusDefinitionId: StandardCombatIds.PoisonStatus,
            stacks: 5,
            durationTurns: 0,
            charges: 0));

        builder.RegisterCard(poisonCard);
        var registry = builder.Build();

        var playedCard = AddCardToZone(
            combat,
            HeroId,
            poisonCardId,
            CardZone.Hand);

        new CombatCardPlayProcessor().PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: playedCard.Id,
                SourceCombatantId: HeroId,
                TargetCombatantId: GoblinId));

        var goblin = combat.GetCombatant(GoblinId);
        var status = Assert.Single(goblin.Statuses);

        Assert.Equal(StandardCombatIds.PoisonStatus, status.DefinitionId);
        Assert.Equal(5, status.Stacks);
        Assert.Equal(HeroId, status.SourceCombatantId);
        Assert.Equal(poisonCardId, status.SourceCardId);

        Assert.Empty(combat.GetCardZones(HeroId).Hand);
        Assert.Same(playedCard, Assert.Single(combat.GetCardZones(HeroId).DiscardPile));

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.StatusApplied);

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.CardMovedToZone);
    }

    [Fact]
    public void PlayingCardWithApplyStatusDefinitionCanApplyBuffToSelf()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var strengthCardId = new CardDefinitionId("test.focus");
        var strengthCard = new CardDefinitionBuilder(
            strengthCardId,
            new PackageId("test"),
            displayNameKey: "card.test.focus.name",
            descriptionKey: "card.test.focus.description");

        strengthCard.Effects.Add(new ApplyStatusCardEffectRecipe(
            CombatantTargetSelectors.Source,
            statusDefinitionId: StandardCombatIds.StrengthStatus,
            stacks: 2,
            durationTurns: 0,
            charges: 0));

        builder.RegisterCard(strengthCard);
        var registry = builder.Build();

        var playedCard = AddCardToZone(
            combat,
            HeroId,
            strengthCardId,
            CardZone.Hand);

        new CombatCardPlayProcessor().PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: playedCard.Id,
                SourceCombatantId: HeroId));

        var hero = combat.GetCombatant(HeroId);
        var status = Assert.Single(hero.Statuses);

        Assert.Equal(StandardCombatIds.StrengthStatus, status.DefinitionId);
        Assert.Equal(2, status.Stacks);
        Assert.Equal(HeroId, status.SourceCombatantId);
        Assert.Equal(strengthCardId, status.SourceCardId);
    }

    [Fact]
    public void PlayingCardWithApplyStatusDefinitionMergesExistingStatus()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var poisonCardId = new CardDefinitionId("test.poison_sting");
        var poisonCard = new CardDefinitionBuilder(
            poisonCardId,
            new PackageId("test"),
            displayNameKey: "card.test.poison_sting.name",
            descriptionKey: "card.test.poison_sting.description");

        poisonCard.Effects.Add(new ApplyStatusCardEffectRecipe(
            CombatantTargetSelectors.EventTarget,
            statusDefinitionId: StandardCombatIds.PoisonStatus,
            stacks: 3,
            durationTurns: 0,
            charges: 0));

        builder.RegisterCard(poisonCard);
        var registry = builder.Build();

        var firstPlayedCard = AddCardToZone(
            combat,
            HeroId,
            poisonCardId,
            CardZone.Hand);

        new CombatCardPlayProcessor().PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: firstPlayedCard.Id,
                SourceCombatantId: HeroId,
                TargetCombatantId: GoblinId));

        var secondPlayedCard = AddCardToZone(
            combat,
            HeroId,
            poisonCardId,
            CardZone.Hand);

        new CombatCardPlayProcessor().PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: secondPlayedCard.Id,
                SourceCombatantId: HeroId,
                TargetCombatantId: GoblinId));

        var goblin = combat.GetCombatant(GoblinId);
        var status = Assert.Single(goblin.Statuses);

        Assert.Equal(StandardCombatIds.PoisonStatus, status.DefinitionId);
        Assert.Equal(6, status.Stacks);

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.StatusMerged);
    }

    private static CardInstance AddCardToZone(
        CombatState combat,
        CombatantId ownerId,
        CardDefinitionId definitionId,
        CardZone zone)
    {
        var card = new CardInstance(
            combat.CreateNextCardInstanceId(),
            definitionId,
            ownerId,
            zone);

        combat.GetCardZones(ownerId).AddCard(card);

        return card;
    }
}

