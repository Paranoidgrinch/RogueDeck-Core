namespace RogueDeck.Core.Combat;

// Trigger context for ResourceModifiedCombatEvent — a general resource modification (which may be
// a loss), distinct from ResourceGained.
public sealed record ResourceModifiedTriggeredEffectContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    ResourceModifiedCombatEvent CombatEvent,
    CombatantState SourceCombatant);

public sealed record ResourceModifiedResourceIdTriggerFilter(ResourceId ResourceId)
    : ITriggeredProgramFilter<ResourceModifiedTriggeredEffectContext>
{
    public bool Matches(ResourceModifiedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.CombatEvent.ResourceId == ResourceId;
    }
}

// Matches only resource losses (negative applied delta).
public sealed class ResourceModifiedLossTriggerFilter
    : ITriggeredProgramFilter<ResourceModifiedTriggeredEffectContext>
{
    public bool Matches(ResourceModifiedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.CombatEvent.AppliedDelta < 0;
    }
}

public static class ResourceModifiedTriggeredEffectTargetResolver
{
    public static TriggeredEffectActionBuildContext CreateActionBuildContext(
        ResourceModifiedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TriggeredEffectActionBuildContext(
            TargetSelectionContext: new CombatantTargetSelectionContext(
                Combat: context.Combat,
                Source: context.SourceCombatant,
                EventTargetId: context.CombatEvent.CombatantId),
            Source: new TriggeredEffectActionSource(
                SourceCombatantId: context.CombatEvent.CombatantId));
    }
}
