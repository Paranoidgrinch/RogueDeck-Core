namespace RogueDeck.Core.Combat;

public sealed record DrawCardsEffectRequest(
    CombatantId CombatantId,
    int Count,
    DrawCardsOutcomeSlot? OutcomeSlot = null
) : IEffectRequest;

public sealed class DrawCardsEffectHandler : EffectRequestHandler<DrawCardsEffectRequest>
{
    protected override void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        DrawCardsEffectRequest request)
    {
        if (request.Count < 0)
            throw new ArgumentOutOfRangeException(nameof(request.Count), "Draw count cannot be negative.");

        if (request.Count == 0)
        {
            if (request.OutcomeSlot is { } zeroSlot)
                zeroSlot.Value = new DrawCardsOutcome(0, 0, []);
            return;
        }

        var zones = combat.GetCardZones(request.CombatantId);
        var drawnCards = new List<CardInstance>();

        while (drawnCards.Count < request.Count)
        {
            var remainingCardsToDraw = request.Count - drawnCards.Count;
            var newlyDrawnCards = zones.DrawCards(remainingCardsToDraw);

            drawnCards.AddRange(newlyDrawnCards);

            if (drawnCards.Count >= request.Count)
                break;

            if (zones.DiscardPile.Count == 0)
                break;

            var shuffleOrder = CombatRandom.CreateShuffledIndexes(
                zones.DiscardPile.Count,
                combat.RandomSeed,
                combat.RandomStep);

            var shuffledCards = zones.MoveDiscardPileToDrawPile(shuffleOrder);

            combat.AdvanceRandomStep();

            var shuffledCardIds = shuffledCards
                .Select(card => card.Id)
                .ToArray();

            combat.AddLogEntry(
                StandardCombatLogTypes.DiscardPileShuffledIntoDrawPile,
                $"Combatant '{request.CombatantId}' shuffled {shuffledCardIds.Length} card(s) from discard pile into draw pile.");

            combat.EnqueueEvent(new DiscardPileShuffledIntoDrawPileCombatEvent(
                request.CombatantId,
                shuffledCardIds));
        }

        if (drawnCards.Count == 0)
        {
            if (request.OutcomeSlot is { } zeroSlot)
                zeroSlot.Value = new DrawCardsOutcome(request.Count, 0, []);
            return;
        }

        var drawnCardIds = drawnCards
            .Select(card => card.Id)
            .ToArray();

        if (request.OutcomeSlot is { } slot)
            slot.Value = new DrawCardsOutcome(request.Count, drawnCardIds.Length, drawnCardIds);

        combat.AddLogEntry(
            StandardCombatLogTypes.CardsDrawn,
            $"Combatant '{request.CombatantId}' drew {drawnCardIds.Length} card(s).");

        combat.EnqueueEvent(new CardsDrawnCombatEvent(
            request.CombatantId,
            drawnCardIds));
    }
}

public sealed record DiscardHandEffectRequest(
    CombatantId CombatantId,
    bool IncludeRetainedCards = true
) : IEffectRequest;

public sealed class DiscardHandEffectHandler : EffectRequestHandler<DiscardHandEffectRequest>
{
    protected override void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        DiscardHandEffectRequest request)
    {
        var zones = combat.GetCardZones(request.CombatantId);

        var cardsToDiscard = zones.Hand
            .Where(card => request.IncludeRetainedCards || !IsRetainedInHandOnTurnEnd(registry, card))
            .ToArray();

        if (cardsToDiscard.Length == 0)
            return;

        foreach (var card in cardsToDiscard)
            zones.MoveCardToZone(card.Id, CardZone.DiscardPile);

        var discardedCardIds = cardsToDiscard
            .Select(card => card.Id)
            .ToArray();

        combat.AddLogEntry(
            StandardCombatLogTypes.HandDiscarded,
            $"Combatant '{request.CombatantId}' discarded {discardedCardIds.Length} card(s).");

        combat.EnqueueEvent(new HandDiscardedCombatEvent(
            request.CombatantId,
            discardedCardIds));
    }

    private static bool IsRetainedInHandOnTurnEnd(
        CombatDefinitionRegistry registry,
        CardInstance card)
    {
        var definition = registry.GetCard(card.DefinitionId);

        return definition.RetainInHandOnTurnEnd;
    }
}

public sealed record MoveCardToZoneEffectRequest(
    CombatantId CombatantId,
    CardInstanceId CardInstanceId,
    CardZone ToZone,
    MoveCardToZoneOutcomeSlot? OutcomeSlot = null,
    // Where the card lands in the destination zone — Top for a tutor ("put on top of the draw pile"), Bottom (the
    // default) to append, preserving historical behaviour for every existing caller.
    ZonePlacement Placement = ZonePlacement.Bottom
) : IEffectRequest;

