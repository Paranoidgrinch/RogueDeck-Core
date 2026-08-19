namespace RogueDeck.Core.Combat;

public sealed record GainBlockEffectRequest(
    CombatantId TargetCombatantId,
    int Amount,
    CombatantId? SourceCombatantId = null,
    CardDefinitionId? SourceCardId = null,
    GainBlockOutcomeSlot? OutcomeSlot = null
) : IEffectRequest;

public sealed class GainBlockEffectHandler : EffectRequestHandler<GainBlockEffectRequest>
{
    protected override void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        GainBlockEffectRequest gainBlock)
    {
        if (gainBlock.Amount < 0)
            throw new ArgumentOutOfRangeException(nameof(gainBlock.Amount), "Block amount cannot be negative.");

        var tracing = combat.TraceListener is not null;
        var target = combat.GetCombatant(gainBlock.TargetCombatantId);
        var modifiedAmount = ApplyBlockAmountModifiers(
            combat,
            registry,
            gainBlock,
            target,
            out var modifierSteps,
            collectSteps: tracing);

        if (modifiedAmount == 0)
        {
            // No-op (e.g. a modifier reduced the gain to zero) must still complete the slot.
            var currentBlock = target.DefensivePools.TryGetValue(
                StandardCombatIds.BlockDefensivePool, out var existingPool)
                ? existingPool.Current
                : 0;

            TraceBlockGain(combat, tracing, gainBlock, modifierSteps,
                amountAfterModifiers: 0, blockBefore: currentBlock, blockAfter: currentBlock);

            if (gainBlock.OutcomeSlot is { } noOpSlot)
                noOpSlot.Value = new GainBlockOutcome(
                    RequestedAmount: gainBlock.Amount,
                    ModifiedAmount: 0,
                    PreviousBlock: currentBlock,
                    NewBlock: currentBlock);
            return;
        }

        if (!target.DefensivePools.TryGetValue(StandardCombatIds.BlockDefensivePool, out var block))
        {
            target.AddDefensivePool(
                StandardCombatIds.BlockDefensivePool,
                new ValuePoolState(current: modifiedAmount));

            combat.AddLogEntry(
                StandardCombatLogTypes.BlockGained,
                $"Gained {modifiedAmount} block on '{gainBlock.TargetCombatantId}'.");

            TraceBlockGain(combat, tracing, gainBlock, modifierSteps,
                amountAfterModifiers: modifiedAmount, blockBefore: 0, blockAfter: modifiedAmount);

            RaiseBlockGained(combat, gainBlock, modifiedAmount);

            if (gainBlock.OutcomeSlot is { } newPoolSlot)
                newPoolSlot.Value = new GainBlockOutcome(
                    RequestedAmount: gainBlock.Amount,
                    ModifiedAmount: modifiedAmount,
                    PreviousBlock: 0,
                    NewBlock: modifiedAmount);

            return;
        }

        var previousBlock = block.Current;
        var requestedBlock = (long)previousBlock + modifiedAmount;
        var newBlock = (int)Math.Min(int.MaxValue, requestedBlock);
        block.SetCurrent(newBlock);

        combat.AddLogEntry(
            StandardCombatLogTypes.BlockGained,
            $"Gained {modifiedAmount} block on '{gainBlock.TargetCombatantId}'.");

        TraceBlockGain(combat, tracing, gainBlock, modifierSteps,
            amountAfterModifiers: modifiedAmount, blockBefore: previousBlock, blockAfter: newBlock);

        RaiseBlockGained(combat, gainBlock, modifiedAmount);

        if (gainBlock.OutcomeSlot is { } slot)
            slot.Value = new GainBlockOutcome(
                RequestedAmount: gainBlock.Amount,
                ModifiedAmount: modifiedAmount,
                PreviousBlock: previousBlock,
                NewBlock: newBlock);
    }

    // Only a gain that actually landed is announced — a gain modified down to zero is not a "gain Block" event.
    private static void RaiseBlockGained(CombatState combat, GainBlockEffectRequest gainBlock, int gainedAmount) =>
        combat.EnqueueEvent(new BlockGainedCombatEvent(
            TargetCombatantId: gainBlock.TargetCombatantId,
            GainedAmount: gainedAmount,
            RequestedAmount: gainBlock.Amount,
            SourceCombatantId: gainBlock.SourceCombatantId,
            SourceCardId: gainBlock.SourceCardId));

    private static void TraceBlockGain(
        CombatState combat,
        bool tracing,
        GainBlockEffectRequest gainBlock,
        List<BlockModifierStepTrace>? modifierSteps,
        int amountAfterModifiers,
        int blockBefore,
        int blockAfter)
    {
        if (!tracing)
            return;

        combat.Trace(new BlockGainResolvedTraceEvent(
            combat.CurrentRound, combat.CurrentTurn,
            gainBlock.TargetCombatantId,
            RequestedAmount: gainBlock.Amount,
            ModifierSteps: modifierSteps ?? [],
            AmountAfterModifiers: amountAfterModifiers,
            BlockBefore: blockBefore,
            BlockAfter: blockAfter));
    }

    private static int ApplyBlockAmountModifiers(
        CombatState combat,
        CombatDefinitionRegistry registry,
        GainBlockEffectRequest gainBlock,
        CombatantState target,
        out List<BlockModifierStepTrace>? steps,
        bool collectSteps)
    {
        steps = collectSteps ? [] : null;
        CombatantState? source = null;

        if (gainBlock.SourceCombatantId is not null &&
            combat.TryGetCombatant(gainBlock.SourceCombatantId.Value, out var foundSource))
        {
            source = foundSource;
        }

        var context = new BlockAmountModificationContext(
            Combat: combat,
            Registry: registry,
            TargetCombatant: target,
            SourceCombatant: source,
            SourceCardId: gainBlock.SourceCardId,
            RequestedAmount: gainBlock.Amount);

        var currentAmount = gainBlock.Amount;

        foreach (var modifier in registry.GetBlockAmountModifiers())
        {
            var before = currentAmount;
            var after = Math.Max(0, modifier.ModifyBlockAmount(context, before));
            if (collectSteps && after != before)
                steps!.Add(new BlockModifierStepTrace(modifier.ModifierId, before, after));
            currentAmount = after;
        }

        return currentAmount;
    }
}

