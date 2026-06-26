namespace RogueDeck.Core.Combat;

public sealed class FreeNextCardCostModifier : ICardCostModifier
{
    public string ModifierId => "standard.free_next_card_cost";
    public int Priority => 100;

    public int ModifyCostAmount(
        CardCostModificationContext context,
        int currentAmount)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (currentAmount <= 0)
            return currentAmount;

        var hasFreeNextCardStatus = context.Source.Statuses
            .Any(status =>
                status.DefinitionId == StandardCombatIds.FreeNextCardStatus &&
                status.Charges > 0);

        if (!hasFreeNextCardStatus)
            return currentAmount;

        return 0;
    }
}

public sealed class ConsumeFreeNextCardOnCardPlayedHandler
    : CombatEventHandler<CardPlayedCombatEvent>
{
    protected override void Handle(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CardPlayedCombatEvent combatEvent)
    {
        if (!combat.TryGetCombatant(combatEvent.SourceCombatantId, out var source))
            return;

        var statusToConsume = source!.Statuses.FirstOrDefault(status =>
            status.DefinitionId == StandardCombatIds.FreeNextCardStatus &&
            status.Charges > 0);

        if (statusToConsume is null)
            return;

        combat.EnqueueEffect(new DecreaseStatusChargesEffectRequest(
            TargetCombatantId: combatEvent.SourceCombatantId,
            StatusInstanceId: statusToConsume.Id));
    }
}