public sealed class MoveCardToZoneEffectHandler : EffectRequestHandler<MoveCardToZoneEffectRequest>
{
    protected override void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        MoveCardToZoneEffectRequest request)
    {
        var zones = combat.GetCardZones(request.CombatantId);
        var card = zones.GetCard(request.CardInstanceId);

        if (card.OwnerId != request.CombatantId)
        {
            throw new InvalidOperationException(
                $"Card instance '{request.CardInstanceId}' is not owned by combatant '{request.CombatantId}'.");
        }

        var fromZone = card.Zone;

        // A card asked to go where it already is normally stays put — but "put it on TOP" is a request about
        // POSITION, not about zones, and the tutor that says it (an inscription fetching its partner to the
        // top of the draw pile) has nowhere else to fetch from. So an explicit Top repositions; Bottom keeps
        // the historical no-op, so nothing authored before placements existed changes under it.
        if (fromZone == request.ToZone)
        {
            if (request.Placement == ZonePlacement.Top)
                zones.MoveCardToZone(request.CardInstanceId, request.ToZone, request.Placement);
            if (request.OutcomeSlot is { } noOpSlot)
                noOpSlot.Value = new MoveCardToZoneOutcome(
                    request.CardInstanceId, fromZone, fromZone, WasMoved: false);
            return;
        }

        zones.MoveCardToZone(request.CardInstanceId, request.ToZone, request.Placement);

        if (request.OutcomeSlot is { } slot)
            slot.Value = new MoveCardToZoneOutcome(
                request.CardInstanceId, fromZone, request.ToZone, WasMoved: true);

        combat.AddLogEntry(
            StandardCombatLogTypes.CardMovedToZone,
            $"Moved card instance '{request.CardInstanceId}' for combatant '{request.CombatantId}' from '{fromZone}' to '{request.ToZone}'.");

        combat.EnqueueEvent(new CardMovedToZoneCombatEvent(
            request.CombatantId,
            request.CardInstanceId,
            fromZone,
            request.ToZone));
    }
}

// Retarget a card instance to a different definition (in-combat transform / upgrade). The instance keeps its id +
// zone; only what it plays as changes. A transform to the same definition is a no-op (still completes the slot).
public sealed record TransformCardEffectRequest(
    CombatantId CombatantId,
    CardInstanceId CardInstanceId,
    CardDefinitionId ToDefinition,
    TransformCardOutcomeSlot? OutcomeSlot = null
) : IEffectRequest;

public sealed class TransformCardEffectHandler : EffectRequestHandler<TransformCardEffectRequest>
{
    protected override void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        TransformCardEffectRequest request)
    {
        var zones = combat.GetCardZones(request.CombatantId);
        var card = zones.GetCard(request.CardInstanceId);

        if (card.OwnerId != request.CombatantId)
            throw new InvalidOperationException(
                $"Card instance '{request.CardInstanceId}' is not owned by combatant '{request.CombatantId}'.");

        var fromDefinition = card.DefinitionId;

        if (fromDefinition == request.ToDefinition)
        {
            if (request.OutcomeSlot is { } noOpSlot)
                noOpSlot.Value = new TransformCardOutcome(
                    request.CardInstanceId, fromDefinition, fromDefinition, WasTransformed: false);
            return;
        }

        card.SetDefinition(request.ToDefinition);

        if (request.OutcomeSlot is { } slot)
            slot.Value = new TransformCardOutcome(
                request.CardInstanceId, fromDefinition, request.ToDefinition, WasTransformed: true);

        combat.AddLogEntry(
            StandardCombatLogTypes.CardTransformed,
            $"Transformed card instance '{request.CardInstanceId}' for combatant '{request.CombatantId}' from '{fromDefinition}' to '{request.ToDefinition}'.");

        combat.EnqueueEvent(new CardTransformedCombatEvent(
            request.CombatantId, request.CardInstanceId, fromDefinition, request.ToDefinition));
    }
}

// ── Card-instance marks ─────────────────────────────────────────────────────────────────────────
// Add or remove a per-instance mark tag on a concrete card. The mark lives on the instance and travels
// with the card through zones; meaning is authored content (Misfiled / Referenced / Redacted / Counted).
// When adding, an optional Source binds the mark to a combatant so death cleanup can find and clear it.
public sealed record MarkCardInstanceEffectRequest(
    CombatantId CombatantId,
    CardInstanceId CardInstanceId,
    TagId Mark,
    bool Remove = false,
    CombatantId? Source = null
) : IEffectRequest;

