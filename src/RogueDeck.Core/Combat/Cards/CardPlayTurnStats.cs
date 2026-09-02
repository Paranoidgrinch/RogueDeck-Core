namespace RogueDeck.Core.Combat;

public sealed class CombatantCardPlayTurnStats
{
    private readonly Dictionary<CardDefinitionId, int> _cardsPlayedByDefinitionThisTurn = new();
    private readonly Dictionary<TagId, int> _cardsPlayedByTagThisTurn = new();

    // Card ORDERING within the turn: the tag set of the FIRST card played this turn (empty until one is
    // played). Content reads it for "the opening card is an Attack" / "first non-Junk card type" mechanics.
    private readonly HashSet<TagId> _firstCardPlayedTags = new();

    // Previous turn's snapshot, retained across Reset so "again" / habit mechanics (Whispered Prediction,
    // "the previous turn was Busy/Sparse", "you opened with Attack again") can compare against last turn.
    private readonly Dictionary<TagId, int> _cardsPlayedByTagLastTurn = new();
    private readonly HashSet<TagId> _firstCardPlayedTagsLastTurn = new();

    public int CardsPlayedThisTurn { get; private set; }

    public int DamageDealtThisTurn { get; private set; }

    public int ResourceGainedThisTurn { get; private set; }

    // How much the combatant has SPENT paying card costs this turn — the mirror of ResourceGainedThisTurn,
    // and the only honest answer to "what did this turn actually cost you": it is summed from the cost
    // ACTUALLY paid, after every cost modifier, so a tax that raised a card's price is inside the number and
    // a discount that lowered it is too. Every resource a cost names counts; a cost paid in something other
    // than energy is still expenditure.
    public int ResourceSpentThisTurn { get; private set; }

    // Definition of the first card played this turn, or null if none yet.
    public CardDefinitionId? FirstCardPlayedDefinitionId { get; private set; }

    public int CardsPlayedLastTurn { get; private set; }

    public IReadOnlyDictionary<CardDefinitionId, int> CardsPlayedByDefinitionThisTurn =>
        _cardsPlayedByDefinitionThisTurn;

    public IReadOnlyDictionary<TagId, int> CardsPlayedByTagThisTurn =>
        _cardsPlayedByTagThisTurn;

    public int GetCardsPlayedWithDefinitionThisTurn(CardDefinitionId cardDefinitionId)
    {
        return _cardsPlayedByDefinitionThisTurn.TryGetValue(cardDefinitionId, out var count)
            ? count
            : 0;
    }

    public int GetCardsPlayedWithTagThisTurn(TagId tagId)
    {
        return _cardsPlayedByTagThisTurn.TryGetValue(tagId, out var count)
            ? count
            : 0;
    }

    public int GetCardsPlayedWithTagLastTurn(TagId tagId)
    {
        return _cardsPlayedByTagLastTurn.TryGetValue(tagId, out var count)
            ? count
            : 0;
    }

    // Whether the FIRST card played this / last turn carried the given tag (its "opening type").
    public bool FirstCardPlayedThisTurnHasTag(TagId tagId) => _firstCardPlayedTags.Contains(tagId);
    public bool FirstCardPlayedLastTurnHasTag(TagId tagId) => _firstCardPlayedTagsLastTurn.Contains(tagId);

    public void RecordCardPlayed(CardDefinition card)
    {
        ArgumentNullException.ThrowIfNull(card);

        if (CardsPlayedThisTurn == 0)
        {
            FirstCardPlayedDefinitionId = card.Id;
            _firstCardPlayedTags.Clear();
            foreach (var tag in card.Tags)
                _firstCardPlayedTags.Add(tag);
        }

        CardsPlayedThisTurn++;

        if (!_cardsPlayedByDefinitionThisTurn.TryAdd(card.Id, 1))
            _cardsPlayedByDefinitionThisTurn[card.Id]++;

        foreach (var tag in card.Tags)
        {
            if (!_cardsPlayedByTagThisTurn.TryAdd(tag, 1))
                _cardsPlayedByTagThisTurn[tag]++;
        }
    }

    public void RecordDamageDealt(int healthDamage)
    {
        if (healthDamage > 0)
            DamageDealtThisTurn = checked(DamageDealtThisTurn + healthDamage);
    }

