namespace RogueDeck.Core.Combat;

// Distinguishes which side of the combat encounter a damage modifier applies on.
// The handler applies modifiers in stage order: Source → Target → Global.
// This mirrors the semantic contract: source-side rules (Strength, Weak) resolve
// before target-side rules (Vulnerable), which resolve before global rules.
public enum DamageModifierStage
{
    Source = 0,
    Target = 1,
    Global = 2
}

public sealed record DamageAmountModificationContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    CombatantState TargetCombatant,
    CombatantState? SourceCombatant,
    CardDefinitionId? SourceCardId,
    DamageKind Kind,
    int RequestedAmount);

public interface IDamageAmountModifier
{
    // Stable ID used as secondary sort key when two modifiers share the same Priority.
    // Must be unique across all registered damage modifiers.
    string ModifierId { get; }

    int Priority { get; }

    DamageModifierStage Stage { get; }

    int ModifyDamageAmount(
        DamageAmountModificationContext context,
        int currentAmount);
}

// Strength, Weak, and Vulnerable were bespoke IDamageAmountModifier classes; they are now declarative
// PassiveModifierSpec entries on their status definitions (see PassiveModifiers.cs and
// StandardCombatPackage). This interface + context remain as the escape hatch for modifiers whose
// magnitude can't be expressed declaratively.
