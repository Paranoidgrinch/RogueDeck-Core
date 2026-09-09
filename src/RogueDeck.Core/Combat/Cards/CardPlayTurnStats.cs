namespace RogueDeck.Core.Combat;

public sealed class CombatantCardPlayTurnStats
{
    private readonly Dictionary<CardDefinitionId, int> _cardsPlayedByDefinitionThisTurn = new();
    private readonly Dictionary<TagId, int> _cardsPlayedByTagThisTurn = new();

    // Card ORDERING within the turn: the tag set of the FIRST card played this turn (empty until one is
    // played). Content reads it for "the opening card is an Attack" / "first non-Junk card type" mechanics.
    private readonly HashSet<TagId> _firstCardPlayedTags = new();

    // The tag set of the card played MOST RECENTLY this turn (empty until one is played). The first card's
    // tags answer "what did you open with"; this answers "what did you just do", which is the only thing a
    // rule about SUCCESSION can be written against — "no work shall follow its likeness" has to compare the
    // card being played against the one immediately before it, not against the turn's opening.
    private readonly HashSet<TagId> _lastCardPlayedTags = new();

    // What this turn has already spent its once-per-turn allowances on (passive-modifier specs marked
    // OncePerTurn). Cleared with everything else at the turn start, and captured, because a fight that is
    // put down and picked up again mid-turn must not hand back an allowance it has already used.
    private readonly HashSet<string> _claimedThisTurn = new(StringComparer.Ordinal);

    // Previous turn's snapshot, retained across Reset so "again" / habit mechanics (Whispered Prediction,
    // "the previous turn was Busy/Sparse", "you opened with Attack again") can compare against last turn.
    private readonly Dictionary<TagId, int> _cardsPlayedByTagLastTurn = new();
    private readonly HashSet<TagId> _firstCardPlayedTagsLastTurn = new();

    public int CardsPlayedThisTurn { get; private set; }

    // How many separate DRAWS the combatant has taken this turn. A turn's opening hand is the first of them,
    // and that is the only way a rule can say "when my turn begins, with my hand in front of me": a turn-start
    // trigger runs before the hand exists, so everything that means "at the hand" hangs off the draw — and a
    // draw is a draw, whoever asked for it. Without this number a rule reacting to the hand also reacts to
    // every card any other rule draws, which is how a relic that rings "every third turn" comes to ring on
    // its own ringing.
    public int CardDrawsThisTurn { get; private set; }

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

    // Definition of the card played most recently this turn, or null if none yet.
    public CardDefinitionId? LastCardPlayedDefinitionId { get; private set; }

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

    // Whether the card played most recently this turn carried the given tag.
    public bool LastCardPlayedThisTurnHasTag(TagId tagId) => _lastCardPlayedTags.Contains(tagId);

    // Claims one named allowance for this turn. True the first time it is asked for, false ever after —
    // the turn-scoped sibling of CombatState.TryClaimOnceThisAction.
    public bool TryClaimOnceThisTurn(string key) => _claimedThisTurn.Add(key);

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
        LastCardPlayedDefinitionId = card.Id;
        _lastCardPlayedTags.Clear();
        foreach (var tag in card.Tags)
            _lastCardPlayedTags.Add(tag);

        if (!_cardsPlayedByDefinitionThisTurn.TryAdd(card.Id, 1))
            _cardsPlayedByDefinitionThisTurn[card.Id]++;

