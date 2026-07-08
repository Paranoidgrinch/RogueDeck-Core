namespace RogueDeck.Core.Combat;

// A serializable "apply this status" grant — the data half of an ApplyStatusEffectRequest without a target. Used to
// give a combatant innate statuses at creation (e.g. a summoned board unit is born with its auto-action marker and
// keyword statuses). Additive: nothing consumes it unless content supplies it.
public sealed record StatusGrant(
    StatusDefinitionId StatusDefinitionId,
    int Stacks = 0,
    int DurationTurns = 0,
    int Charges = 0);

public sealed record ApplyStatusEffectRequest(
    CombatantId TargetCombatantId,
    StatusDefinitionId StatusDefinitionId,
    CombatantId? SourceCombatantId = null,
    CardDefinitionId? SourceCardId = null,
    int Stacks = 0,
    int DurationTurns = 0,
    int Charges = 0,
    ApplyStatusOutcomeSlot? OutcomeSlot = null,
    // Tracks how many times this request has been replaced by an interceptor.
    // When it reaches MaxInterceptionDepth, the chain is skipped to prevent loops.
    int InterceptionDepth = 0
) : IEffectRequest;

public sealed class ApplyStatusEffectHandler : EffectRequestHandler<ApplyStatusEffectRequest>
{
    // Replacements that are themselves ApplyStatusEffectRequests go through the
    // interceptor chain again (so they can be blocked/replaced in turn). This limit
    // stops accidental infinite replacement loops.
    private const int MaxInterceptionDepth = 3;

