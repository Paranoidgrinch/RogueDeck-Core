namespace RogueDeck.Core.Combat;

// An ACTION has finished: one card play, or one enemy action, with everything it set in motion behind it.
//
// The event exists for rules that judge an action by what it turned out to be rather than by what it was
// aimed at — the Bureaucrat pool's Citation ("after the holder resolves a non-damaging action, it loses X
// HP"). Whether the action was damaging is decided the way the design words it: at least one ordinary hit
// landed on the other side, whether or not Block soaked it. Utility, guarding, healing and summoning are not
// damage; nor is a status ticking, which happens outside any action.
//
// One action raises this once, however many effects it contained.
public sealed record ActionResolvedCombatEvent(
    CombatantId ActorCombatantId,
    bool DealtDamage
) : ICombatEvent;

public sealed record ActionResolvedTriggeredEffectContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    ActionResolvedCombatEvent CombatEvent,
    CombatantState ActorCombatant);

public sealed record ActionResolvedActorHasStatusTriggerFilter(StatusDefinitionId StatusDefinitionId)
    : ITriggeredProgramFilter<ActionResolvedTriggeredEffectContext>
{
    public bool Matches(ActionResolvedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.ActorCombatant.Statuses.Any(status => status.DefinitionId == StatusDefinitionId);
    }
}

public static class ActionResolvedTriggeredEffectTargetResolver
{
    public static TriggeredEffectActionBuildContext CreateActionBuildContext(
        ActionResolvedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TriggeredEffectActionBuildContext(
            new CombatantTargetSelectionContext(
                context.Combat, context.ActorCombatant, context.CombatEvent.ActorCombatantId),
            new TriggeredEffectActionSource(context.CombatEvent.ActorCombatantId));
    }
}
