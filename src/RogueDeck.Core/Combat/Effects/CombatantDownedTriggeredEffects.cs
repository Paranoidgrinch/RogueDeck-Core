namespace RogueDeck.Core.Combat;

public sealed record CombatantDownedTriggeredEffectContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    CombatantLifecycleChangedCombatEvent CombatEvent,
    CombatantState DownedCombatant);

public sealed record FixedCombatantDownedTriggeredEffectAmount(int Amount)
    : ICombatValueProvider<CombatantDownedTriggeredEffectContext, int>
{
    public int Resolve(CombatantDownedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Amount;
    }
}

public sealed record CombatantDownedCombatantIdTriggerFilter(CombatantId CombatantId)
    : ITriggeredProgramFilter<CombatantDownedTriggeredEffectContext>
{
    public bool Matches(CombatantDownedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.CombatantId == CombatantId;
    }
}

// Fires only when the downed combatant still carries the given status — used to bind a "when the bearer
// dies" status trigger (on-death effects).
public sealed record CombatantDownedHasStatusTriggerFilter(StatusDefinitionId StatusDefinitionId)
    : ITriggeredProgramFilter<CombatantDownedTriggeredEffectContext>
{
    public bool Matches(CombatantDownedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.DownedCombatant.Statuses.Any(status => status.DefinitionId == StatusDefinitionId);
    }
}

public sealed record CombatantDownedTeamTriggerFilter(TeamId TeamId)
    : ITriggeredProgramFilter<CombatantDownedTriggeredEffectContext>
{
    public bool Matches(CombatantDownedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.DownedCombatant.TeamId == TeamId;
    }
}

public sealed record CombatantDownedOldStateTriggerFilter(CombatantLifecycleState OldState)
    : ITriggeredProgramFilter<CombatantDownedTriggeredEffectContext>
{
    public bool Matches(CombatantDownedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.OldState == OldState;
    }
}

public static class CombatantDownedTriggeredEffectTargetResolver
{
    public static CombatantTargetSelectionContext CreateSelectionContext(
        CombatantDownedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new CombatantTargetSelectionContext(
            Combat: context.Combat,
            Source: context.DownedCombatant,
            EventTargetId: context.CombatEvent.CombatantId);
    }

    public static TriggeredEffectActionSource CreateActionSource(
        CombatantDownedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return TriggeredEffectActionSource.None;
    }

    public static TriggeredEffectActionBuildContext CreateActionBuildContext(
        CombatantDownedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TriggeredEffectActionBuildContext(
            TargetSelectionContext: CreateSelectionContext(context),
            Source: CreateActionSource(context));
    }
}
