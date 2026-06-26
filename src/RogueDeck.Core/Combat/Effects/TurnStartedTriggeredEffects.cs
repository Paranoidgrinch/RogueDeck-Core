namespace RogueDeck.Core.Combat;

public sealed record TurnStartedTriggeredEffectContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    TurnStartedCombatEvent CombatEvent,
    CombatantState TurnCombatant);

public sealed record TurnStartedCombatantStatusStacksAmount(StatusDefinitionId StatusDefinitionId)
    : ICombatValueProvider<TurnStartedTriggeredEffectContext, int>
{
    public int Resolve(TurnStartedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.TurnCombatant.Statuses
            .Where(status => status.DefinitionId == StatusDefinitionId)
            .Sum(status => status.Stacks);
    }
}

public sealed record TurnStartedCombatantHasStatusTriggerFilter(StatusDefinitionId StatusDefinitionId)
    : ITriggeredProgramFilter<TurnStartedTriggeredEffectContext>
{
    public bool Matches(TurnStartedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.TurnCombatant.Statuses.Any(status => status.DefinitionId == StatusDefinitionId);
    }
}

public static class TurnStartedTriggeredEffectTargetResolver
{
    public static IReadOnlyCollection<CombatantId> ResolveTargets(
        TurnStartedTriggeredEffectContext context,
        ICombatantTargetSelector targetSelector)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(targetSelector);

        return targetSelector.ResolveTargets(CreateSelectionContext(context));
    }

    public static CombatantTargetSelectionContext CreateSelectionContext(
        TurnStartedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new CombatantTargetSelectionContext(
            Combat: context.Combat,
            Source: context.TurnCombatant);
    }

    public static TriggeredEffectActionSource CreateActionSource(
        TurnStartedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TriggeredEffectActionSource(
            SourceCombatantId: context.CombatEvent.CombatantId);
    }

    public static TriggeredEffectActionBuildContext CreateActionBuildContext(
        TurnStartedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TriggeredEffectActionBuildContext(
            TargetSelectionContext: CreateSelectionContext(context),
            Source: CreateActionSource(context));
    }
}
