namespace RogueDeck.Core.Combat;

public enum CardZone
{
    DrawPile,
    Hand,
    DiscardPile,
    ExhaustPile,
    BanishedPile,

    // Cards that have been PLAYED but whose effect has not happened yet — the Bureaucrat's Queue. Queueing a
    // card pays its cost and locks its target now; the card waits here, oldest first, until a resolution
    // window comes round (the owner's next turn, before the draw) or an effect resolves it early. Appended
    // last so every zone name already on the wire keeps its meaning.
    QueuePile
}

// Where a card lands in its destination zone. Draw takes from the front (index 0 = the "top"), so Top places a card
// on top of the draw pile (a tutor / "put this card on top") and Bottom appends it (the historical default). Only
// ordering matters for the draw pile, but the placement applies uniformly to any zone.
public enum ZonePlacement
{
    Bottom,
    Top
}

// The player-input collaborator for in-combat card selection — the combat analog of the run's IRunEntityChooser.
// A ChosenCardInZone expression calls this to let a player pick cards during a fight (e.g. Armaments: choose a
// card in hand to upgrade). Must be DETERMINISTIC for a given combat so replays reproduce. Set on CombatState by
// the driver; when absent, a chosen-card selection falls back to the first candidate (headless play).
public interface ICombatCardChooser
{
    IReadOnlyList<CardInstanceId> ChooseCards(IReadOnlyList<CardInstance> candidates, int count, string purpose);
}

public sealed class CardInstance
{
    public CardInstanceId Id { get; }
    public CardDefinitionId DefinitionId { get; private set; }
    public CombatantId OwnerId { get; }

    public CardZone Zone { get; private set; }

    // ── Per-instance marks ────────────────────────────────────────────────────────────────────────
    // A card instance may carry mutable marks that live ON THE INSTANCE (not its definition) and travel
    // with it through every zone: a set of mark tags, a small counter bag, and an optional binding to the
    // combatant that placed the mark. This mirrors StatusInstance's per-instance Tags/Counters/Source and
    // is the substrate for content mechanics such as Misfiled / Referenced / Redacted / Counted — a card is
    // marked, and later triggered programs react to the mark when the card is drawn, played, or leaves hand.
    // The engine attaches no behaviour to any specific mark; meaning is authored content.
    private readonly HashSet<TagId> _marks = new();
    private readonly Dictionary<CounterId, int> _markCounters = new();

    public IReadOnlySet<TagId> Marks => _marks;
    public IReadOnlyDictionary<CounterId, int> MarkCounters => _markCounters;

    // The combatant that applied the mark(s), when a mechanic is source-bound (e.g. a Reference belongs to
    // the enemy that created it, so death cleanup can find and clear it). Null = unbound.
    public CombatantId? MarkSourceCombatantId { get; private set; }

    // Whom this card was aimed at when it was QUEUED. The target is locked at queue time, so a queued card
    // that resolves later still hits what its player chose — and if that combatant is gone by then, the
    // target-bound parts of the card simply fizzle rather than picking a new victim. Null on any card that is
    // not waiting in the Queue, and on a queued card that never had a target.
    public CombatantId? QueuedTargetId { get; private set; }

    public void SetQueuedTarget(CombatantId? target) => QueuedTargetId = target;

    public CardInstance(
        CardInstanceId id,
        CardDefinitionId definitionId,
        CombatantId ownerId,
        CardZone zone,
        IEnumerable<TagId>? initialMarks = null,
        IEnumerable<KeyValuePair<CounterId, int>>? initialMarkCounters = null,
        CombatantId? markSourceCombatantId = null)
    {
        Id = id;
        DefinitionId = definitionId;
        OwnerId = ownerId;
        Zone = zone;

        if (initialMarks is not null)
            foreach (var mark in initialMarks)
                _marks.Add(mark);

        if (initialMarkCounters is not null)
            foreach (var (key, value) in initialMarkCounters)
                _markCounters[key] = value;

        MarkSourceCombatantId = markSourceCombatantId;
    }

    public void SetZone(CardZone zone)
    {
        Zone = zone;
    }

