using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class CardInstancePlayTests
{
    [Fact]
    public void PlayCardInstanceFromHandPaysEnergyAppliesEffectsAndMovesCardToDiscard()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var heroId = new CombatantId("hero_001");
        var goblinId = new CombatantId("goblin_001");

        var hero = combat.GetCombatant(heroId);
        hero.AddResource(
            StandardCombatIds.EnergyResource,
            new ValuePoolState(current: 3, max: 3));

        var zones = combat.GetCardZones(heroId);
        var card = AddCardToHand(combat, heroId, StandardCombatIds.StrikeCard);

        var processor = new CombatCardPlayProcessor();

        processor.PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: card.Id,
                SourceCombatantId: heroId,
                TargetCombatantId: goblinId));

        var goblin = combat.GetCombatant(goblinId);
        var energy = hero.Resources[StandardCombatIds.EnergyResource];

        Assert.Equal(2, energy.Current);
        Assert.Equal(6, goblin.Health.Current);
        Assert.Empty(zones.Hand);
        Assert.Single(zones.DiscardPile);
        Assert.Equal(CardZone.DiscardPile, card.Zone);
        Assert.Equal(0, combat.PendingEffectCount);
        Assert.Equal(0, combat.PendingEventCount);
        Assert.Contains(combat.CombatLog, entry => entry.Type == StandardCombatLogTypes.CardPlayed);
        Assert.Contains(combat.CombatLog, entry => entry.Type == StandardCombatLogTypes.DamageDealt);
    }

    [Fact]
    public void PlayCardInstanceForDefendMovesCardToDiscardAndGrantsBlock()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var heroId = new CombatantId("hero_001");

        var hero = combat.GetCombatant(heroId);
        hero.AddResource(
            StandardCombatIds.EnergyResource,
            new ValuePoolState(current: 3, max: 3));

        var zones = combat.GetCardZones(heroId);
        var card = AddCardToHand(combat, heroId, StandardCombatIds.DefendCard);

        var processor = new CombatCardPlayProcessor();

        processor.PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: card.Id,
                SourceCombatantId: heroId));

        var block = hero.DefensivePools[StandardCombatIds.BlockDefensivePool];

        Assert.Equal(5, block.Current);
        Assert.Empty(zones.Hand);
        Assert.Single(zones.DiscardPile);
        Assert.Equal(CardZone.DiscardPile, card.Zone);
    }

    [Fact]
    public void PlayCardInstanceRejectsCardNotInHandWithoutPayingCost()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var heroId = new CombatantId("hero_001");
        var goblinId = new CombatantId("goblin_001");

        var hero = combat.GetCombatant(heroId);
        hero.AddResource(
            StandardCombatIds.EnergyResource,
            new ValuePoolState(current: 3, max: 3));

        var zones = combat.GetCardZones(heroId);
        var card = AddCardToDrawPile(combat, heroId, StandardCombatIds.StrikeCard);

        var processor = new CombatCardPlayProcessor();

        Assert.Throws<InvalidOperationException>(() =>
            processor.PlayCardInstance(
                combat,
                registry,
                new CardInstancePlayRequest(
                    CardInstanceId: card.Id,
                    SourceCombatantId: heroId,
                    TargetCombatantId: goblinId)));

        var energy = hero.Resources[StandardCombatIds.EnergyResource];
        var goblin = combat.GetCombatant(goblinId);

        Assert.Equal(3, energy.Current);
        Assert.Equal(12, goblin.Health.Current);
        Assert.Single(zones.DrawPile);
        Assert.Empty(zones.DiscardPile);
    }

    [Fact]
    public void PlayCardInstanceRejectsCardOwnedByAnotherCombatant()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var heroId = new CombatantId("hero_001");
        var goblinId = new CombatantId("goblin_001");

        var hero = combat.GetCombatant(heroId);
        hero.AddResource(
            StandardCombatIds.EnergyResource,
            new ValuePoolState(current: 3, max: 3));

        var goblin = combat.GetCombatant(goblinId);
        goblin.AddResource(
            StandardCombatIds.EnergyResource,
            new ValuePoolState(current: 3, max: 3));

        var card = AddCardToHand(combat, heroId, StandardCombatIds.StrikeCard);

        var processor = new CombatCardPlayProcessor();

        Assert.Throws<InvalidOperationException>(() =>
            processor.PlayCardInstance(
                combat,
                registry,
                new CardInstancePlayRequest(
                    CardInstanceId: card.Id,
                    SourceCombatantId: goblinId,
                    TargetCombatantId: heroId)));
    }

    [Fact]
    public void CardPlayedEventContainsCardInstanceIdWhenPlayingInstance()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();

        builder.RegisterCombatEventHandler(new CaptureCardPlayedEventHandler());
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var heroId = new CombatantId("hero_001");
        var goblinId = new CombatantId("goblin_001");

        var hero = combat.GetCombatant(heroId);
        hero.AddResource(
            StandardCombatIds.EnergyResource,
            new ValuePoolState(current: 3, max: 3));

        var card = AddCardToHand(combat, heroId, StandardCombatIds.StrikeCard);

        var processor = new CombatCardPlayProcessor();

        processor.PlayCardInstance(
            combat,
            registry,
            new CardInstancePlayRequest(
                CardInstanceId: card.Id,
                SourceCombatantId: heroId,
                TargetCombatantId: goblinId));

        var handledEvent = Assert.Single(CaptureCardPlayedEventHandler.HandledEvents);

        Assert.Equal(StandardCombatIds.StrikeCard, handledEvent.CardDefinitionId);
        Assert.Equal(card.Id, handledEvent.CardInstanceId);
        Assert.Equal(heroId, handledEvent.SourceCombatantId);
        Assert.Equal(goblinId, handledEvent.TargetCombatantId);

        CaptureCardPlayedEventHandler.HandledEvents.Clear();
    }
    [Fact]
    public void PlayCardInstanceMovesCardToConfiguredPlayedCardDestinationZone()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var heroId = new CombatantId("hero_001");
        var oneUseCardId = new CardDefinitionId("test.one_use");

        var oneUseCard = new CardDefinitionBuilder(
         oneUseCardId,
         new PackageId("test"),
         displayNameKey: "card.test.one_use.name",
         descriptionKey: "card.test.one_use.description")
        {
            PlayedCardDestinationZone = CardZone.BanishedPile
        };

        builder.RegisterCard(oneUseCard);
        var registry = builder.Build();

        var zones = combat.GetCardZones(heroId);
        var card = AddCardToHand(combat, heroId, oneUseCardId);

        var processor = new CombatCardPlayProcessor();

        processor.PlayCardInstance(
         combat,
         registry,
         new CardInstancePlayRequest(
          CardInstanceId: card.Id,
          SourceCombatantId: heroId));

        Assert.Empty(zones.Hand);
        Assert.Empty(zones.DiscardPile);
        Assert.Same(card, Assert.Single(zones.BanishedPile));
        Assert.Equal(CardZone.BanishedPile, card.Zone);
        Assert.Equal(0, combat.PendingEffectCount);
        Assert.Equal(0, combat.PendingEventCount);

        Assert.Contains(
         combat.CombatLog,
         entry => entry.Type == StandardCombatLogTypes.CardMovedToZone);
    }

    private static CardInstance AddCardToHand(
        CombatState combat,
        CombatantId ownerId,
        CardDefinitionId definitionId)
    {
        return AddCardToZone(
            combat,
            ownerId,
            definitionId,
            CardZone.Hand);
    }

    private static CardInstance AddCardToDrawPile(
        CombatState combat,
        CombatantId ownerId,
        CardDefinitionId definitionId)
    {
        return AddCardToZone(
            combat,
            ownerId,
            definitionId,
            CardZone.DrawPile);
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

    private sealed class CaptureCardPlayedEventHandler : CombatEventHandler<CardPlayedCombatEvent>
    {
        public static List<CardPlayedCombatEvent> HandledEvents { get; } = new();

        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            CardPlayedCombatEvent combatEvent)
        {
            HandledEvents.Add(combatEvent);
        }
    }
}