public sealed class MarkCardInstanceEffectHandler : EffectRequestHandler<MarkCardInstanceEffectRequest>
{
    protected override void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        MarkCardInstanceEffectRequest request)
    {
        var zones = combat.GetCardZones(request.CombatantId);
        var card = zones.GetCard(request.CardInstanceId);

        if (card.OwnerId != request.CombatantId)
            throw new InvalidOperationException(
                $"Card instance '{request.CardInstanceId}' is not owned by combatant '{request.CombatantId}'.");

        if (request.Remove)
        {
            card.RemoveMark(request.Mark);
            combat.AddLogEntry(
                StandardCombatLogTypes.CardMarkChanged,
                $"Removed mark '{request.Mark.value}' from card instance '{request.CardInstanceId}'.");
        }
        else
        {
            card.AddMark(request.Mark, request.Source);
            combat.AddLogEntry(
                StandardCombatLogTypes.CardMarkChanged,
                $"Marked card instance '{request.CardInstanceId}' with '{request.Mark.value}'.");
        }
    }
}

// Set or adjust a per-instance mark counter on a concrete card (e.g. a Reference's remaining strength).
public sealed record SetCardInstanceMarkCounterEffectRequest(
    CombatantId CombatantId,
    CardInstanceId CardInstanceId,
    CounterId Counter,
    int Value,
    bool Relative = false
) : IEffectRequest;

public sealed class SetCardInstanceMarkCounterEffectHandler
    : EffectRequestHandler<SetCardInstanceMarkCounterEffectRequest>
{
    protected override void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        SetCardInstanceMarkCounterEffectRequest request)
    {
        var zones = combat.GetCardZones(request.CombatantId);
        var card = zones.GetCard(request.CardInstanceId);

        if (card.OwnerId != request.CombatantId)
            throw new InvalidOperationException(
                $"Card instance '{request.CardInstanceId}' is not owned by combatant '{request.CombatantId}'.");

        var newValue = request.Relative
            ? card.GetMarkCounter(request.Counter) + request.Value
            : request.Value;

        card.SetMarkCounter(request.Counter, newValue);

        combat.AddLogEntry(
            StandardCombatLogTypes.CardMarkChanged,
            $"Set mark counter '{request.Counter.value}'={newValue} on card instance '{request.CardInstanceId}'.");
    }
}

public sealed record MoveHandCardsOnTurnEndEffectRequest(
    CombatantId CombatantId
) : IEffectRequest;

public sealed class MoveHandCardsOnTurnEndEffectHandler
    : EffectRequestHandler<MoveHandCardsOnTurnEndEffectRequest>
{
    protected override void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        MoveHandCardsOnTurnEndEffectRequest request)
    {
        var zones = combat.GetCardZones(request.CombatantId);
        var cardsInHand = zones.Hand.ToArray();

        if (cardsInHand.Length == 0)
            return;

        var movedCardsByDestination = new Dictionary<CardZone, List<CardInstance>>();

        foreach (var card in cardsInHand)
        {
            var destinationZone = ResolveTurnEndDestinationZone(registry, card);

            if (destinationZone == CardZone.Hand)
                continue;

            zones.MoveCardToZone(card.Id, destinationZone);

            if (!movedCardsByDestination.TryGetValue(destinationZone, out var movedCards))
            {
                movedCards = new List<CardInstance>();
                movedCardsByDestination.Add(destinationZone, movedCards);
            }

            movedCards.Add(card);
        }

        foreach (var pair in movedCardsByDestination)
        {
            var destinationZone = pair.Key;
            var movedCards = pair.Value;
            var movedCardIds = movedCards
                .Select(card => card.Id)
                .ToArray();

            if (destinationZone == CardZone.DiscardPile)
            {
                combat.AddLogEntry(
                    StandardCombatLogTypes.HandDiscarded,
                    $"Combatant '{request.CombatantId}' discarded {movedCardIds.Length} card(s).");

                combat.EnqueueEvent(new HandDiscardedCombatEvent(
                    request.CombatantId,
                    movedCardIds));

                continue;
            }

            combat.AddLogEntry(
                StandardCombatLogTypes.CardsMovedBetweenZones,
                $"Moved {movedCardIds.Length} card(s) for combatant '{request.CombatantId}' from hand to '{destinationZone}' at turn end.");

            combat.EnqueueEvent(new CardsMovedBetweenZonesCombatEvent(
                request.CombatantId,
                movedCardIds,
                FromZone: CardZone.Hand,
                ToZone: destinationZone));
        }
    }

    private static CardZone ResolveTurnEndDestinationZone(
        CombatDefinitionRegistry registry,
        CardInstance card)
    {
        // A copy marked as retained stays put whatever its definition says — the per-instance counterpart of
        // the definition flag below.
        if (card.Marks.Contains(StandardCombatIds.RetainedCardMark))
            return CardZone.Hand;

        var definition = registry.GetCard(card.DefinitionId);

        if (definition.RetainInHandOnTurnEnd)
            return CardZone.Hand;

        return definition.TurnEndHandDestinationZone;
    }
}