    protected override void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        ApplyStatusEffectRequest applyStatus)
    {
        if (applyStatus.Stacks < 0)
            throw new ArgumentOutOfRangeException(nameof(applyStatus.Stacks), "Stacks cannot be negative.");

        if (applyStatus.DurationTurns < 0)
            throw new ArgumentOutOfRangeException(nameof(applyStatus.DurationTurns), "Duration cannot be negative.");

        if (applyStatus.Charges < 0)
            throw new ArgumentOutOfRangeException(nameof(applyStatus.Charges), "Charges cannot be negative.");

        var tracing = combat.TraceListener is not null;

        var target = combat.GetCombatant(applyStatus.TargetCombatantId);
        var definition = registry.GetStatus(applyStatus.StatusDefinitionId);

        var (interception, interceptorId) = CheckStatusApplicationInterception(
            combat, registry, applyStatus, target, definition);

        if (interception.IsBlocked)
        {
            if (applyStatus.OutcomeSlot is { } blockedSlot)
                blockedSlot.Value = new ApplyStatusOutcome(
                    Applied: false, Merged: false, Blocked: true,
                    ResultingStacks: 0, ResultingDurationTurns: 0, ResultingCharges: 0);

            TraceStatusApplication(combat, tracing, applyStatus,
                StatusApplicationOutcome.BlockedByInterceptor,
                resultingStacks: 0, resultingDuration: 0, resultingCharges: 0,
                interceptorId, replacementRequestType: null);
            return;
        }

        if (interception.TryGetReplacement(out var replacement))
        {
            if (applyStatus.OutcomeSlot is { } replacedSlot)
                replacedSlot.Value = new ApplyStatusOutcome(
                    Applied: false, Merged: false, Blocked: true,
                    ResultingStacks: 0, ResultingDurationTurns: 0, ResultingCharges: 0);

            TraceStatusApplication(combat, tracing, applyStatus,
                StatusApplicationOutcome.ReplacedByInterceptor,
                resultingStacks: 0, resultingDuration: 0, resultingCharges: 0,
                interceptorId, replacementRequestType: replacement!.GetType().Name);

            // If the replacement is itself an ApplyStatusEffectRequest, increment the
            // depth counter so loop-forming replacement chains are bounded.
            if (replacement is ApplyStatusEffectRequest applyReplacement)
                combat.EnqueueEffect(applyReplacement with
                {
                    InterceptionDepth = applyStatus.InterceptionDepth + 1
                });
            else
                combat.EnqueueEffect(replacement!);
            return;
        }

        // Declarative outgoing-application augment: a status on the applying (source) combatant can
        // scale the stacks of this application (e.g. Catalyst doubles Poison applications). Computed
        // once on the actual-apply path so a blocked/replaced application is never double-scaled.
        var effectiveStacks = applyStatus.Stacks;
        if (applyStatus.SourceCombatantId is { } sourceId &&
            combat.TryGetCombatant(sourceId, out var sourceCombatant) && sourceCombatant is not null)
        {
            effectiveStacks = DeclarativePassiveModifierEngine.Apply(
                combat, registry, sourceCombatant, PassiveModifierPipeline.OutgoingStatusApplicationStacks,
                damageKind: null, applyStatus.Stacks, appliesToStatusId: applyStatus.StatusDefinitionId);
        }

        if (definition.StackingBehavior == StatusStackingBehavior.MergeWithExistingInstance)
        {
            var existingStatus = target.Statuses.FirstOrDefault(
                status => status.DefinitionId == applyStatus.StatusDefinitionId);

            if (existingStatus is not null)
            {
                // Stacks/charges merge with a long intermediate clamped to int.MaxValue so a
                // large existing value plus a large incoming value cannot silently overflow.
                existingStatus.SetStacks(
                    (int)Math.Min(int.MaxValue, (long)existingStatus.Stacks + effectiveStacks));
                existingStatus.SetDurationTurns(Math.Max(existingStatus.DurationTurns, applyStatus.DurationTurns));
                existingStatus.SetCharges(
                    (int)Math.Min(int.MaxValue, (long)existingStatus.Charges + applyStatus.Charges));

                combat.AddLogEntry(
                    StandardCombatLogTypes.StatusMerged,
                    $"Merged status '{applyStatus.StatusDefinitionId}' on '{applyStatus.TargetCombatantId}'.");

                if (applyStatus.OutcomeSlot is { } mergedSlot)
                    mergedSlot.Value = new ApplyStatusOutcome(
                        Applied: false, Merged: true, Blocked: false,
                        ResultingStacks: existingStatus.Stacks,
                        ResultingDurationTurns: existingStatus.DurationTurns,
                        ResultingCharges: existingStatus.Charges);

                TraceStatusApplication(combat, tracing, applyStatus,
                    StatusApplicationOutcome.Merged,
                    resultingStacks: existingStatus.Stacks,
                    resultingDuration: existingStatus.DurationTurns,
                    resultingCharges: existingStatus.Charges,
                    interceptingModifierId: null, replacementRequestType: null);

                combat.EnqueueEvent(
                    new StatusMergedCombatEvent(
                        TargetCombatantId: applyStatus.TargetCombatantId,
                        StatusInstanceId: existingStatus.Id,
                        StatusDefinitionId: existingStatus.DefinitionId,
                        Stacks: existingStatus.Stacks,
                        DurationTurns: existingStatus.DurationTurns,
                        Charges: existingStatus.Charges,
                        SourceCombatantId: existingStatus.SourceCombatantId,
                        SourceCardId: existingStatus.SourceCardId));

                return;
            }
        }

        var newStatus = new StatusInstance(
            combat.CreateNextStatusInstanceId(),
            applyStatus.StatusDefinitionId,
            applyStatus.TargetCombatantId,
            applyStatus.SourceCombatantId,
            applyStatus.SourceCardId,
            effectiveStacks,
            applyStatus.DurationTurns,
            applyStatus.Charges,
            combat.CurrentRound,
            combat.CurrentTurn,
            definition.DefaultVisibility,
            definition.Polarity,
            initialTags: definition.Tags);

        target.AddStatus(newStatus);

        if (applyStatus.OutcomeSlot is { } appliedSlot)
            appliedSlot.Value = new ApplyStatusOutcome(
                Applied: true, Merged: false, Blocked: false,
                ResultingStacks: newStatus.Stacks,
                ResultingDurationTurns: newStatus.DurationTurns,
                ResultingCharges: newStatus.Charges);

        TraceStatusApplication(combat, tracing, applyStatus,
            StatusApplicationOutcome.Applied,
            resultingStacks: newStatus.Stacks,
            resultingDuration: newStatus.DurationTurns,
            resultingCharges: newStatus.Charges,
            interceptingModifierId: null, replacementRequestType: null);

        combat.AddLogEntry(
            StandardCombatLogTypes.StatusApplied,
            $"Applied status '{applyStatus.StatusDefinitionId}' to '{applyStatus.TargetCombatantId}'.");

        combat.EnqueueEvent(
            new StatusAppliedCombatEvent(
                TargetCombatantId: applyStatus.TargetCombatantId,
                StatusInstanceId: newStatus.Id,
                StatusDefinitionId: newStatus.DefinitionId,
                Stacks: newStatus.Stacks,
                DurationTurns: newStatus.DurationTurns,
                Charges: newStatus.Charges,
                SourceCombatantId: newStatus.SourceCombatantId,
                SourceCardId: newStatus.SourceCardId));
    }

    // Returns the decisive interception result together with the id of the interceptor that
    // produced it (null when nothing intercepted), so the caller can name it in the trace.
    private static (InterceptionResult Result, string? InterceptorId) CheckStatusApplicationInterception(
        CombatState combat,
        CombatDefinitionRegistry registry,
        ApplyStatusEffectRequest applyStatus,
        CombatantState target,
        StatusDefinition definition)
    {
        // Skip the chain for replacements that have looped back too many times.
        if (applyStatus.InterceptionDepth >= MaxInterceptionDepth)
            return (InterceptionResult.Allow, null);

        var context = new StatusApplicationInterceptionContext(
            Combat: combat,
            Registry: registry,
            TargetCombatant: target,
            StatusDefinition: definition,
            Request: applyStatus);

        foreach (var interceptor in registry.GetStatusApplicationInterceptors())
        {
            var result = interceptor.TryIntercept(context);
            if (result.IsBlocked || result.TryGetReplacement(out _))
                return (result, interceptor.ModifierId);
        }

        return (InterceptionResult.Allow, null);
    }

    private static void TraceStatusApplication(
        CombatState combat,
        bool tracing,
        ApplyStatusEffectRequest applyStatus,
        StatusApplicationOutcome outcome,
        int resultingStacks,
        int resultingDuration,
        int resultingCharges,
        string? interceptingModifierId,
        string? replacementRequestType)
    {
        if (!tracing)
            return;

        combat.Trace(new StatusApplicationResolvedTraceEvent(
            combat.CurrentRound, combat.CurrentTurn,
            applyStatus.TargetCombatantId,
            applyStatus.StatusDefinitionId,
            outcome,
            applyStatus.Stacks,
            applyStatus.DurationTurns,
            applyStatus.Charges,
            resultingStacks,
            resultingDuration,
            resultingCharges,
            interceptingModifierId,
            replacementRequestType));
    }
}

