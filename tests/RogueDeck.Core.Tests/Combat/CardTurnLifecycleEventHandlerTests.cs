using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class CardTurnLifecycleEventHandlerTests
{
    private static readonly CombatantId HeroId = new("hero_001");

    [Fact]
    public void StandardCombatPackageDrawsCardsWhenTurnStarts()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var first = AddCardToZone(combat, HeroId, CardZone.DrawPile);
        var second = AddCardToZone(combat, HeroId, CardZone.DrawPile);
        var banished = AddCardToZone(combat, HeroId, CardZone.BanishedPile);
        var exhausted = AddCardToZone(combat, HeroId, CardZone.ExhaustPile);

        new CombatTurnProcessor().StartCurrentTurn(combat, registry);

        var zones = combat.GetCardZones(HeroId);

        Assert.Empty(zones.DrawPile);

        Assert.Equal(2, zones.Hand.Count);
        Assert.Equal(first.Id, zones.Hand[0].Id);
        Assert.Equal(second.Id, zones.Hand[1].Id);

        Assert.Same(banished, Assert.Single(zones.BanishedPile));
        Assert.Equal(CardZone.BanishedPile, banished.Zone);

        Assert.Same(exhausted, Assert.Single(zones.ExhaustPile));
        Assert.Equal(CardZone.ExhaustPile, exhausted.Zone);

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.CardsDrawn);
    }

    [Fact]
    public void StandardCombatPackageDiscardsHandWhenTurnEnds()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var first = AddCardToZone(combat, HeroId, CardZone.Hand);
        var second = AddCardToZone(combat, HeroId, CardZone.Hand);
        var banished = AddCardToZone(combat, HeroId, CardZone.BanishedPile);
        var exhausted = AddCardToZone(combat, HeroId, CardZone.ExhaustPile);

        var turnProcessor = new CombatTurnProcessor();

        turnProcessor.StartCurrentTurn(combat, registry);
        turnProcessor.EndCurrentTurn(combat, registry);

        var zones = combat.GetCardZones(HeroId);

        Assert.Empty(zones.Hand);

        Assert.Equal(2, zones.DiscardPile.Count);
        Assert.Contains(zones.DiscardPile, card => card.Id == first.Id);
        Assert.Contains(zones.DiscardPile, card => card.Id == second.Id);

        Assert.Same(banished, Assert.Single(zones.BanishedPile));
        Assert.Equal(CardZone.BanishedPile, banished.Zone);

        Assert.Same(exhausted, Assert.Single(zones.ExhaustPile));
        Assert.Equal(CardZone.ExhaustPile, exhausted.Zone);

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.HandDiscarded);
    }

    [Fact]
    public void DrawCardsOnTurnStartedHandlerCanBeConfiguredWithCustomDrawCount()
    {
        var builder = new CombatDefinitionRegistryBuilder();

        builder.RegisterEffectRequestHandler(new DrawCardsEffectHandler());
        builder.RegisterCombatEventHandler(new DrawCardsOnTurnStartedHandler(cardsToDraw: 1));
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var first = AddCardToZone(combat, HeroId, CardZone.DrawPile);
        var second = AddCardToZone(combat, HeroId, CardZone.DrawPile);

        new CombatTurnProcessor().StartCurrentTurn(combat, registry);

        var zones = combat.GetCardZones(HeroId);

        Assert.Single(zones.Hand);
        Assert.Equal(first.Id, zones.Hand[0].Id);

        Assert.Single(zones.DrawPile);
        Assert.Equal(second.Id, zones.DrawPile[0].Id);
    }

    [Fact]
    public void StandardCombatPackageRegistersCardTurnLifecycleHandlers()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        Assert.Contains(
            registry.GetCombatEventHandlers(typeof(TurnStartedCombatEvent)),
            handler => handler is DrawCardsOnTurnStartedHandler);

        Assert.Contains(
            registry.GetCombatEventHandlers(typeof(TurnEndedCombatEvent)),
            handler => handler is DiscardHandOnTurnEndedHandler);
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
}
