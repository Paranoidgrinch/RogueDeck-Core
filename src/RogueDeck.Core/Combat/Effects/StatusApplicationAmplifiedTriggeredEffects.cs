namespace RogueDeck.Core.Combat;

// The trigger face of an amplification: a status application on the bearer was made larger, and the register
// that paid for it is named. The mirror of the blocked-application context — a prohibition's refusal and an
// amplifier's enlargement are the two things that can happen to an application on its way in, and content
// answers both the same way.
public sealed record StatusApplicationAmplifiedTriggeredEffectContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    StatusApplicationAmplifiedCombatEvent CombatEvent,
    CombatantState TargetCombatant,
    // Who applied the status that was enlarged, when the application named a source.
    CombatantState? SourceCombatant = null);

// "It was THIS status that grew" — the register writes one thing for an enlarged blessing and another for an
// enlarged curse, and a rule that cares which asks here.
public sealed record StatusApplicationAmplifiedStatusTriggerFilter(
    StatusDefinitionId AmplifiedStatusDefinitionId)
    : ITriggeredProgramFilter<StatusApplicationAmplifiedTriggeredEffectContext>
{
    public bool Matches(StatusApplicationAmplifiedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.AmplifiedStatusDefinitionId == AmplifiedStatusDefinitionId;
    }
}

// "It was THIS register that paid" — which amplifier was spent.
public sealed record StatusApplicationAmplifiedAmplifierTriggerFilter(
    StatusDefinitionId AmplifyingStatusDefinitionId)
    : ITriggeredProgramFilter<StatusApplicationAmplifiedTriggeredEffectContext>
{
    public bool Matches(StatusApplicationAmplifiedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.AmplifyingStatusDefinitionId == AmplifyingStatusDefinitionId;
    }
}

// "What grew was a buff / was a debuff" — the whole question for a rule that reads the direction the register
// was spent in, without having to name every status it might have been.
public sealed record StatusApplicationAmplifiedPolarityTriggerFilter(StatusPolarity Polarity)
    : ITriggeredProgramFilter<StatusApplicationAmplifiedTriggeredEffectContext>
{
    public bool Matches(StatusApplicationAmplifiedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.AmplifiedStatusPolarity == Polarity;
    }
}

// "The combatant this amplification happened to carries the reacting status" — the bearer scope for a status
// trigger on the amplification event, matching the *HasStatus filters of every other trigger event.
public sealed record StatusApplicationAmplifiedTargetHasStatusTriggerFilter(StatusDefinitionId StatusDefinitionId)
    : ITriggeredProgramFilter<StatusApplicationAmplifiedTriggeredEffectContext>
{
    public bool Matches(StatusApplicationAmplifiedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.TargetCombatant.Statuses.Any(status => status.DefinitionId == StatusDefinitionId);
    }
}

public static class StatusApplicationAmplifiedTriggeredEffectTargetResolver
{
    public static CombatantTargetSelectionContext CreateSelectionContext(
        StatusApplicationAmplifiedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // "source" is the combatant the amplified status landed on — the one wearing the register — and
        // "eventTarget" is whoever applied it, so a rule can answer the applier. With no named applier the
        // event target falls back to the bearer, so a program never resolves to nobody.
        return new CombatantTargetSelectionContext(
            Combat: context.Combat,
            Source: context.TargetCombatant,
            EventTargetId: context.CombatEvent.SourceCombatantId ?? context.CombatEvent.TargetCombatantId);
    }

    public static TriggeredEffectActionSource CreateActionSource(
        StatusApplicationAmplifiedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TriggeredEffectActionSource(
            SourceCombatantId: context.CombatEvent.TargetCombatantId);
    }

    public static TriggeredEffectActionBuildContext CreateActionBuildContext(
        StatusApplicationAmplifiedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TriggeredEffectActionBuildContext(
            TargetSelectionContext: CreateSelectionContext(context),
            Source: CreateActionSource(context));
    }
}
