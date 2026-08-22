using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class CardRetainLifecycleTests
{
    private static readonly CombatantId HeroId = new("hero_001");

    [Fact]
    public void StandardCardDefinitionsAreNotRetainedByDefault()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        Assert.False(registry.GetCard(StandardCombatIds.StrikeCard).RetainInHandOnTurnEnd);
        Assert.False(registry.GetCard(StandardCombatIds.DefendCard).RetainInHandOnTurnEnd);
        Assert.Equal(CardZone.DiscardPile, registry.GetCard(StandardCombatIds.StrikeCard).TurnEndHandDestinationZone);
        Assert.Equal(CardZone.DiscardPile, registry.GetCard(StandardCombatIds.DefendCard).TurnEndHandDestinationZone);
    }

    [Fact]
    public void TurnEndDiscardKeepsRetainedCardsInHand()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var retainedCardId = new CardDefinitionId("test.retained");
        var retainedCardDefinition = CreateRetainedCardDefinition(retainedCardId);

        builder.RegisterCard(retainedCardDefinition);
        var registry = builder.Build();

        var retainedCard = AddCardToZone(
            combat,
            HeroId,
            retainedCardId,
            CardZone.Hand);

        var normalCard = AddCardToZone(
            combat,
            HeroId,
            StandardCombatIds.StrikeCard,
            CardZone.Hand);

        var processor = new CombatTurnProcessor();

        processor.StartCurrentTurn(combat, registry);
        processor.EndCurrentTurn(combat, registry);

        var zones = combat.GetCardZones(HeroId);

        Assert.Same(retainedCard, Assert.Single(zones.Hand));
        Assert.Equal(CardZone.Hand, retainedCard.Zone);

        Assert.Same(normalCard, Assert.Single(zones.DiscardPile));
        Assert.Equal(CardZone.DiscardPile, normalCard.Zone);

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.HandDiscarded);
    }

    [Fact]
    public void TurnEndDiscardDoesNothingWhenOnlyRetainedCardsAreInHand()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var retainedCardId = new CardDefinitionId("test.retained");
        var retainedCardDefinition = CreateRetainedCardDefinition(retainedCardId);

        builder.RegisterCard(retainedCardDefinition);
        var registry = builder.Build();

        var retainedCard = AddCardToZone(
            combat,
            HeroId,
            retainedCardId,
            CardZone.Hand);

        var processor = new CombatTurnProcessor();

        processor.StartCurrentTurn(combat, registry);
        processor.EndCurrentTurn(combat, registry);

        var zones = combat.GetCardZones(HeroId);

        Assert.Same(retainedCard, Assert.Single(zones.Hand));
        Assert.Empty(zones.DiscardPile);

        Assert.DoesNotContain(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.HandDiscarded);
    }

    [Fact]
    public void ExplicitDiscardHandEffectDiscardsRetainedCardsByDefault()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var retainedCardId = new CardDefinitionId("test.retained");
        var retainedCardDefinition = CreateRetainedCardDefinition(retainedCardId);

        builder.RegisterCard(retainedCardDefinition);
        var registry = builder.Build();

        var retainedCard = AddCardToZone(
            combat,
            HeroId,
            retainedCardId,
            CardZone.Hand);

        var normalCard = AddCardToZone(
            combat,
            HeroId,
            StandardCombatIds.StrikeCard,
            CardZone.Hand);

        combat.EnqueueEffect(new DiscardHandEffectRequest(HeroId));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var zones = combat.GetCardZones(HeroId);

        Assert.Empty(zones.Hand);

        Assert.Equal(2, zones.DiscardPile.Count);
        Assert.Contains(zones.DiscardPile, card => card.Id == retainedCard.Id);
        Assert.Contains(zones.DiscardPile, card => card.Id == normalCard.Id);
    }

    [Fact]
    public void DiscardHandEffectCanKeepRetainedCardsWhenRequested()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var retainedCardId = new CardDefinitionId("test.retained");
        var retainedCardDefinition = CreateRetainedCardDefinition(retainedCardId);

        builder.RegisterCard(retainedCardDefinition);
        var registry = builder.Build();

        var retainedCard = AddCardToZone(
            combat,
            HeroId,
            retainedCardId,
            CardZone.Hand);

        var normalCard = AddCardToZone(
            combat,
            HeroId,
            StandardCombatIds.StrikeCard,
            CardZone.Hand);

        combat.EnqueueEffect(new DiscardHandEffectRequest(
            HeroId,
            IncludeRetainedCards: false));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var zones = combat.GetCardZones(HeroId);

        Assert.Same(retainedCard, Assert.Single(zones.Hand));
        Assert.Same(normalCard, Assert.Single(zones.DiscardPile));
    }

    [Fact]
    public void DiscardHandRecipeCanChooseWhetherRetainedCardsAreIncluded()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);

        var sourceCard = CreateCardDefinition(new CardDefinitionId("test.discard_source"));
        var buildContext = new TriggeredEffectActionBuildContext(
            new CombatantTargetSelectionContext(Combat: combat, Source: hero),
            new TriggeredEffectActionSource(
                SourceCombatantId: hero.Id,
                SourceCardId: sourceCard.Id));

        var recipe = new DiscardHandCardEffectRecipe(
            CombatantTargetSelectors.Source,
            includeRetainedCards: false);

        var requests = recipe.BuildEffectRequests(new CardPlayContext(sourceCard.Build()), buildContext);

        var request = Assert.IsType<DiscardHandEffectRequest>(Assert.Single(requests));

        Assert.Equal(HeroId, request.CombatantId);
        Assert.False(request.IncludeRetainedCards);
    }

    private static CardDefinitionBuilder CreateCardDefinition(CardDefinitionId id)
    {
        return new CardDefinitionBuilder(
            id,
            new PackageId("test"),
            displayNameKey: $"card.{id}.name",
            descriptionKey: $"card.{id}.description");
    }

    private static CardDefinitionBuilder CreateRetainedCardDefinition(CardDefinitionId id)
    {
        var definition = CreateCardDefinition(id);
        definition.RetainInHandOnTurnEnd = true;

        return definition;
    }

    // The per-instance counterpart of the definition flag: ONE copy of an ordinary card is held back, while
    // its identical twin discards. Neither of the older tools can say this — the flag prices every copy alike
    // and the retain-hand status tag holds the whole hand.
    [Fact]
    public void TurnEndDiscardKeepsAMarkedCopyInHandWhileItsTwinDiscards()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var markedCard = AddCardToZone(combat, HeroId, StandardCombatIds.StrikeCard, CardZone.Hand);
        var twin = AddCardToZone(combat, HeroId, StandardCombatIds.StrikeCard, CardZone.Hand);

        markedCard.AddMark(StandardCombatIds.RetainedCardMark);

        var processor = new CombatTurnProcessor();
        processor.StartCurrentTurn(combat, registry);
        processor.EndCurrentTurn(combat, registry);

        var zones = combat.GetCardZones(HeroId);

        Assert.Same(markedCard, Assert.Single(zones.Hand));
        Assert.Equal(CardZone.Hand, markedCard.Zone);
        Assert.Same(twin, Assert.Single(zones.DiscardPile));
        Assert.Equal(CardZone.DiscardPile, twin.Zone);
    }

    // Taking the mark off puts the copy back under the ordinary rule, so the retention really is one-shot
    // when content wants it to be.
    [Fact]
    public void AnUnmarkedCopyDiscardsAgain()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var card = AddCardToZone(combat, HeroId, StandardCombatIds.StrikeCard, CardZone.Hand);
        card.AddMark(StandardCombatIds.RetainedCardMark);
        card.RemoveMark(StandardCombatIds.RetainedCardMark);

        var processor = new CombatTurnProcessor();
        processor.StartCurrentTurn(combat, registry);
        processor.EndCurrentTurn(combat, registry);

        Assert.Empty(combat.GetCardZones(HeroId).Hand);
        Assert.Equal(CardZone.DiscardPile, card.Zone);
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