public sealed record DecreaseStatusDurationEffectRequest(
    CombatantId TargetCombatantId,
    StatusInstanceId StatusInstanceId,
    int Amount = 1
) : IEffectRequest;

public sealed class DecreaseStatusDurationEffectHandler : EffectRequestHandler<DecreaseStatusDurationEffectRequest>
{
    protected override void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        DecreaseStatusDurationEffectRequest decreaseDuration)
    {
        if (decreaseDuration.Amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(decreaseDuration.Amount), "Duration decrease amount must be greater than zero.");

        var target = combat.GetCombatant(decreaseDuration.TargetCombatantId);
        var status = target.Statuses.FirstOrDefault(
            existing => existing.Id == decreaseDuration.StatusInstanceId);

        if (status is null)
            return;

        if (status.DurationTurns <= 0)
            return;

        var oldDuration = status.DurationTurns;
        var newDuration = Math.Max(0, oldDuration - decreaseDuration.Amount);

        status.SetDurationTurns(newDuration);

        combat.AddLogEntry(
            StandardCombatLogTypes.StatusDurationReduced,
            $"Reduced status '{status.DefinitionId}' duration on '{target.Id}' from {oldDuration} to {newDuration}.");

        if (newDuration > 0)
            return;

        target.RemoveStatus(status);

        combat.AddLogEntry(
            StandardCombatLogTypes.StatusExpired,
            $"Status '{status.DefinitionId}' expired on '{target.Id}'.");

        combat.EnqueueEvent(
            new StatusExpiredCombatEvent(
                TargetCombatantId: target.Id,
                StatusInstanceId: status.Id,
                StatusDefinitionId: status.DefinitionId));
    }
}