public sealed record ClearDefensivePoolEffectRequest(
    CombatantId TargetCombatantId,
    DefensivePoolId PoolId,
    ClearDefensivePoolOutcomeSlot? OutcomeSlot = null
) : IEffectRequest;

public sealed class ClearDefensivePoolEffectHandler : EffectRequestHandler<ClearDefensivePoolEffectRequest>
{
    protected override void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        ClearDefensivePoolEffectRequest clearPool)
    {
        var target = combat.GetCombatant(clearPool.TargetCombatantId);

        if (!target.DefensivePools.TryGetValue(clearPool.PoolId, out var pool))
        {
            combat.AddLogEntry(
                StandardCombatLogTypes.DefensivePoolCleared,
                $"Cleared 0 from defensive pool '{clearPool.PoolId}' on '{clearPool.TargetCombatantId}'.");

            if (clearPool.OutcomeSlot is { } emptySlot)
                emptySlot.Value = new ClearDefensivePoolOutcome(ClearedAmount: 0, WasChanged: false);

            return;
        }

        var clearedAmount = pool.Current;
        pool.SetCurrent(0);

        combat.AddLogEntry(
            StandardCombatLogTypes.DefensivePoolCleared,
            $"Cleared {clearedAmount} from defensive pool '{clearPool.PoolId}' on '{clearPool.TargetCombatantId}'.");

        if (combat.TraceListener is not null)
            combat.Trace(new DefensivePoolChangeResolvedTraceEvent(
                combat.CurrentRound, combat.CurrentTurn,
                clearPool.TargetCombatantId, clearPool.PoolId,
                DefensivePoolChangeKind.Cleared,
                RequestedDelta: -clearedAmount,
                AppliedDelta: -clearedAmount,
                PreviousValue: clearedAmount,
                NewValue: 0));

        if (clearPool.OutcomeSlot is { } slot)
            slot.Value = new ClearDefensivePoolOutcome(ClearedAmount: clearedAmount, WasChanged: clearedAmount > 0);
    }
}

