namespace RogueDeck.Core.Combat;

public sealed record StatusesRemovedByPolarityTriggeredEffectContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    StatusesRemovedByPolarityCombatEvent CombatEvent,
    CombatantState TargetCombatant);

public sealed record FixedStatusesRemovedByPolarityTriggeredEffectAmount(int Amount)
    : ICombatValueProvider<StatusesRemovedByPolarityTriggeredEffectContext, int>
{
    public int Resolve(StatusesRemovedByPolarityTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Amount;
    }
}

public sealed class StatusesRemovedByPolarityRemovedStatusCount
    : ICombatValueProvider<StatusesRemovedByPolarityTriggeredEffectContext, int>
{
    public int Resolve(StatusesRemovedByPolarityTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.StatusInstanceIds.Count;
    }
}

public sealed record StatusesRemovedByPolarityTriggerFilter(StatusPolarity Polarity)
    : ITriggeredProgramFilter<StatusesRemovedByPolarityTriggeredEffectContext>
{
    public bool Matches(StatusesRemovedByPolarityTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.Polarity == Polarity;
    }
}

public sealed record StatusesRemovedByPolarityTargetTriggerFilter(CombatantId TargetCombatantId)
    : ITriggeredProgramFilter<StatusesRemovedByPolarityTriggeredEffectContext>
{
    public bool Matches(StatusesRemovedByPolarityTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.TargetCombatantId == TargetCombatantId;
    }
}

public sealed record StatusesRemovedByPolarityMinimumCountTriggerFilter(int MinimumCount)
    : ITriggeredProgramFilter<StatusesRemovedByPolarityTriggeredEffectContext>
{
    public bool Matches(StatusesRemovedByPolarityTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.StatusInstanceIds.Count >= MinimumCount;
    }
}

public static class StatusesRemovedByPolarityTriggeredEffectTargetResolver
{
    public static CombatantTargetSelectionContext CreateSelectionContext(
        StatusesRemovedByPolarityTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new CombatantTargetSelectionContext(
            Combat: context.Combat,
            Source: context.TargetCombatant,
            EventTargetId: context.CombatEvent.TargetCombatantId);
    }

    public static TriggeredEffectActionSource CreateActionSource(
        StatusesRemovedByPolarityTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return TriggeredEffectActionSource.None;
    }

    public static TriggeredEffectActionBuildContext CreateActionBuildContext(
        StatusesRemovedByPolarityTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TriggeredEffectActionBuildContext(
            TargetSelectionContext: CreateSelectionContext(context),
            Source: CreateActionSource(context));
    }
}
