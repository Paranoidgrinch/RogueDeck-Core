namespace RogueDeck.Core.Combat;

public sealed class SkillCostReductionCostModifier : ICardCostModifier
{
    public string ModifierId => "standard.skill_cost_reduction_cost";
    public int Priority => 200;

    public int ModifyCostAmount(
        CardCostModificationContext context,
        int currentAmount)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (currentAmount <= 0)
            return currentAmount;

        if (!context.Card.Tags.Contains(StandardCombatIds.SkillCardTag))
            return currentAmount;

        var reductionAmount = context.Source.Statuses
            .Where(status => status.DefinitionId == StandardCombatIds.SkillCostReductionStatus)
            .Sum(status => status.Stacks);

        if (reductionAmount <= 0)
            return currentAmount;

        return currentAmount - reductionAmount;
    }
}
