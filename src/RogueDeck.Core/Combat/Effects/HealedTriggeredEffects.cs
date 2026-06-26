namespace RogueDeck.Core.Combat;

public sealed record HealedTriggeredEffectContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    HealedCombatEvent CombatEvent,
    CombatantState TargetCombatant,
    CombatantState? SourceCombatant);

public sealed record FixedHealedTriggeredEffectAmount(int Amount)
    : ICombatValueProvider<HealedTriggeredEffectContext, int>
{
    public int Resolve(HealedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Amount;
    }
}

public sealed class HealedAmount
    : ICombatValueProvider<HealedTriggeredEffectContext, int>
{
    public int Resolve(HealedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.HealedAmount;
    }
}

public sealed class HealedRequestedAmount
    : ICombatValueProvider<HealedTriggeredEffectContext, int>
{
    public int Resolve(HealedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.RequestedAmount;
    }
}

public sealed class HealedHasSourceTriggerFilter
    : ITriggeredProgramFilter<HealedTriggeredEffectContext>
{
    public bool Matches(HealedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.SourceCombatant is not null;
    }
}

// Fires only when the healed combatant carries the given status — the receiver-scoped companion to
// HealedSourceCombatantTriggerFilter, used to bind a "when the bearer is healed" status trigger.
public sealed record HealedTargetHasStatusTriggerFilter(StatusDefinitionId StatusDefinitionId)
    : ITriggeredProgramFilter<HealedTriggeredEffectContext>
{
    public bool Matches(HealedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.TargetCombatant.Statuses.Any(status => status.DefinitionId == StatusDefinitionId);
    }
}

public sealed record HealedSourceCombatantTriggerFilter(CombatantId SourceCombatantId)
    : ITriggeredProgramFilter<HealedTriggeredEffectContext>
{
    public bool Matches(HealedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.SourceCombatantId == SourceCombatantId;
    }
}

public sealed record HealedSourceCardTriggerFilter(CardDefinitionId SourceCardId)
    : ITriggeredProgramFilter<HealedTriggeredEffectContext>
{
    public bool Matches(HealedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.SourceCardId == SourceCardId;
    }
}

public sealed record HealedMinimumAmountTriggerFilter(int MinimumHealedAmount)
    : ITriggeredProgramFilter<HealedTriggeredEffectContext>
{
    public bool Matches(HealedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.HealedAmount >= MinimumHealedAmount;
    }
}

public static class HealedTriggeredEffectTargetResolver
{
    public static CombatantTargetSelectionContext CreateSelectionContext(
        HealedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new CombatantTargetSelectionContext(
            Combat: context.Combat,
            Source: context.SourceCombatant ?? context.TargetCombatant,
            EventTargetId: context.CombatEvent.TargetCombatantId);
    }

    public static TriggeredEffectActionSource CreateActionSource(
        HealedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TriggeredEffectActionSource(
            SourceCombatantId: context.CombatEvent.SourceCombatantId,
            SourceCardId: context.CombatEvent.SourceCardId);
    }

    public static TriggeredEffectActionBuildContext CreateActionBuildContext(
        HealedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TriggeredEffectActionBuildContext(
            TargetSelectionContext: CreateSelectionContext(context),
            Source: CreateActionSource(context));
    }
}
