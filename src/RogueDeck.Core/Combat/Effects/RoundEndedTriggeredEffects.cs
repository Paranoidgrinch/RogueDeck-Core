namespace RogueDeck.Core.Combat;

public sealed record RoundEndedTriggeredEffectContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    RoundEndedCombatEvent CombatEvent,
    CombatantState LastActiveCombatant);

public sealed class RoundEndedCompletedRoundAmount
    : ICombatValueProvider<RoundEndedTriggeredEffectContext, int>
{
    public int Resolve(RoundEndedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.Round;
    }
}

public sealed record RoundEndedMinimumRoundTriggerFilter(int MinimumRound)
    : ITriggeredProgramFilter<RoundEndedTriggeredEffectContext>
{
    public bool Matches(RoundEndedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.Round >= MinimumRound;
    }
}

public sealed record RoundEndedEveryNthRoundTriggerFilter(int Interval)
    : ITriggeredProgramFilter<RoundEndedTriggeredEffectContext>
{
    public bool Matches(RoundEndedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (Interval <= 0)
            throw new InvalidOperationException("Round interval must be greater than zero.");

        return context.CombatEvent.Round % Interval == 0;
    }
}

public static class RoundEndedTriggeredEffectTargetResolver
{
    public static CombatantTargetSelectionContext CreateSelectionContext(
        RoundEndedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new CombatantTargetSelectionContext(
            Combat: context.Combat,
            Source: context.LastActiveCombatant,
            EventTargetId: context.LastActiveCombatant.Id);
    }

    public static TriggeredEffectActionSource CreateActionSource(
        RoundEndedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TriggeredEffectActionSource(
            SourceCombatantId: context.LastActiveCombatant.Id);
    }

    public static TriggeredEffectActionBuildContext CreateActionBuildContext(
        RoundEndedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TriggeredEffectActionBuildContext(
            TargetSelectionContext: CreateSelectionContext(context),
            Source: CreateActionSource(context));
    }
}