    public void RecordResourceGained(int amount)
    {
        if (amount > 0)
            ResourceGainedThisTurn = checked(ResourceGainedThisTurn + amount);
    }

    public void RecordResourceSpent(int amount)
    {
        if (amount > 0)
            ResourceSpentThisTurn = checked(ResourceSpentThisTurn + amount);
    }

    public void Reset()
    {
        // Retain this turn's play profile as "last turn" before clearing, for habit/prediction mechanics.
        CardsPlayedLastTurn = CardsPlayedThisTurn;
        _cardsPlayedByTagLastTurn.Clear();
        foreach (var (tag, count) in _cardsPlayedByTagThisTurn)
            _cardsPlayedByTagLastTurn[tag] = count;
        _firstCardPlayedTagsLastTurn.Clear();
        foreach (var tag in _firstCardPlayedTags)
            _firstCardPlayedTagsLastTurn.Add(tag);

        CardsPlayedThisTurn = 0;
        DamageDealtThisTurn = 0;
        ResourceGainedThisTurn = 0;
        ResourceSpentThisTurn = 0;
        FirstCardPlayedDefinitionId = null;
        _cardsPlayedByDefinitionThisTurn.Clear();
        _cardsPlayedByTagThisTurn.Clear();
        _firstCardPlayedTags.Clear();
    }
}

public sealed class TrackCardsPlayedThisTurnHandler
    : CombatEventHandler<CardPlayedCombatEvent>
{
    protected override void Handle(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CardPlayedCombatEvent combatEvent)
    {
        if (!combat.TryGetCombatant(combatEvent.SourceCombatantId, out _))
            return;

        var card = registry.GetCard(combatEvent.CardDefinitionId);
        var stats = combat.GetCardPlayTurnStats(combatEvent.SourceCombatantId);

        stats.RecordCardPlayed(card);
    }
}

public sealed class ResetCardPlayTurnStatsOnTurnStartedHandler
    : CombatEventHandler<TurnStartedCombatEvent>
{
    protected override void Handle(
        CombatState combat,
        CombatDefinitionRegistry registry,
        TurnStartedCombatEvent combatEvent)
    {
        if (!combat.TryGetCombatant(combatEvent.CombatantId, out _))
            return;

        combat.GetCardPlayTurnStats(combatEvent.CombatantId).Reset();
    }
}

public sealed class TrackDamageDealtThisTurnHandler
    : CombatEventHandler<DamageDealtCombatEvent>
{
    protected override void Handle(
        CombatState combat,
        CombatDefinitionRegistry registry,
        DamageDealtCombatEvent combatEvent)
    {
        if (combatEvent.SourceCombatantId is not { } sourceId)
            return;

        if (!combat.TryGetCombatant(sourceId, out _))
            return;

        combat.GetCardPlayTurnStats(sourceId).RecordDamageDealt(combatEvent.HealthDamage);
    }
}

public sealed class TrackResourceGainedThisTurnHandler
    : CombatEventHandler<ResourceGainedCombatEvent>
{
    protected override void Handle(
        CombatState combat,
        CombatDefinitionRegistry registry,
        ResourceGainedCombatEvent combatEvent)
    {
        if (!combat.TryGetCombatant(combatEvent.CombatantId, out _))
            return;

        combat.GetCardPlayTurnStats(combatEvent.CombatantId)
              .RecordResourceGained(combatEvent.GainedAmount);
    }
}

// A card's cost being paid is the one moment the engine knows what a play actually cost, after every
// modifier has had its say — so it is where expenditure is counted. A free play reports a zero cost and
// therefore adds nothing.
public sealed class TrackResourceSpentThisTurnHandler
    : CombatEventHandler<CardCostPaidCombatEvent>
{
    protected override void Handle(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CardCostPaidCombatEvent combatEvent)
    {
        if (!combat.TryGetCombatant(combatEvent.SourceCombatantId, out _))
            return;

        combat.GetCardPlayTurnStats(combatEvent.SourceCombatantId)
              .RecordResourceSpent(combatEvent.Costs.Sum(cost => cost.Amount));
    }
}
