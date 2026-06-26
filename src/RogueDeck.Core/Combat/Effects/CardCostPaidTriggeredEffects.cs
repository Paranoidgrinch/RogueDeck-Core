namespace RogueDeck.Core.Combat;

public sealed record CardCostPaidTriggeredEffectContext(
    CombatState Combat,
    CombatDefinitionRegistry Registry,
    CardCostPaidCombatEvent CombatEvent,
    CombatantState SourceCombatant);

public sealed class CardCostPaidTotalAmount
    : ICombatValueProvider<CardCostPaidTriggeredEffectContext, int>
{
    public int Resolve(CardCostPaidTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.Costs.Sum(cost => cost.Amount);
    }
}

public sealed record CardCostPaidSourceHasStatusTriggerFilter(StatusDefinitionId StatusDefinitionId)
    : ITriggeredProgramFilter<CardCostPaidTriggeredEffectContext>
{
    public bool Matches(CardCostPaidTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.SourceCombatant.Statuses.Any(status => status.DefinitionId == StatusDefinitionId);
    }
}

public sealed record CardCostPaidCardDefinitionTriggerFilter(CardDefinitionId CardDefinitionId)
    : ITriggeredProgramFilter<CardCostPaidTriggeredEffectContext>
{
    public bool Matches(CardCostPaidTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.CardDefinitionId == CardDefinitionId;
    }
}

public sealed record CardCostPaidResourceIdTriggerFilter(ResourceId ResourceId)
    : ITriggeredProgramFilter<CardCostPaidTriggeredEffectContext>
{
    public bool Matches(CardCostPaidTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.Costs.Any(cost => cost.ResourceId == ResourceId);
    }
}

public sealed record CardCostPaidMinimumTotalAmountTriggerFilter(int MinimumTotalAmount)
    : ITriggeredProgramFilter<CardCostPaidTriggeredEffectContext>
{
    public bool Matches(CardCostPaidTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.CombatEvent.Costs.Sum(cost => cost.Amount) >= MinimumTotalAmount;
    }
}

public static class CardCostPaidTriggeredEffectTargetResolver
{
    public static CombatantTargetSelectionContext CreateSelectionContext(
        CardCostPaidTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new CombatantTargetSelectionContext(
            Combat: context.Combat,
            Source: context.SourceCombatant,
            EventTargetId: context.CombatEvent.SourceCombatantId);
    }

    public static TriggeredEffectActionSource CreateActionSource(
        CardCostPaidTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TriggeredEffectActionSource(
            SourceCombatantId: context.CombatEvent.SourceCombatantId);
    }

    public static TriggeredEffectActionBuildContext CreateActionBuildContext(
        CardCostPaidTriggeredEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new TriggeredEffectActionBuildContext(
            TargetSelectionContext: CreateSelectionContext(context),
            Source: CreateActionSource(context));
    }
}
