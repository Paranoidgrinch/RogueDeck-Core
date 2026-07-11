namespace RogueDeck.Core.Combat;

// A per-card triggered program keyed by a moment in the card's LIFECYCLE (while it sits in a zone), as opposed to
// its on-PLAY program. This is the base mechanic behind Burn / Decay / Regret — cards that do something while held,
// not when played. The card OWNS the behaviour (declared on its definition), rather than requiring a separate global
// triggered rule filtered to the card's id. Slice 1 wires the iconic TurnEndInHand trigger; the enum leaves room for
// Drawn / Discarded / Exhausted (each has an existing combat event to hang a handler on).
public enum CardLifecycleTrigger
{
    TurnEndInHand,
}

// The context a card's lifecycle program runs against: the card (its live definition + instance id) and its owner.
// The effect vocabulary is generic over the context, so a program can e.g. deal damage to the owner (Source).
public sealed record CardLifecycleContext(CardDefinition Card, CardInstanceId CardInstanceId, CombatantId OwnerId);

// Runs each hand card's TurnEndInHand lifecycle program when a combatant's turn ends — before the end-of-turn
// discard, which is deferred (enqueued), so the hand is intact here. The card is identified from the live zone; a
// program that moves the card is fine (we snapshot the hand first). No card ⇒ nothing runs, so ordinary cards are
// unaffected.
public sealed class CardLifecycleTurnEndInHandHandler : CombatEventHandler<TurnEndedCombatEvent>
{
    protected override void Handle(
        CombatState combat,
        CombatDefinitionRegistry registry,
        TurnEndedCombatEvent combatEvent)
    {
        if (!combat.TryGetCombatant(combatEvent.CombatantId, out var owner) || owner is null)
            return;

        foreach (var card in combat.GetCardZones(combatEvent.CombatantId).GetCardsInZone(CardZone.Hand))
        {
            if (!registry.TryGetCard(card.DefinitionId, out var definition) || definition is null)
                continue;
            if (!definition.LifecyclePrograms.TryGetValue(CardLifecycleTrigger.TurnEndInHand, out var program))
                continue;

            EffectProgramExecutor.Execute(
                program,
                new CardLifecycleContext(definition, card.Id, owner.Id),
                CardLifecycleTriggeredEffectSupport.CreateActionBuildContext(combat, owner),
                combat,
                registry: registry.EffectNodeExecutors);
        }
    }
}
