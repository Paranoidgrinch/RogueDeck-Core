namespace RogueDeck.Core.Combat;

// A price written on ONE COPY of a card.
//
// Every other cost rule prices a card by what its owner is wearing, which is right for "your Deeds cost 1
// less" and useless for "the card you chose costs 1 less the first time you play it" — a promise made to one
// card, which has to travel with that card through the draw pile and the hand. Card instances already carry
// mark counters for exactly this kind of per-copy state; this reads the reserved one.
//
// The delta is added to each resource cost and clamped at zero, so a discount can make a card free but never
// pay the player. It is CONSUMED by the play (see CombatCardPlayProcessor), which is what makes it a one-shot
// price rather than a standing one.
public sealed class CardInstanceCostModifier : ICardCostModifier
{
    public string ModifierId => "standard.card_instance_cost";

    // After the declarative status modifiers, so a per-card promise is the last word on the price.
    public int Priority => 1100;

    public int ModifyCostAmount(CardCostModificationContext context, int currentAmount)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.CardInstanceId is not { } instanceId)
            return currentAmount;
        if (!context.Combat.CardZonesByCombatant.TryGetValue(context.Source.Id, out var zones))
            return currentAmount;
        if (!zones.ContainsCard(instanceId))
            return currentAmount;

        var delta = zones.GetCard(instanceId).GetMarkCounter(StandardCombatIds.CardCostDeltaCounter);
        return delta == 0 ? currentAmount : Math.Max(0, currentAmount + delta);
    }
}
