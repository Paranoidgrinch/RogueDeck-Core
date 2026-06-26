namespace RogueDeck.Core.Combat;

public static class TriggeredEffectActionBuilder
{
    public static IReadOnlyCollection<IEffectRequest> BuildGainBlockRequests(
        TriggeredEffectActionBuildContext context,
        ICombatantTargetSelector targetSelector,
        int amount)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(targetSelector);

        if (amount <= 0)
            return Array.Empty<IEffectRequest>();

        var targetIds = ResolveTargets(context, targetSelector);

        return TriggeredEffectActionRequestFactory.BuildGainBlockRequests(
            targetIds,
            amount,
            context.Source);
    }

    public static IReadOnlyCollection<IEffectRequest> BuildDealDamageRequests(
        TriggeredEffectActionBuildContext context,
        ICombatantTargetSelector targetSelector,
        int amount,
        DamageKind kind = DamageKind.Direct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(targetSelector);

        if (amount <= 0)
            return Array.Empty<IEffectRequest>();

        var targetIds = ResolveTargets(context, targetSelector);

        return TriggeredEffectActionRequestFactory.BuildDealDamageRequests(
            targetIds,
            amount,
            context.Source,
            kind);
    }

    public static IReadOnlyCollection<IEffectRequest> BuildHealRequests(
        TriggeredEffectActionBuildContext context,
        ICombatantTargetSelector targetSelector,
        int amount)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(targetSelector);

        if (amount <= 0)
            return Array.Empty<IEffectRequest>();

        var targetIds = ResolveTargets(context, targetSelector);

        return TriggeredEffectActionRequestFactory.BuildHealRequests(
            targetIds,
            amount,
            context.Source);
    }

    public static IReadOnlyCollection<IEffectRequest> BuildDrawCardsRequests(
        TriggeredEffectActionBuildContext context,
        ICombatantTargetSelector targetSelector,
        int count)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(targetSelector);

        if (count <= 0)
            return Array.Empty<IEffectRequest>();

        var targetIds = ResolveTargets(context, targetSelector);

        return TriggeredEffectActionRequestFactory.BuildDrawCardsRequests(
            targetIds,
            count);
    }

    public static IReadOnlyCollection<IEffectRequest> BuildApplyStatusRequests(
        TriggeredEffectActionBuildContext context,
        ICombatantTargetSelector targetSelector,
        StatusDefinitionId statusDefinitionId,
        int stacks,
        int durationTurns,
        int charges)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(targetSelector);

        if (stacks < 0 || durationTurns < 0 || charges < 0)
            return Array.Empty<IEffectRequest>();

        if (stacks == 0 && durationTurns == 0 && charges == 0)
            return Array.Empty<IEffectRequest>();

        var targetIds = ResolveTargets(context, targetSelector);

        return TriggeredEffectActionRequestFactory.BuildApplyStatusRequests(
            targetIds,
            statusDefinitionId,
            stacks,
            durationTurns,
            charges,
            context.Source);
    }

    private static IReadOnlyCollection<CombatantId> ResolveTargets(
        TriggeredEffectActionBuildContext context,
        ICombatantTargetSelector targetSelector)
    {
        return targetSelector.ResolveTargets(context.TargetSelectionContext);
    }
}