        foreach (var tag in card.Tags)
        {
            if (!_cardsPlayedByTagThisTurn.TryAdd(tag, 1))
                _cardsPlayedByTagThisTurn[tag]++;
        }
    }

    public void RecordCardsDrawn() => CardDrawsThisTurn++;

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
        CardDrawsThisTurn = 0;
        DamageDealtThisTurn = 0;
        ResourceGainedThisTurn = 0;
        ResourceSpentThisTurn = 0;
        FirstCardPlayedDefinitionId = null;
        LastCardPlayedDefinitionId = null;
        _cardsPlayedByDefinitionThisTurn.Clear();
        _cardsPlayedByTagThisTurn.Clear();
        _firstCardPlayedTags.Clear();
        _lastCardPlayedTags.Clear();
        _claimedThisTurn.Clear();
    }
    // ── capture & restore ─────────────────────────────────────────────────────────
    //
    // A fight can be put down and picked up again — a save taken at a turn boundary, or the replay moving its
    // baseline forward inside a long fight — and what a turn REMEMBERS has to survive that. It did not: a
    // restored fight came back with an empty history, so every "more than last turn", "you opened with an
    // Attack again", "the third copy this turn" rule silently read zero. Nothing caught it, because until a
    // baseline moved inside a fight nothing ever restored one mid-flight.

    public CardPlayTurnStatsSnapshot Capture() => new(
        CardsPlayedThisTurn, CardsPlayedLastTurn, DamageDealtThisTurn, ResourceGainedThisTurn,
        ResourceSpentThisTurn, FirstCardPlayedDefinitionId?.value,
        [.. _cardsPlayedByDefinitionThisTurn.Select(e => (e.Key.value, e.Value)).OrderBy(e => e.Item1, StringComparer.Ordinal)],
        [.. _cardsPlayedByTagThisTurn.Select(e => (e.Key.value, e.Value)).OrderBy(e => e.Item1, StringComparer.Ordinal)],
        [.. _cardsPlayedByTagLastTurn.Select(e => (e.Key.value, e.Value)).OrderBy(e => e.Item1, StringComparer.Ordinal)],
        [.. _firstCardPlayedTags.Select(t => t.value).OrderBy(v => v, StringComparer.Ordinal)],
        [.. _firstCardPlayedTagsLastTurn.Select(t => t.value).OrderBy(v => v, StringComparer.Ordinal)],
        CardDrawsThisTurn,
        LastCardPlayedDefinitionId?.value,
        [.. _lastCardPlayedTags.Select(t => t.value).OrderBy(v => v, StringComparer.Ordinal)],
        [.. _claimedThisTurn.OrderBy(v => v, StringComparer.Ordinal)]);

    public void Restore(CardPlayTurnStatsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        CardsPlayedThisTurn = snapshot.CardsPlayedThisTurn;
        CardDrawsThisTurn = snapshot.CardDrawsThisTurn;
        CardsPlayedLastTurn = snapshot.CardsPlayedLastTurn;
        DamageDealtThisTurn = snapshot.DamageDealtThisTurn;
        ResourceGainedThisTurn = snapshot.ResourceGainedThisTurn;
        ResourceSpentThisTurn = snapshot.ResourceSpentThisTurn;
        FirstCardPlayedDefinitionId = snapshot.FirstCardPlayedDefinitionId is { } id
            ? new CardDefinitionId(id)
            : null;
        _cardsPlayedByDefinitionThisTurn.Clear();
        foreach (var (key, count) in snapshot.ByDefinitionThisTurn)
            _cardsPlayedByDefinitionThisTurn[new CardDefinitionId(key)] = count;
        _cardsPlayedByTagThisTurn.Clear();
        foreach (var (key, count) in snapshot.ByTagThisTurn)
            _cardsPlayedByTagThisTurn[new TagId(key)] = count;
        _cardsPlayedByTagLastTurn.Clear();
        foreach (var (key, count) in snapshot.ByTagLastTurn)
            _cardsPlayedByTagLastTurn[new TagId(key)] = count;
        _firstCardPlayedTags.Clear();
        foreach (var tag in snapshot.FirstCardTagsThisTurn)
            _firstCardPlayedTags.Add(new TagId(tag));
        _firstCardPlayedTagsLastTurn.Clear();
        foreach (var tag in snapshot.FirstCardTagsLastTurn)
            _firstCardPlayedTagsLastTurn.Add(new TagId(tag));
        LastCardPlayedDefinitionId = snapshot.LastCardPlayedDefinitionId is { } lastId
            ? new CardDefinitionId(lastId)
            : null;
        _lastCardPlayedTags.Clear();
        foreach (var tag in snapshot.LastCardTagsThisTurn)
            _lastCardPlayedTags.Add(new TagId(tag));
        _claimedThisTurn.Clear();
        foreach (var claim in snapshot.ClaimedThisTurn)
            _claimedThisTurn.Add(claim);
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

// Every draw is counted, and the count is taken BEFORE the draw is announced — so a rule reacting to the
// announcement can ask which draw of the turn it is looking at.
public sealed class TrackCardDrawsThisTurnHandler
    : CombatEventHandler<CardsDrawnCombatEvent>
{
    protected override void Handle(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CardsDrawnCombatEvent combatEvent)
    {
        if (!combat.TryGetCombatant(combatEvent.CombatantId, out _))
            return;

        combat.GetCardPlayTurnStats(combatEvent.CombatantId).RecordCardsDrawn();
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

// What one combatant's turn remembers, as values. Ordered by key throughout so the capture is stable.
public sealed record CardPlayTurnStatsSnapshot(
    int CardsPlayedThisTurn,
    int CardsPlayedLastTurn,
    int DamageDealtThisTurn,
    int ResourceGainedThisTurn,
    int ResourceSpentThisTurn,
    string? FirstCardPlayedDefinitionId,
    System.Collections.Immutable.ImmutableArray<(string Key, int Count)> ByDefinitionThisTurn,
    System.Collections.Immutable.ImmutableArray<(string Key, int Count)> ByTagThisTurn,
    System.Collections.Immutable.ImmutableArray<(string Key, int Count)> ByTagLastTurn,
    System.Collections.Immutable.ImmutableArray<string> FirstCardTagsThisTurn,
    System.Collections.Immutable.ImmutableArray<string> FirstCardTagsLastTurn,
    // Last, and defaulted: a snapshot written before a turn could count its draws reads zero, which is what
    // a fight that never drew twice in a turn would have said anyway.
    int CardDrawsThisTurn = 0,
    // What the turn played LAST, and what it has already claimed — both defaulted for the same reason: a
    // snapshot written before these existed reads "nothing played yet, nothing claimed yet", which is the
    // truth for every fight that never met a rule about succession or a once-a-turn ceiling.
    string? LastCardPlayedDefinitionId = null,
    System.Collections.Immutable.ImmutableArray<string> LastCardTagsThisTurn = default,
    System.Collections.Immutable.ImmutableArray<string> ClaimedThisTurn = default);
