namespace RogueDeck.Core.Combat;

// Trigger context for ResourceLostCombatEvent — an explicit, non-cost resource loss, distinct
// from ResourceGained, ResourceModified, and CardCostPaid.
public sealed record ResourceLostTriggeredEffectContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    ResourceLostCombatEvent CombatEvent,
    CombatantState SourceCombatant);

public sealed class ResourceLostAmount
    : ICombatValueProvider<ResourceLostTriggeredEffectContext, int>
{
    public int Resolve(ResourceLostTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.CombatEvent.LostAmount;
    }
}

public sealed class ResourceLostNewCurrentAmount
    : ICombatValueProvider<ResourceLostTriggeredEffectContext, int>
{
    public int Resolve(ResourceLostTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.CombatEvent.NewCurrent;
    }
}

public sealed record ResourceLostResourceIdTriggerFilter(ResourceId ResourceId)
    : ITriggeredProgramFilter<ResourceLostTriggeredEffectContext>
{
    public bool Matches(ResourceLostTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.CombatEvent.ResourceId == ResourceId;
    }
}

public sealed record ResourceLostMinimumAmountTriggerFilter(int MinimumLostAmount)
    : ITriggeredProgramFilter<ResourceLostTriggeredEffectContext>
{
    public bool Matches(ResourceLostTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.CombatEvent.LostAmount >= MinimumLostAmount;
    }
}

public sealed class ResourceLostReachedZeroTriggerFilter
    : ITriggeredProgramFilter<ResourceLostTriggeredEffectContext>
{
    public bool Matches(ResourceLostTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.CombatEvent.NewCurrent == 0;
    }
}

public static class ResourceLostTriggeredEffectTargetResolver
{
    public static CombatantTargetSelectionContext CreateSelectionContext(
        ResourceLostTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new CombatantTargetSelectionContext(
            Combat: context.Combat,
            Source: context.SourceCombatant,
            EventTargetId: context.CombatEvent.CombatantId);
    }

    public static TriggeredEffectActionSource CreateActionSource(
        ResourceLostTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TriggeredEffectActionSource(
            SourceCombatantId: context.CombatEvent.CombatantId);
    }

    public static TriggeredEffectActionBuildContext CreateActionBuildContext(
        ResourceLostTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TriggeredEffectActionBuildContext(
            TargetSelectionContext: CreateSelectionContext(context),
            Source: CreateActionSource(context));
    }
}
