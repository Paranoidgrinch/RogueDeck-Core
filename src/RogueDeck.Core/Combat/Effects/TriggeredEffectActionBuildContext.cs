namespace RogueDeck.Core.Combat;

public sealed record TriggeredEffectActionBuildContext(
    CombatantTargetSelectionContext TargetSelectionContext,
    TriggeredEffectActionSource Source);