public sealed class DecreaseTimedStatusDurationsOnTurnEndedHandler
    : CombatEventHandler<TurnEndedCombatEvent>
{
    protected override void Handle(
        CombatState combat,
        CombatDefinitionRegistry registry,
        TurnEndedCombatEvent combatEvent)
    {
        if (!combat.TryGetCombatant(combatEvent.CombatantId, out var combatant))
            return;

        foreach (var status in combatant!.Statuses.ToArray())
        {
            var definition = registry.GetStatus(status.DefinitionId);

            if (!definition.UsesDuration)
                continue;

            if (status.DurationTurns <= 0)
                continue;

            combat.EnqueueEffect(
                new DecreaseStatusDurationEffectRequest(
                    TargetCombatantId: combatEvent.CombatantId,
                    StatusInstanceId: status.Id));
        }
    }
}

public sealed class DamageOverTimeOnTurnStartedHandler
    : CombatEventHandler<TurnStartedCombatEvent>
{
    protected override void Handle(
        CombatState combat,
        CombatDefinitionRegistry registry,
        TurnStartedCombatEvent combatEvent)
    {
        if (!combat.TryGetCombatant(combatEvent.CombatantId, out var combatant))
            return;

        foreach (var status in combatant!.Statuses.ToArray())
        {
            if (!status.Tags.Contains(StandardCombatIds.DamageOverTimeTag))
                continue;

            if (status.Stacks <= 0)
                continue;

            combat.EnqueueEffect(
                new DealDamageEffectRequest(
                    TargetCombatantId: combatEvent.CombatantId,
                    Amount: status.Stacks,
                    SourceCombatantId: status.SourceCombatantId,
                    SourceCardId: status.SourceCardId,
                    Kind: DamageKind.DamageOverTime));
        }
    }
}

public sealed class TriggeredDamageOnDamageDealtHandler
    : CombatEventHandler<DamageDealtCombatEvent>
{
    protected override void Handle(
        CombatState combat,
        CombatDefinitionRegistry registry,
        DamageDealtCombatEvent combatEvent)
    {
        if (combatEvent.Kind == DamageKind.Reflected)
            return;

        if (combatEvent.HealthDamage <= 0)
            return;

        if (combatEvent.SourceCombatantId is null)
            return;

        if (combatEvent.SourceCombatantId.Value == combatEvent.TargetCombatantId)
            return;

        if (!combat.TryGetCombatant(combatEvent.TargetCombatantId, out var damagedCombatant))
            return;

        foreach (var status in damagedCombatant!.Statuses.ToArray())
        {
            if (!status.Tags.Contains(StandardCombatIds.TriggeredDamageTag))
                continue;

            if (status.Stacks <= 0)
                continue;

            combat.EnqueueEffect(
                new DealDamageEffectRequest(
                    TargetCombatantId: combatEvent.SourceCombatantId.Value,
                    Amount: status.Stacks,
                    SourceCombatantId: combatEvent.TargetCombatantId,
                    SourceCardId: status.SourceCardId,
                    Kind: DamageKind.Reflected));
        }
    }
}

public sealed record RemoveStatusEffectRequest(
    CombatantId TargetCombatantId,
    StatusDefinitionId StatusDefinitionId,
    RemoveStatusOutcomeSlot? OutcomeSlot = null
) : IEffectRequest;

