namespace RogueDeck.Core.Combat;

public sealed record StatusApplicationBlockedTriggeredEffectContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    StatusApplicationBlockedCombatEvent CombatEvent,
    CombatantState TargetCombatant);

public sealed record FixedStatusApplicationBlockedTriggeredEffectAmount(int Amount)
    : ICombatValueProvider<StatusApplicationBlockedTriggeredEffectContext, int>
{
    public int Resolve(StatusApplicationBlockedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Amount;
    }
}

public sealed record StatusApplicationBlockedBlockedStatusTriggerFilter(
    StatusDefinitionId BlockedStatusDefinitionId)
    : ITriggeredProgramFilter<StatusApplicationBlockedTriggeredEffectContext>
{
    public bool Matches(StatusApplicationBlockedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.BlockedStatusDefinitionId == BlockedStatusDefinitionId;
    }
}

public sealed record StatusApplicationBlockedBlockingStatusTriggerFilter(
    StatusDefinitionId BlockingStatusDefinitionId)
    : ITriggeredProgramFilter<StatusApplicationBlockedTriggeredEffectContext>
{
    public bool Matches(StatusApplicationBlockedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.BlockingStatusDefinitionId == BlockingStatusDefinitionId;
    }
}

public static class StatusApplicationBlockedTriggeredEffectTargetResolver
{
    public static CombatantTargetSelectionContext CreateSelectionContext(
        StatusApplicationBlockedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new CombatantTargetSelectionContext(
            Combat: context.Combat,
            Source: context.TargetCombatant,
            EventTargetId: context.CombatEvent.TargetCombatantId);
    }

    public static TriggeredEffectActionSource CreateActionSource(
        StatusApplicationBlockedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TriggeredEffectActionSource(
            SourceCombatantId: context.CombatEvent.TargetCombatantId);
    }

    public static TriggeredEffectActionBuildContext CreateActionBuildContext(
        StatusApplicationBlockedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TriggeredEffectActionBuildContext(
            TargetSelectionContext: CreateSelectionContext(context),
            Source: CreateActionSource(context));
    }
}
