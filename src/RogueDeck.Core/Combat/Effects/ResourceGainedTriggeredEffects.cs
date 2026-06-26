namespace RogueDeck.Core.Combat;

public sealed record ResourceGainedTriggeredEffectContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    ResourceGainedCombatEvent CombatEvent,
    CombatantState SourceCombatant);

public sealed record FixedResourceGainedTriggeredEffectAmount(int Amount)
    : ICombatValueProvider<ResourceGainedTriggeredEffectContext, int>
{
    public int Resolve(ResourceGainedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Amount;
    }
}

public sealed class ResourceGainedAmount
    : ICombatValueProvider<ResourceGainedTriggeredEffectContext, int>
{
    public int Resolve(ResourceGainedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.GainedAmount;
    }
}

public sealed class ResourceGainedNewCurrentAmount
    : ICombatValueProvider<ResourceGainedTriggeredEffectContext, int>
{
    public int Resolve(ResourceGainedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.NewCurrent;
    }
}

public sealed record ResourceGainedSourceHasStatusTriggerFilter(StatusDefinitionId StatusDefinitionId)
    : ITriggeredProgramFilter<ResourceGainedTriggeredEffectContext>
{
    public bool Matches(ResourceGainedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.SourceCombatant.Statuses.Any(status => status.DefinitionId == StatusDefinitionId);
    }
}

public sealed record ResourceGainedResourceIdTriggerFilter(ResourceId ResourceId)
    : ITriggeredProgramFilter<ResourceGainedTriggeredEffectContext>
{
    public bool Matches(ResourceGainedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.ResourceId == ResourceId;
    }
}

public sealed record ResourceGainedMinimumAmountTriggerFilter(int MinimumGainedAmount)
    : ITriggeredProgramFilter<ResourceGainedTriggeredEffectContext>
{
    public bool Matches(ResourceGainedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.GainedAmount >= MinimumGainedAmount;
    }
}

public sealed class ResourceGainedReachedMaxTriggerFilter
    : ITriggeredProgramFilter<ResourceGainedTriggeredEffectContext>
{
    public bool Matches(ResourceGainedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.Max is { } max &&
            context.CombatEvent.NewCurrent >= max;
    }
}

public static class ResourceGainedTriggeredEffectTargetResolver
{
    public static CombatantTargetSelectionContext CreateSelectionContext(
        ResourceGainedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new CombatantTargetSelectionContext(
            Combat: context.Combat,
            Source: context.SourceCombatant,
            EventTargetId: context.CombatEvent.CombatantId);
    }

    public static TriggeredEffectActionSource CreateActionSource(
        ResourceGainedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TriggeredEffectActionSource(
            SourceCombatantId: context.CombatEvent.CombatantId);
    }

    public static TriggeredEffectActionBuildContext CreateActionBuildContext(
        ResourceGainedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TriggeredEffectActionBuildContext(
            TargetSelectionContext: CreateSelectionContext(context),
            Source: CreateActionSource(context));
    }
}
