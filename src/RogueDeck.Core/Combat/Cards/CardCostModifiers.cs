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

    // Whether this modifier also prices a hero's ACTION (CardDefinition.IsAction — the Witch's pot, BREW). An
    // action is not a card played, so a rule about cards ("every card you play costs 1 more", "the third card is
    // free") leaves it alone; only a modifier that is ABOUT actions — one aimed at them by tag — opts in.
    bool PricesActions => false;
}