public sealed class RemoveStatusEffectHandler : EffectRequestHandler<RemoveStatusEffectRequest>
{
    protected override void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        RemoveStatusEffectRequest request)
    {
        registry.GetStatus(request.StatusDefinitionId);

        var target = combat.GetCombatant(request.TargetCombatantId);
        var statusesToRemove = target.Statuses
            .Where(status => status.DefinitionId == request.StatusDefinitionId)
            .ToArray();

        if (statusesToRemove.Length == 0)
        {
            if (request.OutcomeSlot is { } emptySlot)
                emptySlot.Value = new RemoveStatusOutcome(0, []);
            return;
        }

        foreach (var status in statusesToRemove)
            target.RemoveStatus(status);

        var removedStatusIds = statusesToRemove
            .Select(status => status.Id)
            .ToArray();

        if (request.OutcomeSlot is { } slot)
            slot.Value = new RemoveStatusOutcome(removedStatusIds.Length, removedStatusIds);

        combat.AddLogEntry(
            StandardCombatLogTypes.StatusRemoved,
            $"Removed status '{request.StatusDefinitionId}' from '{request.TargetCombatantId}'.");

        combat.EnqueueEvent(new StatusRemovedCombatEvent(
            request.TargetCombatantId,
            removedStatusIds,
            request.StatusDefinitionId));
    }
}

public sealed record DecreaseStatusChargesEffectRequest(
    CombatantId TargetCombatantId,
    StatusInstanceId StatusInstanceId,
    int Amount = 1
) : IEffectRequest;

public sealed class DecreaseStatusChargesEffectHandler
    : EffectRequestHandler<DecreaseStatusChargesEffectRequest>
{
    protected override void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        DecreaseStatusChargesEffectRequest decreaseCharges)
    {
        if (decreaseCharges.Amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(decreaseCharges.Amount), "Charge decrease amount must be greater than zero.");

        var target = combat.GetCombatant(decreaseCharges.TargetCombatantId);
        var status = target.Statuses.FirstOrDefault(
            existing => existing.Id == decreaseCharges.StatusInstanceId);

        if (status is null)
            return;

        if (status.Charges <= 0)
            return;

        var oldCharges = status.Charges;
        var newCharges = Math.Max(0, oldCharges - decreaseCharges.Amount);

        status.SetCharges(newCharges);

        combat.AddLogEntry(
            StandardCombatLogTypes.StatusChargesReduced,
            $"Reduced status '{status.DefinitionId}' charges on '{target.Id}' from {oldCharges} to {newCharges}.");

        combat.EnqueueEvent(new StatusChargesReducedCombatEvent(
            TargetCombatantId: target.Id,
            StatusInstanceId: status.Id,
            StatusDefinitionId: status.DefinitionId,
            OldCharges: oldCharges,
            NewCharges: newCharges));

        if (newCharges > 0)
            return;

        target.RemoveStatus(status);

        combat.AddLogEntry(
            StandardCombatLogTypes.StatusExpired,
            $"Status '{status.DefinitionId}' expired on '{target.Id}'.");

        combat.EnqueueEvent(new StatusExpiredCombatEvent(
            TargetCombatantId: target.Id,
            StatusInstanceId: status.Id,
            StatusDefinitionId: status.DefinitionId));
    }
}

// Modifies the stacks of the first matching status instance. Positive delta increases, negative decreases.
// Clamps to 0. If stacks reach 0 from a positive initial value, removes the status instance.
public sealed record ModifyStatusStacksEffectRequest(
    CombatantId TargetCombatantId,
    StatusDefinitionId StatusDefinitionId,
    int Delta,
    ModifyStatusStacksOutcomeSlot? OutcomeSlot = null
) : IEffectRequest;

