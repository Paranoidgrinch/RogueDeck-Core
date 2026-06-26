using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class CardZoneEffectTests
{
    private static readonly CombatantId HeroId = new("hero_001");

    [Fact]
    public void DrawCardsEffectDrawsCardsFromDrawPileAndEmitsEvent()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var eventHandler = new CaptureCardsDrawnEventHandler();
        builder.RegisterCombatEventHandler(eventHandler);
        var registry = builder.Build();

        var first = AddCardToZone(combat, HeroId, CardZone.DrawPile);
        var second = AddCardToZone(combat, HeroId, CardZone.DrawPile);
        var banished = AddCardToZone(combat, HeroId, CardZone.BanishedPile);

        combat.EnqueueEffect(new DrawCardsEffectRequest(HeroId, Count: 3));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var zones = combat.GetCardZones(HeroId);

        Assert.Empty(zones.DrawPile);
        Assert.Equal(2, zones.Hand.Count);
        Assert.Contains(zones.Hand, card => card.Id == first.Id);
        Assert.Contains(zones.Hand, card => card.Id == second.Id);

        Assert.Same(banished, Assert.Single(zones.BanishedPile));
        Assert.Equal(CardZone.BanishedPile, banished.Zone);

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.CardsDrawn);

        var handledEvent = Assert.Single(eventHandler.HandledEvents);
        Assert.Equal(HeroId, handledEvent.CombatantId);
        Assert.Equal(new[] { first.Id, second.Id }, handledEvent.CardInstanceIds);
    }

    [Fact]
    public void DrawCardsEffectRejectsNegativeCount()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        combat.EnqueueEffect(new DrawCardsEffectRequest(HeroId, Count: -1));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CombatEffectQueueProcessor().ResolvePendingEffects(combat, registry));
    }

    [Fact]
    public void DiscardHandEffectDiscardsHandCardsOnlyAndEmitsEvent()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var eventHandler = new CaptureHandDiscardedEventHandler();
        builder.RegisterCombatEventHandler(eventHandler);
        var registry = builder.Build();

        var first = AddCardToZone(combat, HeroId, CardZone.Hand);
        var second = AddCardToZone(combat, HeroId, CardZone.Hand);
        var banished = AddCardToZone(combat, HeroId, CardZone.BanishedPile);

        combat.EnqueueEffect(new DiscardHandEffectRequest(HeroId));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var zones = combat.GetCardZones(HeroId);

        Assert.Empty(zones.Hand);
        Assert.Equal(2, zones.DiscardPile.Count);
        Assert.Contains(zones.DiscardPile, card => card.Id == first.Id);
        Assert.Contains(zones.DiscardPile, card => card.Id == second.Id);

        Assert.Same(banished, Assert.Single(zones.BanishedPile));
        Assert.Equal(CardZone.BanishedPile, banished.Zone);

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.HandDiscarded);

        var handledEvent = Assert.Single(eventHandler.HandledEvents);
        Assert.Equal(HeroId, handledEvent.CombatantId);
        Assert.Equal(new[] { first.Id, second.Id }, handledEvent.CardInstanceIds);
    }

    [Fact]
    public void StandardCombatPackageRegistersCardZoneEffectHandlers()
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

    private sealed class CaptureCardsDrawnEventHandler : CombatEventHandler<CardsDrawnCombatEvent>
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

    private sealed class CaptureHandDiscardedEventHandler : CombatEventHandler<HandDiscardedCombatEvent>
    {
        public List<HandDiscardedCombatEvent> HandledEvents { get; } = new();

        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            HandDiscardedCombatEvent combatEvent)
        {
            HandledEvents.Add(combatEvent);
        }
    }
}
