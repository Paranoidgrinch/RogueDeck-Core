namespace RogueDeck.Core.Combat;

// StatusRemoved

public sealed record StatusRemovedTriggeredEffectContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    StatusRemovedCombatEvent CombatEvent,
    CombatantState TargetCombatant,
    CombatantState? SourceCombatant);

public sealed record FixedStatusRemovedTriggeredEffectAmount(int Amount)
    : ICombatValueProvider<StatusRemovedTriggeredEffectContext, int>
{
    public int Resolve(StatusRemovedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Amount;
    }
}

public sealed class StatusRemovedCountAmount
    : ICombatValueProvider<StatusRemovedTriggeredEffectContext, int>
{
    public int Resolve(StatusRemovedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.StatusInstanceIds.Count;
    }
}

public sealed record StatusRemovedTargetStatusStacksAmount(StatusDefinitionId StatusDefinitionId)
    : ICombatValueProvider<StatusRemovedTriggeredEffectContext, int>
{
    public int Resolve(StatusRemovedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.TargetCombatant.Statuses
            .Where(status => status.DefinitionId == StatusDefinitionId)
            .Sum(status => status.Stacks);
    }
}

public sealed record StatusRemovedSourceStatusStacksAmount(StatusDefinitionId StatusDefinitionId)
    : ICombatValueProvider<StatusRemovedTriggeredEffectContext, int>
{
    public int Resolve(StatusRemovedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.SourceCombatant is null)
            return 0;

        return context.SourceCombatant.Statuses
            .Where(status => status.DefinitionId == StatusDefinitionId)
            .Sum(status => status.Stacks);
    }
}

public sealed class StatusRemovedHasSourceTriggerFilter
    : ITriggeredProgramFilter<StatusRemovedTriggeredEffectContext>
{
    public bool Matches(StatusRemovedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.SourceCombatant is not null;
    }
}

public sealed record StatusRemovedStatusDefinitionTriggerFilter(StatusDefinitionId StatusDefinitionId)
    : ITriggeredProgramFilter<StatusRemovedTriggeredEffectContext>
{
    public bool Matches(StatusRemovedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.StatusDefinitionId == StatusDefinitionId;
    }
}

public sealed record StatusRemovedTargetHasStatusTriggerFilter(StatusDefinitionId StatusDefinitionId)
    : ITriggeredProgramFilter<StatusRemovedTriggeredEffectContext>
{
    public bool Matches(StatusRemovedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.TargetCombatant.Statuses.Any(status => status.DefinitionId == StatusDefinitionId);
    }
}

public sealed record StatusRemovedSourceHasStatusTriggerFilter(StatusDefinitionId StatusDefinitionId)
    : ITriggeredProgramFilter<StatusRemovedTriggeredEffectContext>
{
    public bool Matches(StatusRemovedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.SourceCombatant is not null &&
            context.SourceCombatant.Statuses.Any(status => status.DefinitionId == StatusDefinitionId);
    }
}

public static class StatusRemovedTriggeredEffectTargetResolver
{
    public static CombatantTargetSelectionContext CreateSelectionContext(
        StatusRemovedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new CombatantTargetSelectionContext(
            Combat: context.Combat,
            Source: context.SourceCombatant,
            EventTargetId: context.CombatEvent.TargetCombatantId);
    }

    public static TriggeredEffectActionSource CreateActionSource(
        StatusRemovedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TriggeredEffectActionSource();
    }

    public static TriggeredEffectActionBuildContext CreateActionBuildContext(
        StatusRemovedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TriggeredEffectActionBuildContext(
            TargetSelectionContext: CreateSelectionContext(context),
            Source: CreateActionSource(context));
    }
}

// StatusChargesReduced

public sealed record StatusChargesReducedTriggeredEffectContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    StatusChargesReducedCombatEvent CombatEvent,
    CombatantState TargetCombatant,
    CombatantState? SourceCombatant);

public sealed record FixedStatusChargesReducedTriggeredEffectAmount(int Amount)
    : ICombatValueProvider<StatusChargesReducedTriggeredEffectContext, int>
{
    public int Resolve(StatusChargesReducedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Amount;
    }
}

public sealed class StatusChargesReducedChargeCountAmount
    : ICombatValueProvider<StatusChargesReducedTriggeredEffectContext, int>
{
    public int Resolve(StatusChargesReducedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Math.Max(0, context.CombatEvent.OldCharges - context.CombatEvent.NewCharges);
    }
}

