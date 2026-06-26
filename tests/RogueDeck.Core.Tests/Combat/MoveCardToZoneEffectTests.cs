using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class MoveCardToZoneEffectTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    [Fact]
    public void MoveCardToZoneEffectMovesOwnedCardToRequestedZoneAndEmitsEvent()
    {
        CaptureCardMovedToZoneEventHandler.HandledEvents.Clear();

        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var card = AddCardToZone(combat, HeroId, CardZone.Hand);

        builder.RegisterCombatEventHandler(new CaptureCardMovedToZoneEventHandler());
        var registry = builder.Build();

        combat.EnqueueEffect(new MoveCardToZoneEffectRequest(
            HeroId,
            card.Id,
            CardZone.BanishedPile));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var zones = combat.GetCardZones(HeroId);

        Assert.Empty(zones.Hand);
        Assert.Same(card, Assert.Single(zones.BanishedPile));
        Assert.Equal(CardZone.BanishedPile, card.Zone);

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.CardMovedToZone);

        var handledEvent = Assert.Single(CaptureCardMovedToZoneEventHandler.HandledEvents);

        Assert.Equal(HeroId, handledEvent.CombatantId);
        Assert.Equal(card.Id, handledEvent.CardInstanceId);
        Assert.Equal(CardZone.Hand, handledEvent.FromZone);
        Assert.Equal(CardZone.BanishedPile, handledEvent.ToZone);

        CaptureCardMovedToZoneEventHandler.HandledEvents.Clear();
    }

    [Fact]
    public void MoveCardToZoneEffectRejectsCardOwnedByAnotherCombatant()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var card = AddCardToZone(combat, HeroId, CardZone.Hand);

        combat.EnqueueEffect(new MoveCardToZoneEffectRequest(
            GoblinId,
            card.Id,
            CardZone.BanishedPile));

        Assert.Throws<InvalidOperationException>(() =>
            new CombatEffectQueueProcessor().ResolvePendingEffects(combat, registry));

        Assert.Same(card, Assert.Single(combat.GetCardZones(HeroId).Hand));
        Assert.Empty(combat.GetCardZones(GoblinId).BanishedPile);
    }

    [Fact]
    public void MoveCardToZoneEffectSupportsMovingCardOutOfBanishedPile()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var card = AddCardToZone(combat, HeroId, CardZone.BanishedPile);

        combat.EnqueueEffect(new MoveCardToZoneEffectRequest(
            HeroId,
            card.Id,
            CardZone.Hand));

        new CombatEffectQueueProcessor().ResolvePendingEffects(combat, registry);

        var zones = combat.GetCardZones(HeroId);

        Assert.Empty(zones.BanishedPile);
        Assert.Same(card, Assert.Single(zones.Hand));
        Assert.Equal(CardZone.Hand, card.Zone);
    }

    [Fact]
    public void StandardCombatPackageRegistersMoveCardToZoneEffectHandler()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        var handler = registry.GetEffectRequestHandler(typeof(MoveCardToZoneEffectRequest));

        Assert.IsType<MoveCardToZoneEffectHandler>(handler);
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

    private sealed class CaptureCardMovedToZoneEventHandler
        : CombatEventHandler<CardMovedToZoneCombatEvent>
    {
        public static List<CardMovedToZoneCombatEvent> HandledEvents { get; } = new();

        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            CardMovedToZoneCombatEvent combatEvent)
        {
            HandledEvents.Add(combatEvent);
        }
    }
}
