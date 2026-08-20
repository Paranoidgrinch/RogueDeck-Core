namespace RogueDeck.Core.Combat;

// Resolving the Queue: running the cards that were PLAYED earlier and have been waiting.
//
// A queued card was already played — its cost was paid, its target was locked, and everything that watches
// card plays already saw it. What is left is its effect, and that happens here, oldest first. Resolution is
// deliberately NOT a second play: no cost, no CardPlayed event, no play validators. The card then goes to the
// destination its definition names, exactly as it would have on an ordinary play.
//
// Cards are resolved one after another rather than all at once: the next one starts only when the previous
// one's program has finished, so "oldest first" holds even when a card's effect is several steps deep. The
// set to resolve is snapshotted before the first one runs, which is what makes a card queued DURING a
// resolution window wait for the next window instead of joining this one.
public static class QueueResolution
{
    // Resolve the oldest `count` cards in a combatant's Queue (int.MaxValue = the whole Queue, which is what
    // the turn-start window does).
    public static void ResolveOldest(
        CombatState combat, CombatDefinitionRegistry registry, CombatantId ownerId, int count)
    {
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(registry);

        if (count <= 0 || !combat.TryGetCombatant(ownerId, out _))
            return;

        var waiting = combat.GetCardZones(ownerId).Queue.Take(count).Select(card => card.Id).ToList();
        if (waiting.Count == 0)
            return;

        ResolveFrom(combat, registry, ownerId, waiting, 0);
    }

    private static void ResolveFrom(
        CombatState combat, CombatDefinitionRegistry registry, CombatantId ownerId,
        IReadOnlyList<CardInstanceId> waiting, int index)
    {
        while (index < waiting.Count)
        {
            var cardId = waiting[index];
            var zones = combat.GetCardZones(ownerId);

            // Something may have taken the card out of the Queue since the snapshot; skip it.
            if (!zones.ContainsCard(cardId) || zones.GetCard(cardId).Zone != CardZone.QueuePile)
            {
                index++;
                continue;
            }

            var instance = zones.GetCard(cardId);
            if (!registry.CardDefinitions.TryGetValue(instance.DefinitionId, out var card) ||
                !combat.TryGetCombatant(ownerId, out var owner) || owner is null)
            {
                index++;
                continue;
            }

            // The target was locked when the card was queued. If that combatant has left the fight the
            // target-bound parts of the card fizzle — there is deliberately no retargeting.
            var target = instance.QueuedTargetId is { } locked && combat.TryGetCombatant(locked, out _)
                ? locked
                : (CombatantId?)null;

            combat.AddLogEntry(
                StandardCombatLogTypes.CardMovedToZone,
                $"Queued card '{cardId}' resolves for '{ownerId}'.");

            if (card.Program is not { } program)
            {
                Finish(combat, ownerId, cardId, card);
                index++;
                continue;
            }

            var next = index + 1;
            var buildContext = CombatCardPlayProcessor.CreateCardPlayBuildContext(combat, card.Id, owner, target);

            // A resolution is its own action, like the play that queued it: a once-per-action rule that fires
            // inside it must not be sharing a ledger with whatever ran before.
            combat.BeginActionScope();
            EffectProgramExecutor.Execute(
                program,
                new EffectExecutionContext<CardPlayContext>(new CardPlayContext(card, cardId), buildContext),
                combat,
                onComplete: null,
                registry: registry.EffectNodeExecutors,
                onTerminal: (_, c) =>
                {
                    c.EndActionScope();
                    Finish(c, ownerId, cardId, card);
                    ResolveFrom(c, registry, ownerId, waiting, next);
                });
            return;
        }
    }

    // The card leaves the Queue for wherever its definition sends a played card, and forgets its lock.
    private static void Finish(
        CombatState combat, CombatantId ownerId, CardInstanceId cardId, CardDefinition card)
    {
        var zones = combat.GetCardZones(ownerId);
        if (!zones.ContainsCard(cardId) || zones.GetCard(cardId).Zone != CardZone.QueuePile)
            return;

        zones.GetCard(cardId).SetQueuedTarget(null);
        combat.EnqueueEffect(new MoveCardToZoneEffectRequest(ownerId, cardId, card.PlayedCardDestinationZone));
    }
}

// The Queue's ordinary resolution window: the owner's own turn start, AFTER the turn's triggers have run and
// BEFORE the draw, exactly as the design's turn order says.
public sealed class ResolveQueueOnTurnStartedHandler : CombatEventHandler<TurnStartedCombatEvent>
{
    protected override void Handle(
        CombatState combat, CombatDefinitionRegistry registry, TurnStartedCombatEvent combatEvent) =>
        QueueResolution.ResolveOldest(combat, registry, combatEvent.CombatantId, int.MaxValue);
}
