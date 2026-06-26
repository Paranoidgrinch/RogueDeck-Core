using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class CardCreationEffectTests
{
    private static readonly CombatantId HeroId = new("hero_001");

    [Fact]
    public void CreateCardInstanceEffectCreatesCardInRequestedZoneAndEmitsEvent()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var eventHandler = new CaptureCardInstanceCreatedEventHandler();
        builder.RegisterCombatEventHandler(eventHandler);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        combat.EnqueueEffect(new CreateCardInstanceEffectRequest(
            CombatantId: HeroId,
            CardDefinitionId: StandardCombatIds.StrikeCard,
            ToZone: CardZone.Hand,
            Count: 2));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var zones = combat.GetCardZones(HeroId);

        Assert.Equal(2, zones.Hand.Count);
        Assert.All(zones.Hand, card =>
        {
            Assert.Equal(StandardCombatIds.StrikeCard, card.DefinitionId);
            Assert.Equal(HeroId, card.OwnerId);
            Assert.Equal(CardZone.Hand, card.Zone);
        });

        Assert.Equal(3, combat.NextCardInstanceNumber);
        Assert.Equal(0, combat.PendingEffectCount);
        Assert.Equal(0, combat.PendingEventCount);

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.CardInstanceCreated);

        var handledEvent = Assert.Single(eventHandler.HandledEvents);

        Assert.Equal(HeroId, handledEvent.CombatantId);
        Assert.Equal(StandardCombatIds.StrikeCard, handledEvent.CardDefinitionId);
        Assert.Equal(CardZone.Hand, handledEvent.ToZone);
        Assert.Equal(zones.Hand.Select(card => card.Id).ToArray(), handledEvent.CardInstanceIds);
    }

    [Fact]
    public void CreateCardInstanceEffectCanCreateBanishedCards()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        combat.EnqueueEffect(new CreateCardInstanceEffectRequest(
            CombatantId: HeroId,
            CardDefinitionId: StandardCombatIds.DefendCard,
            ToZone: CardZone.BanishedPile,
            Count: 1));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var zones = combat.GetCardZones(HeroId);
        var card = Assert.Single(zones.BanishedPile);

        Assert.Equal(StandardCombatIds.DefendCard, card.DefinitionId);
        Assert.Equal(HeroId, card.OwnerId);
        Assert.Equal(CardZone.BanishedPile, card.Zone);

        Assert.Empty(zones.DrawPile);
        Assert.Empty(zones.Hand);
        Assert.Empty(zones.DiscardPile);
        Assert.Empty(zones.ExhaustPile);
    }

    [Fact]
    public void CreateCardInstanceEffectRejectsMissingCardDefinition()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        combat.EnqueueEffect(new CreateCardInstanceEffectRequest(
            CombatantId: HeroId,
            CardDefinitionId: new CardDefinitionId("missing.card"),
            ToZone: CardZone.Hand,
            Count: 1));

        Assert.Throws<InvalidOperationException>(() =>
            new CombatEffectQueueProcessor().ResolvePendingEffects(combat, registry));

        Assert.Empty(combat.GetCardZones(HeroId).AllCards);
    }

    [Fact]
    public void CreateCardInstanceEffectRejectsNegativeCount()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        combat.EnqueueEffect(new CreateCardInstanceEffectRequest(
            CombatantId: HeroId,
            CardDefinitionId: StandardCombatIds.StrikeCard,
            ToZone: CardZone.Hand,
            Count: -1));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CombatEffectQueueProcessor().ResolvePendingEffects(combat, registry));
    }

    [Fact]
    public void CreateCardInstanceRecipeBuildsCreateCardInstanceRequest()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);

        var sourceCard = new CardDefinitionBuilder(
            new CardDefinitionId("test.generator"),
            new PackageId("test"),
            displayNameKey: "card.test.generator.name",
            descriptionKey: "card.test.generator.description");

        var buildContext = new TriggeredEffectActionBuildContext(
            new CombatantTargetSelectionContext(Combat: combat, Source: hero),
            new TriggeredEffectActionSource(
                SourceCombatantId: hero.Id,
                SourceCardId: sourceCard.Id));

        var recipe = new CreateCardInstanceCardEffectRecipe(
            CombatantTargetSelectors.Source,
            cardDefinitionId: StandardCombatIds.StrikeCard,
            toZone: CardZone.DrawPile,
            count: 2);

        var requests = recipe.BuildEffectRequests(new CardPlayContext(sourceCard.Build()), buildContext);

        var request = Assert.IsType<CreateCardInstanceEffectRequest>(Assert.Single(requests));

        Assert.Equal(HeroId, request.CombatantId);
        Assert.Equal(StandardCombatIds.StrikeCard, request.CardDefinitionId);
        Assert.Equal(CardZone.DrawPile, request.ToZone);
        Assert.Equal(2, request.Count);
    }

    [Fact]
    public void PlayingCardWithCreateCardInstanceDefinitionCreatesCardThroughRegisteredHandler()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var generatorCardId = new CardDefinitionId("test.generator");
        var generatorCard = new CardDefinitionBuilder(
            generatorCardId,
            new PackageId("test"),
            displayNameKey: "card.test.generator.name",
            descriptionKey: "card.test.generator.description");

        generatorCard.Effects.Add(new CreateCardInstanceCardEffectRecipe(
            CombatantTargetSelectors.Source,
            cardDefinitionId: StandardCombatIds.StrikeCard,
            toZone: CardZone.Hand,
            count: 1));

        builder.RegisterCard(generatorCard);
        var registry = builder.Build();

        var playedCard = new CardInstance(
            combat.CreateNextCardInstanceId(),
            generatorCardId,
            HeroId,
            CardZone.Hand);

        combat.GetCardZones(HeroId).AddCard(playedCard);

        new CombatCardPlayProcessor().PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: playedCard.Id,
                SourceCombatantId: HeroId));

        var zones = combat.GetCardZones(HeroId);

        Assert.Single(zones.DiscardPile);
        Assert.Equal(playedCard.Id, zones.DiscardPile[0].Id);

        var createdCard = Assert.Single(zones.Hand);

        Assert.NotEqual(playedCard.Id, createdCard.Id);
        Assert.Equal(StandardCombatIds.StrikeCard, createdCard.DefinitionId);
        Assert.Equal(HeroId, createdCard.OwnerId);
        Assert.Equal(CardZone.Hand, createdCard.Zone);

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.CardInstanceCreated);
    }

    [Fact]
    public void StandardCombatPackageRegistersCardCreationEffectHandler()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        Assert.IsType<CreateCardInstanceEffectHandler>(
            registry.GetEffectRequestHandler(typeof(CreateCardInstanceEffectRequest)));
    }

    private sealed class CaptureCardInstanceCreatedEventHandler
        : CombatEventHandler<CardInstanceCreatedCombatEvent>
    {
        public List<CardInstanceCreatedCombatEvent> HandledEvents { get; } = new();

        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            CardInstanceCreatedCombatEvent combatEvent)
        {
            HandledEvents.Add(combatEvent);
        }
    }
}

