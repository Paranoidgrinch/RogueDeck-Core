namespace RogueDeck.Core.Combat;

public sealed record CardCostModificationContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    CardDefinition Card,
    CombatantState Source,
    ResourceCost Cost,
    CombatantId? RequestedTargetId,
    CardInstanceId? CardInstanceId);

public sealed record CalculatedResourceCost(
    ResourceId ResourceId,
    int Amount);

public interface ICardCostModifier
{
    string ModifierId { get; }

    int Priority { get; }

    int ModifyCostAmount(
        CardCostModificationContext context,
        int currentAmount);
}
