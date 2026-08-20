namespace RogueDeck.Core.Combat;

public sealed record CardPlayRequest(
    CardDefinitionId CardDefinitionId,
    CombatantId SourceCombatantId,
    CombatantId? TargetCombatantId = null);

public sealed record CardInstancePlayRequest(
    CardInstanceId CardInstanceId,
    CombatantId SourceCombatantId,
    CombatantId? TargetCombatantId = null);

public sealed class CombatCardPlayProcessor
{
    private readonly CombatQueueProcessor _queueProcessor = new();

    public void PlayCard(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CardPlayRequest request,
        CombatExecutionLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        PlayCardCore(
            combat,
            registry,
            request.CardDefinitionId,
            request.SourceCombatantId,
            request.TargetCombatantId,
            cardInstanceId: null,
            limits);
    }

    public void PlayCardInstance(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CardInstancePlayRequest request,
        CombatExecutionLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(request);

        var zones = combat.GetCardZones(request.SourceCombatantId);
        var cardInstance = zones.GetCard(request.CardInstanceId);

        if (cardInstance.OwnerId != request.SourceCombatantId)
        {
            throw new InvalidOperationException(
                $"Card instance '{request.CardInstanceId}' is not owned by combatant '{request.SourceCombatantId}'.");
        }

        if (cardInstance.Zone != CardZone.Hand)
        {
            throw new InvalidOperationException(
                $"Card instance '{request.CardInstanceId}' cannot be played because it is in zone '{cardInstance.Zone}'.");
        }

        PlayCardCore(
            combat,
            registry,
            cardInstance.DefinitionId,
            request.SourceCombatantId,
            request.TargetCombatantId,
            cardInstance.Id,
            limits);
    }

    private void PlayCardCore(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CardDefinitionId cardDefinitionId,
        CombatantId sourceCombatantId,
        CombatantId? targetCombatantId,
        CardInstanceId? cardInstanceId,
        CombatExecutionLimits? limits)
    {
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(registry);

        if (combat.Result != CombatResult.Ongoing)
        {
            throw new InvalidOperationException(
                $"Cannot play card because combat result is '{combat.Result}'.");
        }

        var source = combat.GetCombatant(sourceCombatantId);

        if (!source.IsAlive)
        {
            throw new InvalidOperationException(
                $"Combatant '{sourceCombatantId}' cannot play cards because it is not alive.");
        }

        var card = registry.GetCard(cardDefinitionId);

        ValidateCardPlay(
            combat,
            registry,
            card,
            source,
            targetCombatantId,
            cardInstanceId);

        EnqueueCardPlayEffects(combat, registry, card, source, targetCombatantId, cardInstanceId);

        _queueProcessor.ResolvePendingQueues(combat, registry, limits);
    }