public sealed record StatusChargesReducedStatusDefinitionTriggerFilter(StatusDefinitionId StatusDefinitionId)
    : ITriggeredProgramFilter<StatusChargesReducedTriggeredEffectContext>
{
    public bool Matches(StatusChargesReducedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.StatusDefinitionId == StatusDefinitionId;
    }
}

public static class StatusChargesReducedTriggeredEffectTargetResolver
{
    public static CombatantTargetSelectionContext CreateSelectionContext(
        StatusChargesReducedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new CombatantTargetSelectionContext(
            Combat: context.Combat,
            Source: context.SourceCombatant,
            EventTargetId: context.CombatEvent.TargetCombatantId);
    }

    public static TriggeredEffectActionSource CreateActionSource(
        StatusChargesReducedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TriggeredEffectActionSource();
    }

    public static TriggeredEffectActionBuildContext CreateActionBuildContext(
        StatusChargesReducedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TriggeredEffectActionBuildContext(
            TargetSelectionContext: CreateSelectionContext(context),
            Source: CreateActionSource(context));
    }
}

// StatusExpired

public sealed record StatusExpiredTriggeredEffectContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    StatusExpiredCombatEvent CombatEvent,
    CombatantState TargetCombatant,
    CombatantState? SourceCombatant);

public sealed record FixedStatusExpiredTriggeredEffectAmount(int Amount)
    : ICombatValueProvider<StatusExpiredTriggeredEffectContext, int>
{
    public int Resolve(StatusExpiredTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Amount;
    }
}

public sealed record StatusExpiredTargetStatusStacksAmount(StatusDefinitionId StatusDefinitionId)
    : ICombatValueProvider<StatusExpiredTriggeredEffectContext, int>
{
    public int Resolve(StatusExpiredTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.TargetCombatant.Statuses
            .Where(status => status.DefinitionId == StatusDefinitionId)
            .Sum(status => status.Stacks);
    }
}

public sealed record StatusExpiredSourceStatusStacksAmount(StatusDefinitionId StatusDefinitionId)
    : ICombatValueProvider<StatusExpiredTriggeredEffectContext, int>
{
    public int Resolve(StatusExpiredTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.SourceCombatant is null)
            return 0;

        return context.SourceCombatant.Statuses
            .Where(status => status.DefinitionId == StatusDefinitionId)
            .Sum(status => status.Stacks);
    }
}

public sealed class StatusExpiredHasSourceTriggerFilter
    : ITriggeredProgramFilter<StatusExpiredTriggeredEffectContext>
{
    public bool Matches(StatusExpiredTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.SourceCombatant is not null;
    }
}

public sealed record StatusExpiredStatusDefinitionTriggerFilter(StatusDefinitionId StatusDefinitionId)
    : ITriggeredProgramFilter<StatusExpiredTriggeredEffectContext>
{
    public bool Matches(StatusExpiredTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.StatusDefinitionId == StatusDefinitionId;
    }
}

public sealed record StatusExpiredTargetHasStatusTriggerFilter(StatusDefinitionId StatusDefinitionId)
    : ITriggeredProgramFilter<StatusExpiredTriggeredEffectContext>
{
    public bool Matches(StatusExpiredTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.TargetCombatant.Statuses.Any(status => status.DefinitionId == StatusDefinitionId);
    }
}

public sealed record StatusExpiredSourceHasStatusTriggerFilter(StatusDefinitionId StatusDefinitionId)
    : ITriggeredProgramFilter<StatusExpiredTriggeredEffectContext>
{
    public bool Matches(StatusExpiredTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.SourceCombatant is not null &&
            context.SourceCombatant.Statuses.Any(status => status.DefinitionId == StatusDefinitionId);
    }
}

public static class StatusExpiredTriggeredEffectTargetResolver
{
    public static CombatantTargetSelectionContext CreateSelectionContext(
        StatusExpiredTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new CombatantTargetSelectionContext(
            Combat: context.Combat,
            Source: context.SourceCombatant,
            EventTargetId: context.CombatEvent.TargetCombatantId);
    }

    public static TriggeredEffectActionSource CreateActionSource(
        StatusExpiredTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TriggeredEffectActionSource();
    }