public sealed class ModifyStatusStacksEffectHandler : EffectRequestHandler<ModifyStatusStacksEffectRequest>
{
    protected override void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        ModifyStatusStacksEffectRequest request)
    {
        var target = combat.GetCombatant(request.TargetCombatantId);
        var status = target.Statuses.FirstOrDefault(s => s.DefinitionId == request.StatusDefinitionId);

        if (status is null)
        {
            if (request.OutcomeSlot is { } emptySlot)
                emptySlot.Value = new ModifyStatusStacksOutcome(0, 0, 0, WasChanged: false, WasRemoved: false);
            return;
        }

        var oldStacks = status.Stacks;
        var rawNew = (long)oldStacks + request.Delta;
        var newStacks = (int)Math.Max(0, Math.Min(int.MaxValue, rawNew));
        var actualDelta = newStacks - oldStacks;

        if (actualDelta == 0)
        {
            if (request.OutcomeSlot is { } noOpSlot)
                noOpSlot.Value = new ModifyStatusStacksOutcome(oldStacks, oldStacks, 0, WasChanged: false, WasRemoved: false);
            return;
        }

        status.SetStacks(newStacks);

        var wasRemoved = newStacks == 0 && oldStacks > 0;

        if (wasRemoved)
        {
            target.RemoveStatus(status);
            combat.AddLogEntry(
                StandardCombatLogTypes.StatusExpired,
                $"Status '{status.DefinitionId}' expired on '{target.Id}' (stacks depleted).");
            combat.EnqueueEvent(new StatusExpiredCombatEvent(target.Id, status.Id, status.DefinitionId));
        }
        else
        {
            combat.AddLogEntry(
                StandardCombatLogTypes.StatusStacksChanged,
                $"Status '{status.DefinitionId}' stacks on '{target.Id}' changed from {oldStacks} to {newStacks}.");
            combat.EnqueueEvent(new StatusStacksChangedCombatEvent(
                target.Id, status.Id, status.DefinitionId, oldStacks, newStacks));
        }

        if (request.OutcomeSlot is { } slot)
            slot.Value = new ModifyStatusStacksOutcome(oldStacks, newStacks, actualDelta, WasChanged: true, WasRemoved: wasRemoved);
    }
}

// Modifies the remaining duration of the first matching status instance. Positive delta extends, negative reduces.
// Clamps to 0. If duration reaches 0 from a positive initial value, removes the status instance.
public sealed record ModifyStatusDurationEffectRequest(
    CombatantId TargetCombatantId,
    StatusDefinitionId StatusDefinitionId,
    int Delta,
    ModifyStatusDurationOutcomeSlot? OutcomeSlot = null
) : IEffectRequest;

public sealed class ModifyStatusDurationEffectHandler : EffectRequestHandler<ModifyStatusDurationEffectRequest>
{
    protected override void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        ModifyStatusDurationEffectRequest request)
    {
        var target = combat.GetCombatant(request.TargetCombatantId);
        var status = target.Statuses.FirstOrDefault(s => s.DefinitionId == request.StatusDefinitionId);

        if (status is null)
        {
            if (request.OutcomeSlot is { } emptySlot)
                emptySlot.Value = new ModifyStatusDurationOutcome(0, 0, 0, WasChanged: false, WasRemoved: false);
            return;
        }

        var oldDuration = status.DurationTurns;
        var rawNew = (long)oldDuration + request.Delta;
        var newDuration = (int)Math.Max(0, Math.Min(int.MaxValue, rawNew));
        var actualDelta = newDuration - oldDuration;

        if (actualDelta == 0)
        {
            if (request.OutcomeSlot is { } noOpSlot)
                noOpSlot.Value = new ModifyStatusDurationOutcome(oldDuration, oldDuration, 0, WasChanged: false, WasRemoved: false);
            return;
        }

        status.SetDurationTurns(newDuration);

        var wasRemoved = newDuration == 0 && oldDuration > 0;

        if (wasRemoved)
        {
            target.RemoveStatus(status);
            combat.AddLogEntry(
                StandardCombatLogTypes.StatusExpired,
                $"Status '{status.DefinitionId}' expired on '{target.Id}' (duration depleted).");
            combat.EnqueueEvent(new StatusExpiredCombatEvent(target.Id, status.Id, status.DefinitionId));
        }
        else
        {
            combat.AddLogEntry(
                StandardCombatLogTypes.StatusDurationReduced,
                $"Status '{status.DefinitionId}' duration on '{target.Id}' changed from {oldDuration} to {newDuration}.");
            combat.EnqueueEvent(new StatusDurationChangedCombatEvent(
                target.Id, status.Id, status.DefinitionId, oldDuration, newDuration));
        }

        if (request.OutcomeSlot is { } slot)
            slot.Value = new ModifyStatusDurationOutcome(oldDuration, newDuration, actualDelta, WasChanged: true, WasRemoved: wasRemoved);
    }
}

// Modifies the charges of the first matching status instance. Positive delta adds, negative reduces.
// Clamps to 0. If charges reach 0 from a positive initial value, removes the status instance.
public sealed record ModifyStatusChargesEffectRequest(
    CombatantId TargetCombatantId,
    StatusDefinitionId StatusDefinitionId,
    int Delta,
    ModifyStatusChargesOutcomeSlot? OutcomeSlot = null
) : IEffectRequest;

