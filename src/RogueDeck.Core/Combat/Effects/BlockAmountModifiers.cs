namespace RogueDeck.Core.Combat;

public sealed record BlockAmountModificationContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    CombatantState TargetCombatant,
    CombatantState? SourceCombatant,
    CardDefinitionId? SourceCardId,
    int RequestedAmount);

public interface IBlockAmountModifier
{
    string ModifierId { get; }

    int Priority { get; }

    int ModifyBlockAmount(
        BlockAmountModificationContext context,
        int currentAmount);
}

// Dexterity and Frail were bespoke IBlockAmountModifier classes; they are now declarative
// PassiveModifierSpec entries on their status definitions (see PassiveModifiers.cs and
// StandardCombatPackage). This interface + context remain as the escape hatch.
