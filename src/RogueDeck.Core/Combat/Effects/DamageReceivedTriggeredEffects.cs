namespace RogueDeck.Core.Combat;

public sealed record DamageReceivedTriggeredEffectContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    DamageReceivedCombatEvent CombatEvent,
    CombatantState ReceiverCombatant,
    CombatantState? SourceCombatant);

public sealed record FixedDamageReceivedTriggeredEffectAmount(int Amount)
    : ICombatValueProvider<DamageReceivedTriggeredEffectContext, int>
{
    public int Resolve(DamageReceivedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Amount;
    }
}

public sealed class DamageReceivedHealthDamageAmount
    : ICombatValueProvider<DamageReceivedTriggeredEffectContext, int>
{
    public int Resolve(DamageReceivedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.HealthDamage;
    }
}

public sealed class DamageReceivedBlockedDamageAmount
    : ICombatValueProvider<DamageReceivedTriggeredEffectContext, int>
{
    public int Resolve(DamageReceivedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.BlockedDamage;
    }
}

public sealed class DamageReceivedRequestedAmount
    : ICombatValueProvider<DamageReceivedTriggeredEffectContext, int>
{
    public int Resolve(DamageReceivedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.RequestedAmount;
    }
}

public sealed record DamageReceivedReceiverStatusStacksAmount(StatusDefinitionId StatusDefinitionId)
    : ICombatValueProvider<DamageReceivedTriggeredEffectContext, int>
{
    public int Resolve(DamageReceivedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.ReceiverCombatant.Statuses
            .Where(status => status.DefinitionId == StatusDefinitionId)
            .Sum(status => status.Stacks);
    }
}

public sealed record DamageReceivedSourceStatusStacksAmount(StatusDefinitionId StatusDefinitionId)
    : ICombatValueProvider<DamageReceivedTriggeredEffectContext, int>
{
    public int Resolve(DamageReceivedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.SourceCombatant is null)
            return 0;

        return context.SourceCombatant.Statuses
            .Where(status => status.DefinitionId == StatusDefinitionId)
            .Sum(status => status.Stacks);
    }
}

public sealed class DamageReceivedHasSourceTriggerFilter
    : ITriggeredProgramFilter<DamageReceivedTriggeredEffectContext>
{
    public bool Matches(DamageReceivedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.SourceCombatant is not null;
    }
}

public sealed record DamageReceivedDamageKindTriggerFilter(DamageKind Kind)
    : ITriggeredProgramFilter<DamageReceivedTriggeredEffectContext>
{
    public bool Matches(DamageReceivedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.Kind == Kind;
    }
}

public sealed record DamageReceivedMinimumHealthDamageTriggerFilter(int MinimumHealthDamage)
    : ITriggeredProgramFilter<DamageReceivedTriggeredEffectContext>
{
    public bool Matches(DamageReceivedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.HealthDamage >= MinimumHealthDamage;
    }
}

public sealed record DamageReceivedReceiverHasStatusTriggerFilter(StatusDefinitionId StatusDefinitionId)
    : ITriggeredProgramFilter<DamageReceivedTriggeredEffectContext>
{
    public bool Matches(DamageReceivedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.ReceiverCombatant.Statuses.Any(status => status.DefinitionId == StatusDefinitionId);
    }
}

public sealed record DamageReceivedSourceHasStatusTriggerFilter(StatusDefinitionId StatusDefinitionId)
    : ITriggeredProgramFilter<DamageReceivedTriggeredEffectContext>
{
    public bool Matches(DamageReceivedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.SourceCombatant is not null &&
            context.SourceCombatant.Statuses.Any(status => status.DefinitionId == StatusDefinitionId);
    }
}

public static class DamageReceivedTriggeredEffectTargetResolver
{
    public static CombatantTargetSelectionContext CreateSelectionContext(
        DamageReceivedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new CombatantTargetSelectionContext(
            Combat: context.Combat,
            Source: context.SourceCombatant ?? context.ReceiverCombatant,
            EventTargetId: context.CombatEvent.ReceiverCombatantId);
    }

    public static TriggeredEffectActionSource CreateActionSource(
        DamageReceivedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TriggeredEffectActionSource(
            SourceCombatantId: context.CombatEvent.SourceCombatantId,
            SourceCardId: context.CombatEvent.SourceCardId);
    }

    public static TriggeredEffectActionBuildContext CreateActionBuildContext(
        DamageReceivedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TriggeredEffectActionBuildContext(
            TargetSelectionContext: CreateSelectionContext(context),
            Source: CreateActionSource(context));
    }
}
