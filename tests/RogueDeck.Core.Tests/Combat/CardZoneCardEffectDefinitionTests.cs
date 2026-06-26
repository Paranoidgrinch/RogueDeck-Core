using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class CardZoneCardEffectDefinitionTests
{
    private static readonly CombatantId HeroId = new("hero_001");

    [Fact]
    public void DrawCardsRecipeBuildsDrawCardsRequest()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);

        var card = new CardDefinitionBuilder(
            new CardDefinitionId("test.draw"),
            new PackageId("test"),
            displayNameKey: "card.test.draw.name",
            descriptionKey: "card.test.draw.description");

        var buildContext = new TriggeredEffectActionBuildContext(
            new CombatantTargetSelectionContext(Combat: combat, Source: hero),
            new TriggeredEffectActionSource(SourceCombatantId: hero.Id, SourceCardId: card.Id));

        var recipe = new DrawCardsCardEffectRecipe(CombatantTargetSelectors.Source, 2);

        var requests = recipe.BuildEffectRequests(new CardPlayContext(card.Build()), buildContext);

        var request = Assert.IsType<DrawCardsEffectRequest>(Assert.Single(requests));

        Assert.Equal(HeroId, request.CombatantId);
        Assert.Equal(2, request.Count);
    }

    [Fact]
    public void DiscardHandRecipeBuildsDiscardHandRequest()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);

        var card = new CardDefinitionBuilder(
            new CardDefinitionId("test.discard_hand"),
            new PackageId("test"),
            displayNameKey: "card.test.discard_hand.name",
            descriptionKey: "card.test.discard_hand.description");

        var buildContext = new TriggeredEffectActionBuildContext(
            new CombatantTargetSelectionContext(Combat: combat, Source: hero),
            new TriggeredEffectActionSource(SourceCombatantId: hero.Id, SourceCardId: card.Id));

        var recipe = new DiscardHandCardEffectRecipe(CombatantTargetSelectors.Source);

        var requests = recipe.BuildEffectRequests(new CardPlayContext(card.Build()), buildContext);

        var request = Assert.IsType<DiscardHandEffectRequest>(Assert.Single(requests));

        Assert.Equal(HeroId, request.CombatantId);
    }

    [Fact]
    public void PlayingCardWithDrawCardsRecipeDrawsCards()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var drawCardId = new CardDefinitionId("test.draw_card");
        var drawCardDefinition = new CardDefinitionBuilder(
            drawCardId,
            new PackageId("test"),
            displayNameKey: "card.test.draw_card.name",
            descriptionKey: "card.test.draw_card.description");

        drawCardDefinition.Effects.Add(new DrawCardsCardEffectRecipe(
            CombatantTargetSelectors.Source, 2));

        builder.RegisterCard(drawCardDefinition);
        var registry = builder.Build();

        var playedCard = AddCardToZone(combat, HeroId, drawCardId, CardZone.Hand);
        var firstDrawnCard = AddCardToZone(combat, HeroId, StandardCombatIds.StrikeCard, CardZone.DrawPile);
        var secondDrawnCard = AddCardToZone(combat, HeroId, StandardCombatIds.DefendCard, CardZone.DrawPile);

        new CombatCardPlayProcessor().PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: playedCard.Id,
                SourceCombatantId: HeroId));

        var zones = combat.GetCardZones(HeroId);

        Assert.Empty(zones.DrawPile);

        Assert.Equal(2, zones.Hand.Count);
        Assert.Contains(zones.Hand, card => card.Id == firstDrawnCard.Id);
        Assert.Contains(zones.Hand, card => card.Id == secondDrawnCard.Id);

        Assert.Same(playedCard, Assert.Single(zones.DiscardPile));
        Assert.Equal(CardZone.DiscardPile, playedCard.Zone);

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.CardsDrawn);

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.CardMovedToZone);
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
