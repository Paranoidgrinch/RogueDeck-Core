namespace RogueDeck.Core.Combat;

// Programmatically play a card through the full card-play pipeline from within an effect program.
// This satisfies §11.9 item 14: "programmatically play a card through the correct pipeline".
//
// The handler validates the play, pays costs, fires all events, and enqueues the card's effects
// and program into the existing queue. It does NOT call ResolvePendingQueues — the outer queue
// processor that is already running handles the newly enqueued items.
//
// WasPlayed=false (no-op, no exception) when:
//   - the player combatant is not alive or not found
//   - the card instance is not in the player's hand
//   - the player cannot afford the card's costs
public sealed record PlayCardEffectRequest(
    CombatantId PlayerId,
    CardInstanceId CardInstanceId,
    CombatantId? TargetCombatantId = null,
    PlayCardOutcomeSlot? OutcomeSlot = null
) : IEffectRequest;

public sealed class PlayCardEffectHandler : EffectRequestHandler<PlayCardEffectRequest>
{
    protected override void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        PlayCardEffectRequest request)
    {
        if (!combat.TryGetCombatant(request.PlayerId, out var player) || !player!.IsAlive)
        {
            NoOp(request);
            return;
        }

        var zones = combat.GetCardZones(request.PlayerId);

        if (!zones.ContainsCard(request.CardInstanceId))
        {
            NoOp(request);
            return;
        }

        var cardInstance = zones.GetCard(request.CardInstanceId);

        if (cardInstance.Zone != CardZone.Hand)
        {
            NoOp(request);
            return;
        }

        var card = registry.GetCard(cardInstance.DefinitionId);

        // An unplayable card (a curse) is never played — no-op it like an unaffordable card rather than throwing, so
        // the run/playtest/auto paths that funnel through here just leave it in hand.
        if (card.Tags.Contains(StandardCombatIds.UnplayableTag))
        {
            NoOp(request);
            return;
        }

        // …and neither is a card a rule forbids right now. Stun is the plain case — "you lose this turn" is
        // only true if the turn's cards actually stay in hand — and this is the path the host, the playtest
        // walker and Godot all play through, so a validator that is not asked here is a validator that does
        // not exist. Refusal no-ops like an unaffordable cost rather than throwing.
        if (!CombatCardPlayProcessor.IsCardPlayAllowed(
                combat, registry, card, player, request.TargetCombatantId, request.CardInstanceId))
        {
            NoOp(request);
            return;
        }

        var costs = CombatCardPlayProcessor.CalculateCostsInternal(
            combat, registry, card, player, request.TargetCombatantId, request.CardInstanceId);

        foreach (var cost in costs)
        {
            if (!player.Resources.TryGetValue(cost.ResourceId, out var resource) ||
                resource.Current < cost.Amount)
            {
                NoOp(request);
                return;
            }
        }

        CombatCardPlayProcessor.EnqueueCardPlayEffects(
            combat, registry, card, player, request.TargetCombatantId, request.CardInstanceId);

        if (request.OutcomeSlot is { } slot)
            slot.Value = new PlayCardOutcome(request.CardInstanceId, WasPlayed: true);

        static void NoOp(PlayCardEffectRequest req)
        {
            if (req.OutcomeSlot is { } s)
                s.Value = new PlayCardOutcome(req.CardInstanceId, WasPlayed: false);
        }
    }
}