public sealed record ModifyDefensivePoolEffectRequest(
    CombatantId TargetCombatantId,
    DefensivePoolId PoolId,
    int Delta,
    PoolChangeOutcomeSlot? OutcomeSlot = null
) : IEffectRequest;

public sealed class ModifyDefensivePoolEffectHandler
    : EffectRequestHandler<ModifyDefensivePoolEffectRequest>
{
    protected override void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        ModifyDefensivePoolEffectRequest request)
    {
        var target = combat.GetCombatant(request.TargetCombatantId);

        var previous = 0;
        var applied = 0;
        var next = 0;

        if (target.DefensivePools.TryGetValue(request.PoolId, out var pool))
        {
            previous = pool.Current;
            var max = pool.Max;
            var raw = (long)previous + request.Delta;
            next = max.HasValue
                ? (int)Math.Clamp(raw, 0, max.Value)
                : (int)Math.Max(0L, Math.Min((long)int.MaxValue, raw));
            applied = next - previous;
            pool.SetCurrent(next);
        }
        else if (request.Delta > 0)
        {
            next = request.Delta;
            applied = request.Delta;
            target.AddDefensivePool(request.PoolId, new ValuePoolState(next));
        }

        if (request.OutcomeSlot is { } slot)
            slot.Value = new PoolChangeOutcome(
                RequestedDelta: request.Delta,
                AppliedDelta: applied,
                PreviousValue: previous,
                NewValue: next);

        if (combat.TraceListener is not null)
            combat.Trace(new DefensivePoolChangeResolvedTraceEvent(
                combat.CurrentRound, combat.CurrentTurn,
                request.TargetCombatantId, request.PoolId,
                DefensivePoolChangeKind.Modified,
                RequestedDelta: request.Delta,
                AppliedDelta: applied,
                PreviousValue: previous,
                NewValue: next));

        // A general pool modification is not a clear — use the distinct modified log type so
        // triggers/UI can tell a gain/loss apart from an actual pool clear.
        combat.AddLogEntry(
            StandardCombatLogTypes.DefensivePoolModified,
            $"Modified defensive pool '{request.PoolId}' on '{request.TargetCombatantId}' " +
            $"by {applied} (requested {request.Delta}, {previous} → {next}).");
    }
}

public sealed class ClearBlockOnTurnStartedHandler
    : CombatEventHandler<TurnStartedCombatEvent>
{
    protected override void Handle(
        CombatState combat,
        CombatDefinitionRegistry registry,
        TurnStartedCombatEvent combatEvent)
    {
        if (!combat.TryGetCombatant(combatEvent.CombatantId, out var combatant))
            return;

        // Declarative override: a status bearing the retain-block tag suppresses the start-of-turn clear of
        // every clearing pool for its wearer (e.g. Barricade keeping Block across turns). Same status-tag
        // mechanism as retain-hand and DamageOverTime.
        var retains = combatant!.Statuses.Any(status => status.Tags.Contains(StandardCombatIds.RetainBlockTag));

        // Clear every registered defensive pool that empties at its owner's turn start (Block by default;
        // custom pools opt in via DefensivePoolDefinition.ClearsOnOwnerTurnStart).
        foreach (var poolDef in registry.DefensivePoolDefinitions.Values)
        {
            if (!poolDef.ClearsOnOwnerTurnStart)
                continue;
            if (!combatant.DefensivePools.TryGetValue(poolDef.Id, out var pool) || pool.Current <= 0)
                continue;

            if (retains)
            {
                combat.AddLogEntry(
                    StandardCombatLogTypes.TurnAutomationSuppressed,
                    $"Combatant '{combatEvent.CombatantId}' retained '{poolDef.Id}' (start-of-turn clear suppressed).");
                continue;
            }

            combat.EnqueueEffect(
                new ClearDefensivePoolEffectRequest(
                    TargetCombatantId: combatEvent.CombatantId,
                    PoolId: poolDef.Id));
        }
    }
}
