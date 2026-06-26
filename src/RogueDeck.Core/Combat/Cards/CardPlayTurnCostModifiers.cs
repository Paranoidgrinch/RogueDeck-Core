namespace RogueDeck.Core.Combat;

public sealed class FirstAttackEachTurnFreeCostModifier : ICardCostModifier
{
    public string ModifierId => "standard.first_attack_each_turn_free_cost";
    public int Priority => 150;

    public int ModifyCostAmount(
        CardCostModificationContext context,
        int currentAmount)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (currentAmount <= 0)
            return currentAmount;

        if (!context.Card.Tags.Contains(StandardCombatIds.AttackCardTag))
            return currentAmount;

        var hasFirstAttackFreeStatus = context.Source.Statuses.Any(status =>
            status.DefinitionId == StandardCombatIds.FirstAttackEachTurnFreeStatus);

        if (!hasFirstAttackFreeStatus)
            return currentAmount;

        var stats = context.Combat.GetCardPlayTurnStats(context.Source.Id);

        if (stats.GetCardsPlayedWithTagThisTurn(StandardCombatIds.AttackCardTag) > 0)
            return currentAmount;

        return 0;
    }
}
