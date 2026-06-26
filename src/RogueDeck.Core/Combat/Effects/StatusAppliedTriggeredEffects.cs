namespace RogueDeck.Core.Combat;

public sealed record StatusAppliedTriggeredEffectContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    StatusAppliedCombatEvent CombatEvent,
    CombatantState TargetCombatant,
    CombatantState? SourceCombatant);

public sealed record FixedStatusAppliedTriggeredEffectAmount(int Amount)
    : ICombatValueProvider<StatusAppliedTriggeredEffectContext, int>
{
    public int Resolve(StatusAppliedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Amount;
    }
}

public sealed class StatusAppliedStacksAmount
    : ICombatValueProvider<StatusAppliedTriggeredEffectContext, int>
{
    public int Resolve(StatusAppliedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.Stacks;
    }
}

public sealed class StatusAppliedDurationTurnsAmount
    : ICombatValueProvider<StatusAppliedTriggeredEffectContext, int>
{
    public int Resolve(StatusAppliedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.DurationTurns;
    }
}

public sealed class StatusAppliedChargesAmount
    : ICombatValueProvider<StatusAppliedTriggeredEffectContext, int>
{
    public int Resolve(StatusAppliedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.Charges;
    }
}

public sealed record StatusAppliedTargetStatusStacksAmount(StatusDefinitionId StatusDefinitionId)
    : ICombatValueProvider<StatusAppliedTriggeredEffectContext, int>
{
    public int Resolve(StatusAppliedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.TargetCombatant.Statuses
            .Where(status => status.DefinitionId == StatusDefinitionId)
            .Sum(status => status.Stacks);
    }
}

public sealed record StatusAppliedSourceStatusStacksAmount(StatusDefinitionId StatusDefinitionId)
    : ICombatValueProvider<StatusAppliedTriggeredEffectContext, int>
{
    public int Resolve(StatusAppliedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.SourceCombatant is null)
            return 0;

        return context.SourceCombatant.Statuses
            .Where(status => status.DefinitionId == StatusDefinitionId)
            .Sum(status => status.Stacks);
    }
}

public sealed class StatusAppliedHasSourceTriggerFilter
    : ITriggeredProgramFilter<StatusAppliedTriggeredEffectContext>
{
    public bool Matches(StatusAppliedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.SourceCombatant is not null;
    }
}

public sealed record StatusAppliedStatusDefinitionTriggerFilter(StatusDefinitionId StatusDefinitionId)
    : ITriggeredProgramFilter<StatusAppliedTriggeredEffectContext>
{
    public bool Matches(StatusAppliedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.StatusDefinitionId == StatusDefinitionId;
    }
}

public sealed record StatusAppliedExceptStatusDefinitionTriggerFilter(StatusDefinitionId ExcludedStatusDefinitionId)
    : ITriggeredProgramFilter<StatusAppliedTriggeredEffectContext>
{
    public bool Matches(StatusAppliedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.CombatEvent.StatusDefinitionId != ExcludedStatusDefinitionId;
    }
}

public sealed record StatusAppliedTargetHasStatusTriggerFilter(StatusDefinitionId StatusDefinitionId)
    : ITriggeredProgramFilter<StatusAppliedTriggeredEffectContext>
{
    public bool Matches(StatusAppliedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.TargetCombatant.Statuses.Any(status => status.DefinitionId == StatusDefinitionId);
    }
}

public sealed record StatusAppliedSourceHasStatusTriggerFilter(StatusDefinitionId StatusDefinitionId)
    : ITriggeredProgramFilter<StatusAppliedTriggeredEffectContext>
{
    public bool Matches(StatusAppliedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.SourceCombatant is not null &&
            context.SourceCombatant.Statuses.Any(status => status.DefinitionId == StatusDefinitionId);
    }
}

// Matches when the applied status's definition has the given polarity. The combat event carries only
// the status definition id, so the polarity is read from the registered definition. Composes
// "react whenever a buff/debuff is applied" (e.g. Resonance) without naming a specific status.
public sealed record StatusAppliedPolarityTriggerFilter(StatusPolarity Polarity)
    : ITriggeredProgramFilter<StatusAppliedTriggeredEffectContext>
{
    public bool Matches(StatusAppliedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Registry.TryGetStatus(context.CombatEvent.StatusDefinitionId, out var definition)
            && definition is not null
            && definition.Polarity == Polarity;
    }
}

public static class StatusAppliedTriggeredEffectTargetResolver
{
    public static CombatantTargetSelectionContext CreateSelectionContext(
        StatusAppliedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new CombatantTargetSelectionContext(
            Combat: context.Combat,
            Source: context.SourceCombatant ?? context.TargetCombatant,
            EventTargetId: context.CombatEvent.TargetCombatantId);
    }

    public static TriggeredEffectActionSource CreateActionSource(
        StatusAppliedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TriggeredEffectActionSource(
            SourceCombatantId: context.CombatEvent.SourceCombatantId,
            SourceCardId: context.CombatEvent.SourceCardId);
    }

    public static TriggeredEffectActionBuildContext CreateActionBuildContext(
        StatusAppliedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TriggeredEffectActionBuildContext(
            TargetSelectionContext: CreateSelectionContext(context),
            Source: CreateActionSource(context));
    }
}
