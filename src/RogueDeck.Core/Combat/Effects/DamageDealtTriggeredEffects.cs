namespace RogueDeck.Core.Combat;

public sealed record DamageDealtTriggeredEffectContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    DamageDealtCombatEvent CombatEvent,
    CombatantState TargetCombatant,
    CombatantState? SourceCombatant);

public sealed record FixedDamageDealtTriggeredEffectAmount(int Amount)
    : ICombatValueProvider<DamageDealtTriggeredEffectContext, int>
{
    public int Resolve(DamageDealtTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Amount;
    }
}

public sealed class DamageDealtHealthDamageAmount
    : ICombatValueProvider<DamageDealtTriggeredEffectContext, int>
{
    public int Resolve(DamageDealtTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.HealthDamage;
    }
}

public sealed class DamageDealtBlockedDamageAmount
    : ICombatValueProvider<DamageDealtTriggeredEffectContext, int>
{
    public int Resolve(DamageDealtTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.BlockedDamage;
    }
}

public sealed class DamageDealtRequestedAmount
    : ICombatValueProvider<DamageDealtTriggeredEffectContext, int>
{
    public int Resolve(DamageDealtTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.RequestedAmount;
    }
}

public sealed record DamageDealtSourceStatusStacksAmount(StatusDefinitionId StatusDefinitionId)
    : ICombatValueProvider<DamageDealtTriggeredEffectContext, int>
{
    public int Resolve(DamageDealtTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.SourceCombatant is null)
            return 0;

        return context.SourceCombatant.Statuses
            .Where(status => status.DefinitionId == StatusDefinitionId)
            .Sum(status => status.Stacks);
    }
}

public sealed record DamageDealtTargetStatusStacksAmount(StatusDefinitionId StatusDefinitionId)
    : ICombatValueProvider<DamageDealtTriggeredEffectContext, int>
{
    public int Resolve(DamageDealtTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.TargetCombatant.Statuses
            .Where(status => status.DefinitionId == StatusDefinitionId)
            .Sum(status => status.Stacks);
    }
}

public sealed class DamageDealtHasSourceTriggerFilter
    : ITriggeredProgramFilter<DamageDealtTriggeredEffectContext>
{
    public bool Matches(DamageDealtTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.SourceCombatant is not null;
    }
}

public sealed record DamageDealtDamageKindTriggerFilter(DamageKind Kind)
    : ITriggeredProgramFilter<DamageDealtTriggeredEffectContext>
{
    public bool Matches(DamageDealtTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.Kind == Kind;
    }
}

public sealed record DamageDealtMinimumHealthDamageTriggerFilter(int MinimumHealthDamage)
    : ITriggeredProgramFilter<DamageDealtTriggeredEffectContext>
{
    public bool Matches(DamageDealtTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.HealthDamage >= MinimumHealthDamage;
    }
}

public sealed record DamageDealtSourceHasStatusTriggerFilter(StatusDefinitionId StatusDefinitionId)
    : ITriggeredProgramFilter<DamageDealtTriggeredEffectContext>
{
    public bool Matches(DamageDealtTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.SourceCombatant is not null &&
            context.SourceCombatant.Statuses.Any(status => status.DefinitionId == StatusDefinitionId);
    }
}

public sealed record DamageDealtTargetHasStatusTriggerFilter(StatusDefinitionId StatusDefinitionId)
    : ITriggeredProgramFilter<DamageDealtTriggeredEffectContext>
{
    public bool Matches(DamageDealtTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.TargetCombatant.Statuses.Any(status => status.DefinitionId == StatusDefinitionId);
    }
}

public static class DamageDealtTriggeredEffectTargetResolver
{
    public static CombatantTargetSelectionContext CreateSelectionContext(
        DamageDealtTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new CombatantTargetSelectionContext(
            Combat: context.Combat,
            Source: context.SourceCombatant ?? context.TargetCombatant,
            EventTargetId: context.CombatEvent.TargetCombatantId);
    }

    public static TriggeredEffectActionSource CreateActionSource(
        DamageDealtTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TriggeredEffectActionSource(
            SourceCombatantId: context.CombatEvent.SourceCombatantId,
            SourceCardId: context.CombatEvent.SourceCardId);
    }

    public static TriggeredEffectActionBuildContext CreateActionBuildContext(
        DamageDealtTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TriggeredEffectActionBuildContext(
            TargetSelectionContext: CreateSelectionContext(context),
            Source: CreateActionSource(context));
    }
}
