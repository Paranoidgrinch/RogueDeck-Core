using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class CardZoneTests
{
    private static readonly CombatantId OwnerId = new("hero");
    private static readonly CardDefinitionId StrikeCardId = new("standard.strike");

    [Fact]
    public void AddCardSupportsBanishedPile()
    {
        var zones = new CombatantCardZones();
        var card = CreateCard("card_001", CardZone.BanishedPile);

        zones.AddCard(card);

        Assert.Same(card, Assert.Single(zones.BanishedPile));
        Assert.Contains(card, zones.AllCards);
        Assert.Equal(CardZone.BanishedPile, zones.GetCard(card.Id).Zone);
    }

    [Fact]
    public void MoveCardToZoneCanMoveCardsIntoAndOutOfBanishedPile()
    {
        var zones = new CombatantCardZones();
        var card = CreateCard("card_001", CardZone.Hand);
        zones.AddCard(card);

        zones.MoveCardToZone(card.Id, CardZone.BanishedPile);

        Assert.Empty(zones.Hand);
        Assert.Same(card, Assert.Single(zones.BanishedPile));
        Assert.Equal(CardZone.BanishedPile, card.Zone);

        zones.MoveCardToZone(card.Id, CardZone.DrawPile);

        Assert.Empty(zones.BanishedPile);
        Assert.Same(card, Assert.Single(zones.DrawPile));
        Assert.Equal(CardZone.DrawPile, card.Zone);
    }

    [Fact]
    public void DrawCardsDoesNotDrawBanishedCards()
    {
        var zones = new CombatantCardZones();
        var drawableCard = CreateCard("card_drawable", CardZone.DrawPile);
        var banishedCard = CreateCard("card_banished", CardZone.BanishedPile);

        zones.AddCard(drawableCard);
        zones.AddCard(banishedCard);

        var drawnCards = zones.DrawCards(2);

        Assert.Single(drawnCards);
        Assert.Same(drawableCard, drawnCards[0]);
        Assert.Empty(zones.DrawPile);
        Assert.Same(drawableCard, Assert.Single(zones.Hand));
        Assert.Same(banishedCard, Assert.Single(zones.BanishedPile));
        Assert.Equal(CardZone.BanishedPile, banishedCard.Zone);
    }

    [Fact]
    public void DiscardHandDoesNotDiscardBanishedCards()
    {
        var zones = new CombatantCardZones();
        var handCard = CreateCard("card_hand", CardZone.Hand);
        var banishedCard = CreateCard("card_banished", CardZone.BanishedPile);

        zones.AddCard(handCard);
        zones.AddCard(banishedCard);

        zones.DiscardHand();

        Assert.Empty(zones.Hand);
        Assert.Same(handCard, Assert.Single(zones.DiscardPile));
        Assert.Same(banishedCard, Assert.Single(zones.BanishedPile));
        Assert.Equal(CardZone.BanishedPile, banishedCard.Zone);
    }

    private static CardInstance CreateCard(string id, CardZone zone)
    {
        return new CardInstance(
            new CardInstanceId(id),
            StrikeCardId,
            OwnerId,
            zone);
    }
}