    // Change which definition this instance plays as — the in-combat transform/upgrade primitive. The card keeps
    // its stable Id/Owner; its program + costs are resolved from the definition registry by DefinitionId at play
    // time, so retargeting the definition changes the card's behaviour for the rest of the fight.
    public void SetDefinition(CardDefinitionId definitionId)
    {
        DefinitionId = definitionId;
    }

    // ── Mark mutation ─────────────────────────────────────────────────────────────────────────────
    public bool HasMark(TagId mark) => _marks.Contains(mark);

    // Adds a mark tag; returns true if it was newly added. Optionally (re)binds the mark source.
    public bool AddMark(TagId mark, CombatantId? source = null)
    {
        if (source is { } s)
            MarkSourceCombatantId = s;
        return _marks.Add(mark);
    }

    public bool RemoveMark(TagId mark) => _marks.Remove(mark);

    public int GetMarkCounter(CounterId id) =>
        _markCounters.TryGetValue(id, out var value) ? value : 0;

    public void SetMarkCounter(CounterId id, int value)
    {
        if (value == 0)
            _markCounters.Remove(id);
        else
            _markCounters[id] = value;
    }

    public void SetMarkSource(CombatantId? source) => MarkSourceCombatantId = source;
}

public sealed class CombatantCardZones
{
    private readonly List<CardInstance> _drawPile = new();
    private readonly List<CardInstance> _hand = new();
    private readonly List<CardInstance> _discardPile = new();
    private readonly List<CardInstance> _exhaustPile = new();
    private readonly List<CardInstance> _banishedPile = new();
    private readonly List<CardInstance> _queue = new();

    public IReadOnlyList<CardInstance> DrawPile => _drawPile;
    public IReadOnlyList<CardInstance> Hand => _hand;
    public IReadOnlyList<CardInstance> DiscardPile => _discardPile;
    public IReadOnlyList<CardInstance> ExhaustPile => _exhaustPile;
    public IReadOnlyList<CardInstance> BanishedPile => _banishedPile;

    // Oldest first: the Queue resolves FIFO, so index 0 is the card that has waited longest.
    public IReadOnlyList<CardInstance> Queue => _queue;

    public IReadOnlyCollection<CardInstance> AllCards => _drawPile
        .Concat(_hand)
        .Concat(_discardPile)
        .Concat(_exhaustPile)
        .Concat(_banishedPile)
        .Concat(_queue)
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

    public void MoveCardToZone(CardInstanceId id, CardZone zone, ZonePlacement placement = ZonePlacement.Bottom)
    {
        var card = GetCard(id);

        RemoveFromCurrentZone(card);
        card.SetZone(zone);
        var destination = GetMutableZone(zone);
        if (placement == ZonePlacement.Top)
            destination.Insert(0, card); // the front is the draw "top"
        else
            destination.Add(card);
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

    // Reorder the draw pile by a permutation of its current indexes (index i of the new order names the
    // draw-pile position of the card that should land at position i). Used to shuffle the opening draw
    // pile at combat start; the permutation must be a bijection over the pile.
    public IReadOnlyList<CardInstance> ReorderDrawPile(IReadOnlyList<int> drawPileIndexesInNewOrder)
    {
        ArgumentNullException.ThrowIfNull(drawPileIndexesInNewOrder);
        if (drawPileIndexesInNewOrder.Count != _drawPile.Count)
            throw new InvalidOperationException(
                "The reorder must contain exactly one index for each card in the draw pile.");

        var current = _drawPile.ToArray();
        var seen = new HashSet<int>();
        foreach (var sourceIndex in drawPileIndexesInNewOrder)
        {
            if (sourceIndex < 0 || sourceIndex >= current.Length)
                throw new InvalidOperationException($"Draw pile reorder index '{sourceIndex}' is out of range.");
            if (!seen.Add(sourceIndex))
                throw new InvalidOperationException($"Draw pile reorder index '{sourceIndex}' appears more than once.");
        }

        _drawPile.Clear();
        foreach (var sourceIndex in drawPileIndexesInNewOrder)
            _drawPile.Add(current[sourceIndex]);
        return _drawPile;
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
            CardZone.QueuePile => _queue,
            _ => throw new InvalidOperationException($"Unsupported card zone '{zone}'.")
        };
    }
}
