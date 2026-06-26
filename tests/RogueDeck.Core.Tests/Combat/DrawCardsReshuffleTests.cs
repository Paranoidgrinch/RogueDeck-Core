using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class DrawCardsReshuffleTests
{
    private static readonly CombatantId HeroId = new("hero_001");

    [Fact]
    public void CombatRandomCreatesStableShuffleIndexes()
    {
        var indexes = CombatRandom.CreateShuffledIndexes(
            count: 3,
            randomSeed: 12345,
            randomStep: 0);

        Assert.Equal(new[] { 1, 2, 0 }, indexes);
    }

    [Fact]
    public void DrawCardsEffectShufflesDiscardPileIntoDrawPileWhenDrawPileIsEmpty()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var shuffleEventHandler = new CaptureDiscardPileShuffledIntoDrawPileEventHandler();
        var drawnEventHandler = new CaptureCardsDrawnEventHandler();

        builder.RegisterCombatEventHandler(shuffleEventHandler);
        builder.RegisterCombatEventHandler(drawnEventHandler);
        var registry = builder.Build();

        var firstDiscard = AddCardToZone(combat, HeroId, CardZone.DiscardPile);
        var secondDiscard = AddCardToZone(combat, HeroId, CardZone.DiscardPile);
        var thirdDiscard = AddCardToZone(combat, HeroId, CardZone.DiscardPile);
        var banished = AddCardToZone(combat, HeroId, CardZone.BanishedPile);
        var exhausted = AddCardToZone(combat, HeroId, CardZone.ExhaustPile);

        combat.EnqueueEffect(new DrawCardsEffectRequest(HeroId, Count: 2));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var zones = combat.GetCardZones(HeroId);

        Assert.Equal(1, combat.RandomStep);

        Assert.Empty(zones.DiscardPile);

        Assert.Equal(2, zones.Hand.Count);
        Assert.Equal(secondDiscard.Id, zones.Hand[0].Id);
        Assert.Equal(thirdDiscard.Id, zones.Hand[1].Id);

        Assert.Single(zones.DrawPile);
        Assert.Equal(firstDiscard.Id, zones.DrawPile[0].Id);

        Assert.Same(banished, Assert.Single(zones.BanishedPile));
        Assert.Equal(CardZone.BanishedPile, banished.Zone);

        Assert.Same(exhausted, Assert.Single(zones.ExhaustPile));
        Assert.Equal(CardZone.ExhaustPile, exhausted.Zone);

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.DiscardPileShuffledIntoDrawPile);

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.CardsDrawn);

        var shuffleEvent = Assert.Single(shuffleEventHandler.HandledEvents);
        Assert.Equal(HeroId, shuffleEvent.CombatantId);
        Assert.Equal(
            new[] { secondDiscard.Id, thirdDiscard.Id, firstDiscard.Id },
            shuffleEvent.CardInstanceIds);

        var drawnEvent = Assert.Single(drawnEventHandler.HandledEvents);
        Assert.Equal(HeroId, drawnEvent.CombatantId);
        Assert.Equal(
            new[] { secondDiscard.Id, thirdDiscard.Id },
            drawnEvent.CardInstanceIds);
    }

    [Fact]
    public void DrawCardsEffectDoesNotShuffleWhenDrawPileHasEnoughCards()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var shuffleEventHandler = new CaptureDiscardPileShuffledIntoDrawPileEventHandler();
        builder.RegisterCombatEventHandler(shuffleEventHandler);
        var registry = builder.Build();

        var firstDraw = AddCardToZone(combat, HeroId, CardZone.DrawPile);
        var secondDraw = AddCardToZone(combat, HeroId, CardZone.DrawPile);
        var discard = AddCardToZone(combat, HeroId, CardZone.DiscardPile);

        combat.EnqueueEffect(new DrawCardsEffectRequest(HeroId, Count: 2));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var zones = combat.GetCardZones(HeroId);

        Assert.Equal(0, combat.RandomStep);

        Assert.Empty(zones.DrawPile);
        Assert.Equal(2, zones.Hand.Count);
        Assert.Equal(firstDraw.Id, zones.Hand[0].Id);
        Assert.Equal(secondDraw.Id, zones.Hand[1].Id);

        Assert.Same(discard, Assert.Single(zones.DiscardPile));
        Assert.Empty(shuffleEventHandler.HandledEvents);
    }

    [Fact]
    public void DrawCardsEffectDrawsAvailableCardsOnlyWhenDrawAndDiscardPileAreEmpty()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var onlyCard = AddCardToZone(combat, HeroId, CardZone.DrawPile);
        var banished = AddCardToZone(combat, HeroId, CardZone.BanishedPile);

        combat.EnqueueEffect(new DrawCardsEffectRequest(HeroId, Count: 5));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var zones = combat.GetCardZones(HeroId);

        Assert.Empty(zones.DrawPile);
        Assert.Empty(zones.DiscardPile);
        Assert.Same(onlyCard, Assert.Single(zones.Hand));
        Assert.Same(banished, Assert.Single(zones.BanishedPile));
        Assert.Equal(0, combat.RandomStep);
    }

    [Fact]
    public void StandardCombatPackageRegistersDrawAndDiscardEffectHandlers()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        Assert.IsType<DrawCardsEffectHandler>(
            registry.GetEffectRequestHandler(typeof(DrawCardsEffectRequest)));

        Assert.IsType<DiscardHandEffectHandler>(
            registry.GetEffectRequestHandler(typeof(DiscardHandEffectRequest)));
    }

    private static CardInstance AddCardToZone(
        CombatState combat,
        CombatantId ownerId,
        CardZone zone)
    {
        var card = new CardInstance(
            combat.CreateNextCardInstanceId(),
            StandardCombatIds.StrikeCard,
            ownerId,
            zone);

        combat.GetCardZones(ownerId).AddCard(card);

        return card;
    }

    private sealed class CaptureDiscardPileShuffledIntoDrawPileEventHandler
        : CombatEventHandler<DiscardPileShuffledIntoDrawPileCombatEvent>
    {
        public List<DiscardPileShuffledIntoDrawPileCombatEvent> HandledEvents { get; } = new();

        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            DiscardPileShuffledIntoDrawPileCombatEvent combatEvent)
        {
            HandledEvents.Add(combatEvent);
        }
    }

    private sealed class CaptureCardsDrawnEventHandler
        : CombatEventHandler<CardsDrawnCombatEvent>
    {
        public List<CardsDrawnCombatEvent> HandledEvents { get; } = new();

        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            CardsDrawnCombatEvent combatEvent)
        {
            HandledEvents.Add(combatEvent);
        }
    }
}