    public static TriggeredEffectActionBuildContext CreateActionBuildContext(
        StatusExpiredTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TriggeredEffectActionBuildContext(
            TargetSelectionContext: CreateSelectionContext(context),
            Source: CreateActionSource(context));
    }
}

// StatusMerged

public sealed record StatusMergedTriggeredEffectContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    StatusMergedCombatEvent CombatEvent,
    CombatantState TargetCombatant,
    CombatantState? SourceCombatant);

public sealed record FixedStatusMergedTriggeredEffectAmount(int Amount)
    : ICombatValueProvider<StatusMergedTriggeredEffectContext, int>
{
    public int Resolve(StatusMergedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Amount;
    }
}

public sealed class StatusMergedStacksAmount
    : ICombatValueProvider<StatusMergedTriggeredEffectContext, int>
{
    public int Resolve(StatusMergedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.Stacks;
    }
}

public sealed class StatusMergedDurationTurnsAmount
    : ICombatValueProvider<StatusMergedTriggeredEffectContext, int>
{
    public int Resolve(StatusMergedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.DurationTurns;
    }
}

public sealed class StatusMergedChargesAmount
    : ICombatValueProvider<StatusMergedTriggeredEffectContext, int>
{
    public int Resolve(StatusMergedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.Charges;
    }
}

public sealed record StatusMergedTargetStatusStacksAmount(StatusDefinitionId StatusDefinitionId)
    : ICombatValueProvider<StatusMergedTriggeredEffectContext, int>
{
    public int Resolve(StatusMergedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.TargetCombatant.Statuses
            .Where(status => status.DefinitionId == StatusDefinitionId)
            .Sum(status => status.Stacks);
    }
}

public sealed record StatusMergedSourceStatusStacksAmount(StatusDefinitionId StatusDefinitionId)
    : ICombatValueProvider<StatusMergedTriggeredEffectContext, int>
{
    public int Resolve(StatusMergedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.SourceCombatant is null)
            return 0;

        return context.SourceCombatant.Statuses
            .Where(status => status.DefinitionId == StatusDefinitionId)
            .Sum(status => status.Stacks);
    }
}

public sealed class StatusMergedHasSourceTriggerFilter
    : ITriggeredProgramFilter<StatusMergedTriggeredEffectContext>
{
    public bool Matches(StatusMergedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.SourceCombatant is not null;
    }
}

public sealed record StatusMergedStatusDefinitionTriggerFilter(StatusDefinitionId StatusDefinitionId)
    : ITriggeredProgramFilter<StatusMergedTriggeredEffectContext>
{
    public bool Matches(StatusMergedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.StatusDefinitionId == StatusDefinitionId;
    }
}

public sealed record StatusMergedTargetHasStatusTriggerFilter(StatusDefinitionId StatusDefinitionId)
    : ITriggeredProgramFilter<StatusMergedTriggeredEffectContext>
{
    public bool Matches(StatusMergedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.TargetCombatant.Statuses.Any(status => status.DefinitionId == StatusDefinitionId);
    }
}

public sealed record StatusMergedSourceHasStatusTriggerFilter(StatusDefinitionId StatusDefinitionId)
    : ITriggeredProgramFilter<StatusMergedTriggeredEffectContext>
{
    public bool Matches(StatusMergedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.SourceCombatant is not null &&
            context.SourceCombatant.Statuses.Any(status => status.DefinitionId == StatusDefinitionId);
    }
}

public static class StatusMergedTriggeredEffectTargetResolver
{
    public static CombatantTargetSelectionContext CreateSelectionContext(
        StatusMergedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new CombatantTargetSelectionContext(
            Combat: context.Combat,
            Source: context.SourceCombatant,
            EventTargetId: context.CombatEvent.TargetCombatantId);
    }

    public static TriggeredEffectActionSource CreateActionSource(
        StatusMergedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TriggeredEffectActionSource(
            SourceCombatantId: context.CombatEvent.SourceCombatantId,
            SourceCardId: context.CombatEvent.SourceCardId);
    }

    public static TriggeredEffectActionBuildContext CreateActionBuildContext(
        StatusMergedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TriggeredEffectActionBuildContext(
            TargetSelectionContext: CreateSelectionContext(context),
            Source: CreateActionSource(context));
    }
}