public sealed class ModifyStatusChargesEffectHandler : EffectRequestHandler<ModifyStatusChargesEffectRequest>
{
    protected override void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        ModifyStatusChargesEffectRequest request)
    {
        var target = combat.GetCombatant(request.TargetCombatantId);
        var status = target.Statuses.FirstOrDefault(s => s.DefinitionId == request.StatusDefinitionId);

        if (status is null)
        {
            if (request.OutcomeSlot is { } emptySlot)
                emptySlot.Value = new ModifyStatusChargesOutcome(0, 0, 0, WasChanged: false, WasRemoved: false);
            return;
        }

        var oldCharges = status.Charges;
        var rawNew = (long)oldCharges + request.Delta;
        var newCharges = (int)Math.Max(0, Math.Min(int.MaxValue, rawNew));
        var actualDelta = newCharges - oldCharges;

        if (actualDelta == 0)
        {
            if (request.OutcomeSlot is { } noOpSlot)
                noOpSlot.Value = new ModifyStatusChargesOutcome(oldCharges, oldCharges, 0, WasChanged: false, WasRemoved: false);
            return;
        }

        status.SetCharges(newCharges);

        var wasRemoved = newCharges == 0 && oldCharges > 0;

        if (wasRemoved)
        {
            target.RemoveStatus(status);
            combat.AddLogEntry(
                StandardCombatLogTypes.StatusExpired,
                $"Status '{status.DefinitionId}' expired on '{target.Id}' (charges depleted).");
            combat.EnqueueEvent(new StatusExpiredCombatEvent(target.Id, status.Id, status.DefinitionId));
        }
        else
        {
            combat.AddLogEntry(
                StandardCombatLogTypes.StatusChargesChanged,
                $"Status '{status.DefinitionId}' charges on '{target.Id}' changed from {oldCharges} to {newCharges}.");
            combat.EnqueueEvent(new StatusChargesChangedCombatEvent(
                target.Id, status.Id, status.DefinitionId, oldCharges, newCharges));
        }

        if (request.OutcomeSlot is { } slot)
            slot.Value = new ModifyStatusChargesOutcome(oldCharges, newCharges, actualDelta, WasChanged: true, WasRemoved: wasRemoved);
    }
}

public sealed record RemoveStatusesByPolarityEffectRequest(
    CombatantId TargetCombatantId,
    StatusPolarity Polarity,
    RemoveStatusesByPolarityOutcomeSlot? OutcomeSlot = null
) : IEffectRequest;

public sealed class RemoveStatusesByPolarityEffectHandler
    : EffectRequestHandler<RemoveStatusesByPolarityEffectRequest>
{
    protected override void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        RemoveStatusesByPolarityEffectRequest request)
    {
        var target = combat.GetCombatant(request.TargetCombatantId);

        var statusesToRemove = target.Statuses
            .Where(status => registry.GetStatus(status.DefinitionId).Polarity == request.Polarity)
            .ToArray();

        if (statusesToRemove.Length == 0)
        {
            if (request.OutcomeSlot is { } emptySlot)
                emptySlot.Value = new RemoveStatusesByPolarityOutcome(0, [], request.Polarity);
            return;
        }

        foreach (var status in statusesToRemove)
            target.RemoveStatus(status);

        var removedStatusIds = statusesToRemove
            .Select(status => status.Id)
            .ToArray();

        if (request.OutcomeSlot is { } slot)
            slot.Value = new RemoveStatusesByPolarityOutcome(
                removedStatusIds.Length, removedStatusIds, request.Polarity);

        combat.AddLogEntry(
            StandardCombatLogTypes.StatusesRemovedByPolarity,
            $"Removed {removedStatusIds.Length} '{request.Polarity}' status(es) from '{request.TargetCombatantId}'.");

        combat.EnqueueEvent(new StatusesRemovedByPolarityCombatEvent(
            TargetCombatantId: request.TargetCombatantId,
            StatusInstanceIds: removedStatusIds,
            Polarity: request.Polarity));
    }
}

