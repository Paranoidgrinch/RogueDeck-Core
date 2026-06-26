namespace RogueDeck.Core.Combat;

public sealed record RoundStartedTriggeredEffectContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    RoundStartedCombatEvent CombatEvent,
    CombatantState ActiveCombatant);

public sealed class RoundStartedCurrentRoundAmount
    : ICombatValueProvider<RoundStartedTriggeredEffectContext, int>
{
    public int Resolve(RoundStartedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.Round;
    }
}

public sealed record RoundStartedMinimumRoundTriggerFilter(int MinimumRound)
    : ITriggeredProgramFilter<RoundStartedTriggeredEffectContext>
{
    public bool Matches(RoundStartedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.Round >= MinimumRound;
    }
}

public sealed record RoundStartedEveryNthRoundTriggerFilter(int Interval)
    : ITriggeredProgramFilter<RoundStartedTriggeredEffectContext>
{
    public bool Matches(RoundStartedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (Interval <= 0)
            throw new InvalidOperationException("Round interval must be greater than zero.");

        return context.CombatEvent.Round % Interval == 0;
    }
}

public static class RoundStartedTriggeredEffectTargetResolver
{
    public static CombatantTargetSelectionContext CreateSelectionContext(
        RoundStartedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new CombatantTargetSelectionContext(
            Combat: context.Combat,
            Source: context.ActiveCombatant,
            EventTargetId: context.ActiveCombatant.Id);
    }

    public static TriggeredEffectActionSource CreateActionSource(
        RoundStartedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TriggeredEffectActionSource(
            SourceCombatantId: context.ActiveCombatant.Id);
    }

    public static TriggeredEffectActionBuildContext CreateActionBuildContext(
        RoundStartedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TriggeredEffectActionBuildContext(
            TargetSelectionContext: CreateSelectionContext(context),
            Source: CreateActionSource(context));
    }
}