    // Enqueues all effects produced by playing a card. Does NOT call ResolvePendingQueues —
    // callers either do it themselves (CombatCardPlayProcessor) or rely on an outer queue
    // processor already running (PlayCardEffectHandler).
    internal static void EnqueueCardPlayEffects(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CardDefinition card,
        CombatantState source,
        CombatantId? targetCombatantId,
        CardInstanceId? cardInstanceId)
    {
        var effectRequests = BuildEffectRequests(combat, card, source, targetCombatantId);
        var costsToPay = CalculateCosts(combat, registry, card, source, targetCombatantId, cardInstanceId, trace: true);

        EnsureCostsCanBePaid(source, card, costsToPay);
        PayCosts(source, costsToPay);

        AddCardCostPaidLogAndEvent(combat, source.Id, card.Id, cardInstanceId, costsToPay);

        combat.AddLogEntry(
            StandardCombatLogTypes.CardPlayed,
            $"Combatant '{source.Id}' played card '{card.Id}'.");

        combat.EnqueueEvent(
            new CardPlayedCombatEvent(
                CardDefinitionId: card.Id,
                SourceCombatantId: source.Id,
                TargetCombatantId: targetCombatantId,
                CardInstanceId: cardInstanceId));

        // Redacted substrate: if the played instance carries a next-play output-scale mark, read + consume it
        // ONCE here (a one-shot reduction), then apply the fraction to BOTH card execution paths — the legacy
        // effect-request list (below) and the on-play Program (via its execution context). It narrows only
        // player-facing output (damage/Block/heal/draw/energy/status), never cost/hit-count/target-count.
        var scaleNum = 1;
        var scaleDen = 1;
        if (cardInstanceId is { } scaleInstanceId)
        {
            var playedCard = combat.GetCardZones(source.Id).GetCard(scaleInstanceId);
            var den = playedCard.GetMarkCounter(StandardCombatIds.CardOutputScaleDenominatorCounter);
            if (den > 0)
            {
                scaleNum = playedCard.GetMarkCounter(StandardCombatIds.CardOutputScaleNumeratorCounter);
                scaleDen = den;
                playedCard.SetMarkCounter(StandardCombatIds.CardOutputScaleNumeratorCounter, 0);
                playedCard.SetMarkCounter(StandardCombatIds.CardOutputScaleDenominatorCounter, 0);
            }
        }
        var outputScaled = scaleNum < scaleDen;

        foreach (var effectRequest in effectRequests)
            combat.EnqueueEffect(outputScaled
                ? CardOutputScaling.ScaleRequest(effectRequest, scaleNum, scaleDen)
                : effectRequest);

        if (card.Program is { } program)
        {
            var buildContext = CreateCardPlayBuildContext(combat, card.Id, source, targetCombatantId);

            // Open the play's once-per-play ledger around the whole program, so every hit the program
            // produces — including the ones its own repeat/replay nodes produce — counts as this one play.
            combat.BeginCardPlayScope();

            Action<EffectProgramExecutionState, CombatState>? onTerminal = null;
            if (cardInstanceId is not null)
            {
                var capturedPlayerId = source.Id;
                var capturedCardId = cardInstanceId.Value;
                var capturedDestZone = card.PlayedCardDestinationZone;

                // Card placement runs when the on-play program reaches a terminal state, with
                // behaviour defined per outcome so a played card never gets stuck in hand:
                //   Completed — normal play: route the move through the effect queue so move
                //               events and the log fire while the queue is still draining.
                //   Faulted   — the play happened (cost paid, CardPlayed fired) but the program
                //               threw. The queue will not drain further once the fault unwinds,
                //               so move the card directly to its destination zone; a broken play
                //               must not leave a replayable card in hand.
                //   Cancelled — combat ended mid-program: leave the card as-is; combat is over
                //               and the queue has stopped.
                onTerminal = (state, c) =>
                {
                    var zones = c.GetCardZones(capturedPlayerId);
                    if (!zones.ContainsCard(capturedCardId) ||
                        zones.GetCard(capturedCardId).Zone != CardZone.Hand)
                        return;

                    if (state == EffectProgramExecutionState.Completed)
                    {
                        c.EnqueueEffect(new MoveCardToZoneEffectRequest(
                            capturedPlayerId, capturedCardId, capturedDestZone));
                    }
                    else if (state == EffectProgramExecutionState.Faulted)
                    {
                        zones.MoveCardToZone(capturedCardId, capturedDestZone);
                        c.AddLogEntry(
                            StandardCombatLogTypes.CardMovedToZone,
                            $"Card '{capturedCardId}' moved to '{capturedDestZone}' after its " +
                            $"on-play program faulted.");
                    }
                    // Cancelled (combat ended): leave the card as-is; combat is over.
                };
            }

            var executionContext = new EffectExecutionContext<CardPlayContext>(
                new CardPlayContext(card, cardInstanceId), buildContext);

            if (outputScaled)
                executionContext.SetOutputScale(scaleNum, scaleDen);

            var placeCard = onTerminal;
            EffectProgramExecutor.Execute(
                program, executionContext, combat,
                onComplete: null,
                registry: registry.EffectNodeExecutors,
                onTerminal: (state, c) =>
                {
                    placeCard?.Invoke(state, c);
                    c.EndCardPlayScope();
                });
        }
        else if (cardInstanceId is not null)
        {
            combat.EnqueueEffect(new MoveCardToZoneEffectRequest(
                source.Id,
                cardInstanceId.Value,
                card.PlayedCardDestinationZone));
        }
    }

    internal static IReadOnlyList<CalculatedResourceCost> CalculateCostsInternal(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CardDefinition card,
        CombatantState source,
        CombatantId? targetId,
        CardInstanceId? cardInstanceId) =>
        // Affordability pre-check (PlayCardEffectRequest path). Does not emit the cost-derivation
        // trace — that is owned by the actual payment path so a played card derives its cost once.
        CalculateCosts(combat, registry, card, source, targetId, cardInstanceId, trace: false); private static void AddCardCostPaidLogAndEvent(
        CombatState combat,
        CombatantId sourceCombatantId,
        CardDefinitionId cardDefinitionId,
        CardInstanceId? cardInstanceId,
        IReadOnlyCollection<CalculatedResourceCost> costsToPay)
    {
        if (costsToPay.Count == 0)
            return;

        var costSummary = string.Join(
            ", ",
            costsToPay.Select(cost => $"{cost.Amount} {cost.ResourceId}"));

        combat.AddLogEntry(
            StandardCombatLogTypes.CardCostPaid,
            $"Combatant '{sourceCombatantId}' paid {costSummary} for card '{cardDefinitionId}'.");

        combat.EnqueueEvent(new CardCostPaidCombatEvent(
            SourceCombatantId: sourceCombatantId,
            CardDefinitionId: cardDefinitionId,
            CardInstanceId: cardInstanceId,
            Costs: costsToPay.ToArray()));
    }


