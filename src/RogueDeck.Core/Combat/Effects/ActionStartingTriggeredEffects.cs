namespace RogueDeck.Core.Combat;

// An enemy is ABOUT to take its action: announced before anything the action does, so a rule may decide that it
// fails (the Hedge Witch's Misfortune: "roll once before the next action; on a success the whole action fails").
// A rule makes it fail by putting StandardCombatIds.ActionFailsStatus on the actor; the action then resolves to
// nothing — no hit, no Block, no status, no summon — and is still spent.
//
// Only an actor carrying a status that LISTENS for this is announced at all (ExecuteEnemyActionEffectHandler):
// every other action runs exactly as it always has, in the order it always has.
public sealed record ActionStartingCombatEvent(
    CombatantId ActorCombatantId,
    EnemyActionDefinitionId ActionId,
    CombatantId? TargetCombatantId
) : ICombatEvent;

public sealed record ActionStartingTriggeredEffectContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    ActionStartingCombatEvent CombatEvent,
    CombatantState ActorCombatant);

public sealed record ActionStartingActorHasStatusTriggerFilter(StatusDefinitionId StatusDefinitionId)
    : ITriggeredProgramFilter<ActionStartingTriggeredEffectContext>
{
    public bool Matches(ActionStartingTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.ActorCombatant.Statuses.Any(status => status.DefinitionId == StatusDefinitionId);
    }
}

public static class ActionStartingTriggeredEffectTargetResolver
{
    // Source = the actor (the bearer of the rule), event target = whom the action is aimed at.
    public static TriggeredEffectActionBuildContext CreateActionBuildContext(ActionStartingTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new TriggeredEffectActionBuildContext(
            new CombatantTargetSelectionContext(
                context.Combat, context.ActorCombatant, context.CombatEvent.TargetCombatantId),
            new TriggeredEffectActionSource(context.CombatEvent.ActorCombatantId));
    }
}
