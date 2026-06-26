namespace RogueDeck.Core.Combat;

// Context for programs that react to an enemy executing an action.
// Source = acting combatant; EventTarget = target combatant (if any).
public sealed record EnemyActionExecutedTriggeredEffectContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    EnemyActionExecutedCombatEvent CombatEvent,
    CombatantState ActorCombatant,
    CombatantState? TargetCombatant);

internal static class EnemyActionExecutedTargetResolver
{
    internal static TriggeredEffectActionBuildContext CreateActionBuildContext(
        EnemyActionExecutedTriggeredEffectContext context) =>
        new(
            new CombatantTargetSelectionContext(
                Combat: context.Combat,
                Source: context.ActorCombatant,
                EventTargetId: context.TargetCombatant?.Id),
            TriggeredEffectActionSource.FromEnemyAction(
                context.ActorCombatant.Id,
                context.CombatEvent.ActionId));
}
