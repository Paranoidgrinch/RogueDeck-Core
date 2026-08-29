namespace RogueDeck.Core.Combat;

public sealed record CardInstanceCreatedTriggeredEffectContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    CardInstanceCreatedCombatEvent CombatEvent,
    CombatantState OwnerCombatant);

public sealed record FixedCardInstanceCreatedTriggeredEffectAmount(int Amount)
    : ICombatValueProvider<CardInstanceCreatedTriggeredEffectContext, int>
{
    public int Resolve(CardInstanceCreatedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return Amount;
    }
}

public sealed class CardInstanceCreatedCount
    : ICombatValueProvider<CardInstanceCreatedTriggeredEffectContext, int>
{
    public int Resolve(CardInstanceCreatedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.CardInstanceIds.Count;
    }
}

// Bearer filter for a status trigger: only the owner's own statuses may react to a card being made for them.
public sealed record CardInstanceCreatedOwnerHasStatusTriggerFilter(StatusDefinitionId StatusDefinitionId)
    : ITriggeredProgramFilter<CardInstanceCreatedTriggeredEffectContext>
{
    public bool Matches(CardInstanceCreatedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.OwnerCombatant.Statuses.Any(status => status.DefinitionId == StatusDefinitionId);
    }
}

public sealed record CardInstanceCreatedCardDefinitionTriggerFilter(CardDefinitionId CardDefinitionId)
    : ITriggeredProgramFilter<CardInstanceCreatedTriggeredEffectContext>
{
    public bool Matches(CardInstanceCreatedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.CardDefinitionId == CardDefinitionId;
    }
}

public sealed record CardInstanceCreatedToZoneTriggerFilter(CardZone ToZone)
    : ITriggeredProgramFilter<CardInstanceCreatedTriggeredEffectContext>
{
    public bool Matches(CardInstanceCreatedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.ToZone == ToZone;
    }
}

public sealed record CardInstanceCreatedMinimumCountTriggerFilter(int MinimumCount)
    : ITriggeredProgramFilter<CardInstanceCreatedTriggeredEffectContext>
{
    public bool Matches(CardInstanceCreatedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.CardInstanceIds.Count >= MinimumCount;
    }
}

public static class CardInstanceCreatedTriggeredEffectTargetResolver
{
    public static CombatantTargetSelectionContext CreateSelectionContext(
        CardInstanceCreatedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new CombatantTargetSelectionContext(
            Combat: context.Combat,
            Source: context.OwnerCombatant,
            EventTargetId: context.CombatEvent.CombatantId);
    }

    public static TriggeredEffectActionSource CreateActionSource(
        CardInstanceCreatedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TriggeredEffectActionSource(
            SourceCombatantId: context.CombatEvent.CombatantId);
    }

    public static TriggeredEffectActionBuildContext CreateActionBuildContext(
        CardInstanceCreatedTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TriggeredEffectActionBuildContext(
            TargetSelectionContext: CreateSelectionContext(context),
            Source: CreateActionSource(context));
    }
}
