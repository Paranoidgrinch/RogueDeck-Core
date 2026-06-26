using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class MoveAllCardsFromZoneEffectTests
{
    private static readonly CombatantId HeroId = new("hero_001");

    [Fact]
    public void MoveAllCardsFromZoneEffectMovesAllCardsFromBanishedPileToDiscardPileAndEmitsEvent()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var eventHandler = new CaptureCardsMovedBetweenZonesEventHandler();
        builder.RegisterCombatEventHandler(eventHandler);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var firstBanished = AddCardToZone(combat, HeroId, CardZone.BanishedPile);
        var secondBanished = AddCardToZone(combat, HeroId, CardZone.BanishedPile);
        var exhausted = AddCardToZone(combat, HeroId, CardZone.ExhaustPile);
        var handCard = AddCardToZone(combat, HeroId, CardZone.Hand);

        combat.EnqueueEffect(new MoveAllCardsFromZoneEffectRequest(
            CombatantId: HeroId,
            FromZone: CardZone.BanishedPile,
            ToZone: CardZone.DiscardPile));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var zones = combat.GetCardZones(HeroId);

        Assert.Empty(zones.BanishedPile);

        Assert.Equal(2, zones.DiscardPile.Count);
        Assert.Equal(firstBanished.Id, zones.DiscardPile[0].Id);
        Assert.Equal(secondBanished.Id, zones.DiscardPile[1].Id);
        Assert.All(zones.DiscardPile, card => Assert.Equal(CardZone.DiscardPile, card.Zone));

        Assert.Same(exhausted, Assert.Single(zones.ExhaustPile));
        Assert.Equal(CardZone.ExhaustPile, exhausted.Zone);

        Assert.Same(handCard, Assert.Single(zones.Hand));
        Assert.Equal(CardZone.Hand, handCard.Zone);

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.CardsMovedBetweenZones);

        var handledEvent = Assert.Single(eventHandler.HandledEvents);

        Assert.Equal(HeroId, handledEvent.CombatantId);
        Assert.Equal(CardZone.BanishedPile, handledEvent.FromZone);
        Assert.Equal(CardZone.DiscardPile, handledEvent.ToZone);
        Assert.Equal(
            new[] { firstBanished.Id, secondBanished.Id },
            handledEvent.CardInstanceIds);
    }

    [Fact]
    public void MoveAllCardsFromZoneEffectDoesNothingWhenSourceZoneIsEmpty()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var eventHandler = new CaptureCardsMovedBetweenZonesEventHandler();
        builder.RegisterCombatEventHandler(eventHandler);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        combat.EnqueueEffect(new MoveAllCardsFromZoneEffectRequest(
            CombatantId: HeroId,
            FromZone: CardZone.BanishedPile,
            ToZone: CardZone.DiscardPile));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Empty(combat.GetCardZones(HeroId).AllCards);
        Assert.Empty(eventHandler.HandledEvents);
        Assert.DoesNotContain(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.CardsMovedBetweenZones);
    }

    [Fact]
    public void MoveAllCardsFromZoneEffectDoesNothingWhenSourceAndTargetZoneAreSame()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var eventHandler = new CaptureCardsMovedBetweenZonesEventHandler();
        builder.RegisterCombatEventHandler(eventHandler);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var banished = AddCardToZone(combat, HeroId, CardZone.BanishedPile);

        combat.EnqueueEffect(new MoveAllCardsFromZoneEffectRequest(
            CombatantId: HeroId,
            FromZone: CardZone.BanishedPile,
            ToZone: CardZone.BanishedPile));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Same(banished, Assert.Single(combat.GetCardZones(HeroId).BanishedPile));
        Assert.Empty(eventHandler.HandledEvents);
    }

    [Fact]
    public void MoveAllCardsFromZoneRecipeBuildsMoveRequest()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);

        var sourceCard = new CardDefinitionBuilder(
            new CardDefinitionId("test.recover"),
            new PackageId("test"),
            displayNameKey: "card.test.recover.name",
            descriptionKey: "card.test.recover.description");

        var buildContext = new TriggeredEffectActionBuildContext(
            new CombatantTargetSelectionContext(Combat: combat, Source: hero),
            new TriggeredEffectActionSource(
                SourceCombatantId: hero.Id,
                SourceCardId: sourceCard.Id));

        var recipe = new MoveAllCardsFromZoneCardEffectRecipe(
            CombatantTargetSelectors.Source,
            fromZone: CardZone.BanishedPile,
            toZone: CardZone.DiscardPile);

        var requests = recipe.BuildEffectRequests(new CardPlayContext(sourceCard.Build()), buildContext);

        var request = Assert.IsType<MoveAllCardsFromZoneEffectRequest>(Assert.Single(requests));

        Assert.Equal(HeroId, request.CombatantId);
        Assert.Equal(CardZone.BanishedPile, request.FromZone);
        Assert.Equal(CardZone.DiscardPile, request.ToZone);
    }

    [Fact]
    public void PlayingRecoveryCardCanReturnBanishedCardsThroughRegisteredHandler()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var recoveryCardId = new CardDefinitionId("test.recover_banished");
        var recoveryCard = new CardDefinitionBuilder(
            recoveryCardId,
            new PackageId("test"),
            displayNameKey: "card.test.recover_banished.name",
            descriptionKey: "card.test.recover_banished.description");

        recoveryCard.Effects.Add(new MoveAllCardsFromZoneCardEffectRecipe(
            CombatantTargetSelectors.Source,
            fromZone: CardZone.BanishedPile,
            toZone: CardZone.DiscardPile));

        builder.RegisterCard(recoveryCard);
        var registry = builder.Build();

        var playedCard = AddCardToZone(combat, HeroId, recoveryCardId, CardZone.Hand);
        var firstBanished = AddCardToZone(combat, HeroId, StandardCombatIds.StrikeCard, CardZone.BanishedPile);
        var secondBanished = AddCardToZone(combat, HeroId, StandardCombatIds.DefendCard, CardZone.BanishedPile);

        new CombatCardPlayProcessor().PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: playedCard.Id,
                SourceCombatantId: HeroId));

        var zones = combat.GetCardZones(HeroId);

        Assert.Empty(zones.BanishedPile);
        Assert.Empty(zones.Hand);

        Assert.Equal(3, zones.DiscardPile.Count);
        Assert.Equal(firstBanished.Id, zones.DiscardPile[0].Id);
        Assert.Equal(secondBanished.Id, zones.DiscardPile[1].Id);
        Assert.Equal(playedCard.Id, zones.DiscardPile[2].Id);

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.CardsMovedBetweenZones);

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.CardMovedToZone);
    }

    [Fact]
    public void StandardCombatPackageRegistersMoveAllCardsFromZoneEffectHandler()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        Assert.IsType<MoveAllCardsFromZoneEffectHandler>(
            registry.GetEffectRequestHandler(typeof(MoveAllCardsFromZoneEffectRequest)));
    }

    private static CardInstance AddCardToZone(
        CombatState combat,
        CombatantId ownerId,
        CardZone zone)
    {
        return AddCardToZone(
            combat,
            ownerId,
            StandardCombatIds.StrikeCard,
            zone);
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

    private sealed class CaptureCardsMovedBetweenZonesEventHandler
        : CombatEventHandler<CardsMovedBetweenZonesCombatEvent>
    {
        public List<CardsMovedBetweenZonesCombatEvent> HandledEvents { get; } = new();

        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            CardsMovedBetweenZonesCombatEvent combatEvent)
        {
            HandledEvents.Add(combatEvent);
        }
    }
}
