namespace RogueDeck.Core.Combat;

public static class TriggeredEffectActionRequestFactory
{
    public static IReadOnlyCollection<IEffectRequest> BuildGainBlockRequests(
        IReadOnlyCollection<CombatantId> targetIds,
        int amount,
        TriggeredEffectActionSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return BuildGainBlockRequests(
            targetIds,
            amount,
            sourceCombatantId: source.SourceCombatantId,
            sourceCardId: source.SourceCardId);
    }

    public static IReadOnlyCollection<IEffectRequest> BuildGainBlockRequests(
        IReadOnlyCollection<CombatantId> targetIds,
        int amount,
        CombatantId? sourceCombatantId = null,
        CardDefinitionId? sourceCardId = null)
    {
        ArgumentNullException.ThrowIfNull(targetIds);

        if (amount <= 0 || targetIds.Count == 0)
            return Array.Empty<IEffectRequest>();

        return targetIds
            .Select(targetId => (IEffectRequest)new GainBlockEffectRequest(
                TargetCombatantId: targetId,
                Amount: amount,
                SourceCombatantId: sourceCombatantId,
                SourceCardId: sourceCardId))
            .ToArray();
    }

    public static IReadOnlyCollection<IEffectRequest> BuildDealDamageRequests(
        IReadOnlyCollection<CombatantId> targetIds,
        int amount,
        TriggeredEffectActionSource source,
        DamageKind kind = DamageKind.Direct)
    {
        ArgumentNullException.ThrowIfNull(source);

        return BuildDealDamageRequests(
            targetIds,
            amount,
            sourceCombatantId: source.SourceCombatantId,
            sourceCardId: source.SourceCardId,
            kind: kind);
    }

    public static IReadOnlyCollection<IEffectRequest> BuildDealDamageRequests(
        IReadOnlyCollection<CombatantId> targetIds,
        int amount,
        CombatantId? sourceCombatantId = null,
        CardDefinitionId? sourceCardId = null,
        DamageKind kind = DamageKind.Direct)
    {
        ArgumentNullException.ThrowIfNull(targetIds);

        if (amount <= 0 || targetIds.Count == 0)
            return Array.Empty<IEffectRequest>();

        return targetIds
            .Select(targetId => (IEffectRequest)new DealDamageEffectRequest(
                TargetCombatantId: targetId,
                Amount: amount,
                SourceCombatantId: sourceCombatantId,
                SourceCardId: sourceCardId,
                Kind: kind))
            .ToArray();
    }

    public static IReadOnlyCollection<IEffectRequest> BuildHealRequests(
        IReadOnlyCollection<CombatantId> targetIds,
        int amount,
        TriggeredEffectActionSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return BuildHealRequests(
            targetIds,
            amount,
            sourceCombatantId: source.SourceCombatantId,
            sourceCardId: source.SourceCardId);
    }

    public static IReadOnlyCollection<IEffectRequest> BuildHealRequests(
        IReadOnlyCollection<CombatantId> targetIds,
        int amount,
        CombatantId? sourceCombatantId = null,
        CardDefinitionId? sourceCardId = null)
    {
        ArgumentNullException.ThrowIfNull(targetIds);

        if (amount <= 0 || targetIds.Count == 0)
            return Array.Empty<IEffectRequest>();

        return targetIds
            .Select(targetId => (IEffectRequest)new HealEffectRequest(
                TargetCombatantId: targetId,
                Amount: amount,
                SourceCombatantId: sourceCombatantId,
                SourceCardId: sourceCardId))
            .ToArray();
    }

    public static IReadOnlyCollection<IEffectRequest> BuildDrawCardsRequests(
        IReadOnlyCollection<CombatantId> targetIds,
        int count)
    {
        ArgumentNullException.ThrowIfNull(targetIds);

        if (count <= 0 || targetIds.Count == 0)
            return Array.Empty<IEffectRequest>();

        return targetIds
            .Select(targetId => (IEffectRequest)new DrawCardsEffectRequest(
                CombatantId: targetId,
                Count: count))
            .ToArray();
    }

    public static IReadOnlyCollection<IEffectRequest> BuildApplyStatusRequests(
        IReadOnlyCollection<CombatantId> targetIds,
        StatusDefinitionId statusDefinitionId,
        int stacks,
        int durationTurns,
        int charges,
        TriggeredEffectActionSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return BuildApplyStatusRequests(
            targetIds,
            statusDefinitionId,
            stacks,
            durationTurns,
            charges,
            sourceCombatantId: source.SourceCombatantId,
            sourceCardId: source.SourceCardId);
    }

    public static IReadOnlyCollection<IEffectRequest> BuildApplyStatusRequests(
        IReadOnlyCollection<CombatantId> targetIds,
        StatusDefinitionId statusDefinitionId,
        int stacks,
        int durationTurns,
        int charges,
        CombatantId? sourceCombatantId = null,
        CardDefinitionId? sourceCardId = null)
    {
        ArgumentNullException.ThrowIfNull(targetIds);

        if (stacks < 0 || durationTurns < 0 || charges < 0)
            return Array.Empty<IEffectRequest>();

        if (stacks == 0 && durationTurns == 0 && charges == 0)
            return Array.Empty<IEffectRequest>();

        if (targetIds.Count == 0)
            return Array.Empty<IEffectRequest>();

        return targetIds
            .Select(targetId => (IEffectRequest)new ApplyStatusEffectRequest(
                TargetCombatantId: targetId,
                StatusDefinitionId: statusDefinitionId,
                SourceCombatantId: sourceCombatantId,
                SourceCardId: sourceCardId,
                Stacks: stacks,
                DurationTurns: durationTurns,
                Charges: charges))
            .ToArray();
    }
}
