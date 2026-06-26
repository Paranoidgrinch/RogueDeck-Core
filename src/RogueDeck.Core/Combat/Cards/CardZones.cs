namespace RogueDeck.Core.Combat;

public enum CardZone
{
    DrawPile,
    Hand,
    DiscardPile,
    ExhaustPile,
    BanishedPile
}

public sealed class CardInstance
{
    public CardInstanceId Id { get; }
    public CardDefinitionId DefinitionId { get; }
    public CombatantId OwnerId { get; }

    public CardZone Zone { get; private set; }

    public CardInstance(
        CardInstanceId id,
        CardDefinitionId definitionId,
        CombatantId ownerId,
        CardZone zone)
    {
        Id = id;
        DefinitionId = definitionId;
        OwnerId = ownerId;
        Zone = zone;
    }

    public void SetZone(CardZone zone)
    {
        Zone = zone;
    }
}

public sealed class CombatantCardZones
{
    private readonly List<CardInstance> _drawPile = new();
    private readonly List<CardInstance> _hand = new();
    private readonly List<CardInstance> _discardPile = new();
    private readonly List<CardInstance> _exhaustPile = new();
    private readonly List<CardInstance> _banishedPile = new();

    public IReadOnlyList<CardInstance> DrawPile => _drawPile;
    public IReadOnlyList<CardInstance> Hand => _hand;
    public IReadOnlyList<CardInstance> DiscardPile => _discardPile;
    public IReadOnlyList<CardInstance> ExhaustPile => _exhaustPile;
    public IReadOnlyList<CardInstance> BanishedPile => _banishedPile;

    public IReadOnlyCollection<CardInstance> AllCards => _drawPile
        .Concat(_hand)
        .Concat(_discardPile)
        .Concat(_exhaustPile)
        .Concat(_banishedPile)
        .ToArray();

    public void AddCard(CardInstance card)
    {
        ArgumentNullException.ThrowIfNull(card);

        if (ContainsCard(card.Id))
            throw new InvalidOperationException($"Card instance '{card.Id}' already exists in these card zones.");

        GetMutableZone(card.Zone).Add(card);
    }

    public CardInstance GetCard(CardInstanceId id)
    {
        var card = AllCards.FirstOrDefault(existing => existing.Id == id);

        if (card is null)
            throw new InvalidOperationException($"Card instance '{id}' does not exist in these card zones.");

        return card;
    }

    public bool ContainsCard(CardInstanceId id)
    {
        return AllCards.Any(card => card.Id == id);
    }

    public IReadOnlyList<CardInstance> GetCardsInZone(CardZone zone)
    {
        return GetMutableZone(zone).ToArray();
    }

    public void MoveCardToZone(CardInstanceId id, CardZone zone)
    {
        var card = GetCard(id);

        RemoveFromCurrentZone(card);
        card.SetZone(zone);
        GetMutableZone(zone).Add(card);
    }

    public IReadOnlyList<CardInstance> MoveAllCardsFromZone(
        CardZone fromZone,
        CardZone toZone)
    {
        if (fromZone == toZone)
            return Array.Empty<CardInstance>();

        var cardsToMove = GetMutableZone(fromZone).ToArray();

        foreach (var card in cardsToMove)
            MoveCardToZone(card.Id, toZone);

        return cardsToMove;
    }

    public IReadOnlyList<CardInstance> DrawCards(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Draw count cannot be negative.");

        var drawnCards = new List<CardInstance>();

        while (drawnCards.Count < count && _drawPile.Count > 0)
        {
            var card = _drawPile[0];

            _drawPile.RemoveAt(0);
            card.SetZone(CardZone.Hand);
            _hand.Add(card);
            drawnCards.Add(card);
        }

        return drawnCards;
    }

    public IReadOnlyList<CardInstance> MoveDiscardPileToDrawPile(
        IReadOnlyList<int> discardPileIndexesInNewDrawOrder)
    {
        ArgumentNullException.ThrowIfNull(discardPileIndexesInNewDrawOrder);

        if (discardPileIndexesInNewDrawOrder.Count != _discardPile.Count)
            throw new InvalidOperationException(
                "The shuffle order must contain exactly one index for each card in the discard pile.");

        var discardCards = _discardPile.ToArray();
        var seenIndexes = new HashSet<int>();

        foreach (var sourceIndex in discardPileIndexesInNewDrawOrder)
        {
            if (sourceIndex < 0 || sourceIndex >= discardCards.Length)
                throw new InvalidOperationException(
                    $"Discard pile shuffle index '{sourceIndex}' is outside the discard pile.");

            if (!seenIndexes.Add(sourceIndex))
                throw new InvalidOperationException(
                    $"Discard pile shuffle index '{sourceIndex}' appears more than once.");
        }

        _discardPile.Clear();

        var movedCards = new List<CardInstance>();

        foreach (var sourceIndex in discardPileIndexesInNewDrawOrder)
        {
            var card = discardCards[sourceIndex];

            card.SetZone(CardZone.DrawPile);
            _drawPile.Add(card);
            movedCards.Add(card);
        }

        return movedCards;
    }

    public void DiscardHand()
    {
        foreach (var card in _hand.ToArray())
            MoveCardToZone(card.Id, CardZone.DiscardPile);
    }

    private void RemoveFromCurrentZone(CardInstance card)
    {
        var currentZone = GetMutableZone(card.Zone);

        if (!currentZone.Remove(card))
            throw new InvalidOperationException(
                $"Card instance '{card.Id}' was not found in its current zone '{card.Zone}'.");
    }

    private List<CardInstance> GetMutableZone(CardZone zone)
    {
        return zone switch
        {
            CardZone.DrawPile => _drawPile,
            CardZone.Hand => _hand,
            CardZone.DiscardPile => _discardPile,
            CardZone.ExhaustPile => _exhaustPile,
            CardZone.BanishedPile => _banishedPile,
            _ => throw new InvalidOperationException($"Unsupported card zone '{zone}'.")
        };
    }
}