    private static void ValidateCardPlay(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CardDefinition card,
        CombatantState source,
        CombatantId? requestedTargetId,
        CardInstanceId? cardInstanceId)
    {
        var context = new CardPlayValidationContext(
            Combat: combat,
            Registry: registry,
            Card: card,
            Source: source,
            RequestedTargetId: requestedTargetId,
            CardInstanceId: cardInstanceId);

        foreach (var validator in registry.GetCardPlayValidators())
            validator.Validate(context);
    }

    private static TriggeredEffectActionBuildContext CreateCardPlayBuildContext(
        CombatState combat,
        CardDefinitionId cardDefinitionId,
        CombatantState source,
        CombatantId? targetCombatantId)
    {
        return new TriggeredEffectActionBuildContext(
            new CombatantTargetSelectionContext(
                Combat: combat,
                Source: source,
                EventTargetId: targetCombatantId),
            new TriggeredEffectActionSource(
                SourceCombatantId: source.Id,
                SourceCardId: cardDefinitionId));
    }

    private static List<IEffectRequest> BuildEffectRequests(
        CombatState combat,
        CardDefinition card,
        CombatantState source,
        CombatantId? requestedTargetId)
    {
        var requests = new List<IEffectRequest>();
        var context = new CardPlayContext(card);
        var buildContext = CreateCardPlayBuildContext(combat, card.Id, source, requestedTargetId);

        foreach (var recipe in card.Effects)
            requests.AddRange(recipe.BuildEffectRequests(context, buildContext));

        return requests;
    }

    private static IReadOnlyList<CalculatedResourceCost> CalculateCosts(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CardDefinition card,
        CombatantState source,
        CombatantId? requestedTargetId,
        CardInstanceId? cardInstanceId,
        bool trace)
    {
        var totalCosts = new Dictionary<ResourceId, int>();

        foreach (var cost in card.Costs)
        {
            if (cost.Amount < 0)
            {
                throw new InvalidOperationException(
                    $"Card '{card.Id}' has a negative cost for resource '{cost.ResourceId}'.");
            }

            var context = new CardCostModificationContext(
                Combat: combat,
                Registry: registry,
                Card: card,
                Source: source,
                Cost: cost,
                RequestedTargetId: requestedTargetId,
                CardInstanceId: cardInstanceId);

            var tracing = trace && combat.TraceListener is not null;
            var steps = tracing ? new List<CardCostModifierStepTrace>() : null;

            var modifiedAmount = cost.Amount;

            foreach (var modifier in registry.GetCardCostModifiers())
            {
                var before = modifiedAmount;
                var after = Math.Max(0, modifier.ModifyCostAmount(context, before));
                if (tracing && after != before)
                    steps!.Add(new CardCostModifierStepTrace(modifier.ModifierId, before, after));
                modifiedAmount = after;
            }

            if (tracing)
                combat.Trace(new CardCostResolvedTraceEvent(
                    combat.CurrentRound, combat.CurrentTurn,
                    card.Id, cost.ResourceId,
                    BaseAmount: cost.Amount,
                    ModifierSteps: steps!,
                    FinalAmount: modifiedAmount));

            if (modifiedAmount == 0)
                continue;

            if (!totalCosts.TryAdd(cost.ResourceId, modifiedAmount))
                totalCosts[cost.ResourceId] = checked(totalCosts[cost.ResourceId] + modifiedAmount);
        }

        return totalCosts
            .Select(pair => new CalculatedResourceCost(pair.Key, pair.Value))
            .ToArray();
    }

    private static void EnsureCostsCanBePaid(
        CombatantState source,
        CardDefinition card,
        IReadOnlyCollection<CalculatedResourceCost> costsToPay)
    {
        foreach (var cost in costsToPay)
        {
            if (!source.Resources.TryGetValue(cost.ResourceId, out var resource))
            {
                throw new InvalidOperationException(
                    $"Combatant '{source.Id}' does not have resource '{cost.ResourceId}'.");
            }

            if (resource.Current < cost.Amount)
            {
                throw new InvalidOperationException(
                    $"Combatant '{source.Id}' cannot pay {cost.Amount} '{cost.ResourceId}' for card '{card.Id}'.");
            }
        }
    }

    private static void PayCosts(
        CombatantState source,
        IReadOnlyCollection<CalculatedResourceCost> costsToPay)
    {
        foreach (var cost in costsToPay)
        {
            var resource = source.Resources[cost.ResourceId];
            resource.SetCurrent(resource.Current - cost.Amount);
        }
    }
}

