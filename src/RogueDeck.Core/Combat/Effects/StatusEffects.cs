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
    int InterceptionDepth = 0,
    // The one prohibition this application is not subject to — the mirror of StatusPreventionSpec.Only.
    // A prohibition names the single status it refuses; this names the single prohibition that may not
    // refuse THIS application, which is what an injunction against a licence is: the licence still exists
    // and is still spendable elsewhere, but not against this.
    StatusDefinitionId? UnrefusableBy = null,
    // Whether an amplification has already enlarged this application. An enlarged application is re-run
    // through the interceptor chain (so a prohibition still meets its true size), and this mark is what
    // stops a second amplifier — or the same one's next stack — from enlarging it again.
    bool Amplified = false,
    // Whether this application is a COPY of another one — a forgery, a duplicate seal, a mirrored filing.
    //
    // A copy is an ordinary application in every way that matters at the table: it lands, it is refused or
    // enlarged like any other, and rules may answer it. What it must never do is start another copy chain,
    // or count as the ORIGINAL application a chain is measured from — which is a question only the copier can
    // ask, and only if the application says what it is. The mark rides on the applied/merged events for
    // exactly that.
    bool Replicated = false
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

        // Due notice: a status the target carries can postpone what lands on it. A postponed application is
        // always its own instance — merging it into a status already in force would make it effective at once.
        var pendingTurns = IncomingDelayFor(combat, registry, target, definition);

        if (pendingTurns == 0 && definition.StackingBehavior == StatusStackingBehavior.MergeWithExistingInstance)
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

                // A merge reports the source of THIS application, not of the instance it merged into. The two
                // are different questions — "who did this to me?" and "whose status is this?" — and only the
                // first is what an event is for. Reporting the instance's owner meant every rule that asks
                // whether somebody ELSE just applied something got the wrong body the moment the status was
                // already there, which is most of the time. The instance keeps its own source, untouched, for
                // rules that ask about standing (Act III's source-bound Trespass reads the STATUS, not the
                // event); an application that names no source still falls back to it.
                combat.EnqueueEvent(
                    new StatusMergedCombatEvent(
                        TargetCombatantId: applyStatus.TargetCombatantId,
                        StatusInstanceId: existingStatus.Id,
                        StatusDefinitionId: existingStatus.DefinitionId,
                        Stacks: existingStatus.Stacks,
                        DurationTurns: existingStatus.DurationTurns,
                        Charges: existingStatus.Charges,
                        SourceCombatantId: applyStatus.SourceCombatantId ?? existingStatus.SourceCombatantId,
                        SourceCardId: applyStatus.SourceCardId ?? existingStatus.SourceCardId,
                        Replicated: applyStatus.Replicated,
                        AppliedStacks: effectiveStacks));

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
            initialTags: definition.Tags,
            pendingTurns: pendingTurns);

        target.AddStatus(newStatus);

        if (pendingTurns > 0)
        {
            combat.AddLogEntry(
                StandardCombatLogTypes.StatusApplied,
                $"Status '{applyStatus.StatusDefinitionId}' on '{applyStatus.TargetCombatantId}' is pending "
                + $"for {pendingTurns} turn(s).");

            if (applyStatus.OutcomeSlot is { } pendingSlot)
                pendingSlot.Value = new ApplyStatusOutcome(
                    Applied: true, Merged: false, Blocked: false,
                    ResultingStacks: newStatus.Stacks,
                    ResultingDurationTurns: newStatus.DurationTurns,
                    ResultingCharges: newStatus.Charges);

            // A pending status is not in force, so it raises no StatusApplied event: nothing may react to a
            // notice as though it were an effect. The activation event comes when it takes hold.
            return;
        }

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
                SourceCardId: newStatus.SourceCardId,
                Replicated: applyStatus.Replicated));
    }

    // The longest delay any status in force on the target imposes on this kind of application.
    private static int IncomingDelayFor(
        CombatState combat, CombatDefinitionRegistry registry, CombatantState target, StatusDefinition definition)
    {
        var delay = 0;
        foreach (var status in target.Statuses)
        {
            if (!registry.TryGetStatus(status.DefinitionId, out var carried) || carried is null)
                continue;
            if (carried.IncomingStatusDelay is { } spec && spec.Applies(definition.Polarity) && spec.Turns > delay)
                delay = spec.Turns;
        }

        return delay;
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

// Postponed statuses count down at the start of their bearer's turn and take effect at zero.
public sealed class ActivatePendingStatusesOnTurnStartedHandler
    : CombatEventHandler<TurnStartedCombatEvent>
{
    protected override void Handle(
        CombatState combat,
        CombatDefinitionRegistry registry,
        TurnStartedCombatEvent combatEvent)
    {
        if (!combat.TryGetCombatant(combatEvent.CombatantId, out var combatant) || combatant is null)
            return;

        var pending = combatant.PendingStatuses.ToList();
        if (pending.Count == 0)
            return;

        foreach (var status in pending)
        {
            status.SetPendingTurns(status.PendingTurns - 1);
            if (!status.IsActive)
                continue;

            combat.AddLogEntry(
                StandardCombatLogTypes.StatusApplied,
                $"Status '{status.DefinitionId}' on '{combatant.Id}' takes effect.");

            combat.EnqueueEvent(new StatusActivatedCombatEvent(
                TargetCombatantId: combatant.Id,
                StatusInstanceId: status.Id,
                StatusDefinitionId: status.DefinitionId,
                Stacks: status.Stacks,
                DurationTurns: status.DurationTurns,
                Charges: status.Charges,
                SourceCombatantId: status.SourceCombatantId,
                SourceCardId: status.SourceCardId));
        }

        combatant.RecountActiveStatuses();
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
                new DamageOverTimeTickEffectRequest(
                    TargetCombatantId: combatEvent.CombatantId,
                    StatusInstanceId: status.Id));
        }
    }
}

// One damage-over-time instance ticking at its bearer's turn start. The tick reads the instance's stacks
// when it RESOLVES, not when it was queued: turn-start triggers run before this automation (see
// TurnStartedEffectRecipeArchitectureTests) but their own work is queued too, so an "antidote" trigger that
// removes stacks must shrink the very tick it precedes. A vanished or emptied instance ticks for nothing.
public sealed record DamageOverTimeTickEffectRequest(
    CombatantId TargetCombatantId,
    StatusInstanceId StatusInstanceId) : IEffectRequest;

public sealed class DamageOverTimeTickEffectHandler
    : EffectRequestHandler<DamageOverTimeTickEffectRequest>
{
    protected override void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        DamageOverTimeTickEffectRequest tick)
    {
        if (!combat.TryGetCombatant(tick.TargetCombatantId, out var combatant))
            return;

        var status = combatant!.Statuses.FirstOrDefault(s => s.Id == tick.StatusInstanceId);
        if (status is null
            || status.Stacks <= 0
            || !status.Tags.Contains(StandardCombatIds.DamageOverTimeTag))
            return;

        combat.EnqueueEffect(
            new DealDamageEffectRequest(
                TargetCombatantId: tick.TargetCombatantId,
                Amount: status.Stacks,
                SourceCombatantId: status.SourceCombatantId,
                SourceCardId: status.SourceCardId,
                Kind: DamageKind.DamageOverTime));
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
        // AllStatuses: a notice that has not taken effect yet can still be answered — cleansing reaches
        // pending instances as well as those in force.
        var statusesToRemove = target.AllStatuses
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

// Removes ONE specific status instance (addressed by its StatusInstanceId), unlike RemoveStatusEffectRequest
// which removes every instance of a definition. Backs the status-instance selection ops (#3): "remove a random
// buff" resolves an instance id, then this removes exactly it. A no-op (empty outcome) if the instance is gone.
public sealed record RemoveStatusInstanceEffectRequest(
    CombatantId TargetCombatantId,
    StatusInstanceId StatusInstanceId,
    RemoveStatusOutcomeSlot? OutcomeSlot = null
) : IEffectRequest;

public sealed class RemoveStatusInstanceEffectHandler : EffectRequestHandler<RemoveStatusInstanceEffectRequest>
{
    protected override void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        RemoveStatusInstanceEffectRequest request)
    {
        var target = combat.GetCombatant(request.TargetCombatantId);
        var status = target.Statuses.FirstOrDefault(s => s.Id == request.StatusInstanceId);

        if (status is null)
        {
            if (request.OutcomeSlot is { } emptySlot)
                emptySlot.Value = new RemoveStatusOutcome(0, []);
            return;
        }

        target.RemoveStatus(status);

        if (request.OutcomeSlot is { } slot)
            slot.Value = new RemoveStatusOutcome(1, [status.Id]);

        combat.AddLogEntry(
            StandardCombatLogTypes.StatusRemoved,
            $"Removed status instance '{status.DefinitionId}' from '{request.TargetCombatantId}'.");

        combat.EnqueueEvent(new StatusRemovedCombatEvent(
            request.TargetCombatantId,
            [status.Id],
            status.DefinitionId));
    }
}

// Modifies the stacks of ONE specific status instance (addressed by StatusInstanceId), unlike
// ModifyStatusStacksEffectRequest which acts on the first instance of a definition. Clamps to 0 and removes the
// instance if its stacks deplete. Backs the "reduce/boost a SELECTED status" ops (#3 status-instance targeting).
public sealed record ModifyStatusInstanceStacksEffectRequest(
    CombatantId TargetCombatantId,
    StatusInstanceId StatusInstanceId,
    int Delta
) : IEffectRequest;

public sealed class ModifyStatusInstanceStacksEffectHandler
    : EffectRequestHandler<ModifyStatusInstanceStacksEffectRequest>
{
    protected override void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        ModifyStatusInstanceStacksEffectRequest request)
    {
        var target = combat.GetCombatant(request.TargetCombatantId);
        var status = target.Statuses.FirstOrDefault(s => s.Id == request.StatusInstanceId);
        if (status is null)
            return;

        var oldStacks = status.Stacks;
        var newStacks = (int)Math.Max(0, Math.Min(int.MaxValue, (long)oldStacks + request.Delta));
        if (newStacks == oldStacks)
            return;

        status.SetStacks(newStacks);

        if (newStacks == 0 && oldStacks > 0)
        {
            target.RemoveStatus(status);
            combat.AddLogEntry(StandardCombatLogTypes.StatusExpired,
                $"Status '{status.DefinitionId}' expired on '{target.Id}' (stacks depleted).");
            combat.EnqueueEvent(new StatusExpiredCombatEvent(target.Id, status.Id, status.DefinitionId));
        }
        else
        {
            combat.AddLogEntry(StandardCombatLogTypes.StatusStacksChanged,
                $"Status '{status.DefinitionId}' stacks on '{target.Id}' changed from {oldStacks} to {newStacks}.");
            combat.EnqueueEvent(new StatusStacksChangedCombatEvent(
                target.Id, status.Id, status.DefinitionId, oldStacks, newStacks));
        }
    }
}

// Moves ONE specific status instance from one combatant to another (#3 "steal a status"): the instance is
// removed from FromCombatantId and re-created on ToCombatantId with the same definition/stacks/duration/charges/
// source/applied-time/polarity/tags (a fresh instance id). "Steal the enemy's Strength."
public sealed record StealStatusInstanceEffectRequest(
    CombatantId FromCombatantId,
    CombatantId ToCombatantId,
    StatusInstanceId StatusInstanceId
) : IEffectRequest;

public sealed class StealStatusInstanceEffectHandler : EffectRequestHandler<StealStatusInstanceEffectRequest>
{
    protected override void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        StealStatusInstanceEffectRequest request)
    {
        if (request.FromCombatantId == request.ToCombatantId)
            return;

        var from = combat.GetCombatant(request.FromCombatantId);
        var status = from.Statuses.FirstOrDefault(s => s.Id == request.StatusInstanceId);
        if (status is null)
            return;

        from.RemoveStatus(status);
        combat.EnqueueEvent(new StatusRemovedCombatEvent(from.Id, [status.Id], status.DefinitionId));

        var to = combat.GetCombatant(request.ToCombatantId);
        var moved = new StatusInstance(
            combat.CreateNextStatusInstanceId(),
            status.DefinitionId,
            to.Id,
            status.SourceCombatantId,
            status.SourceCardId,
            status.Stacks,
            status.DurationTurns,
            status.Charges,
            status.AppliedRound,
            status.AppliedTurn,
            status.Visibility,
            status.Polarity,
            initialTags: status.Tags);
        to.AddStatus(moved);

        combat.AddLogEntry(
            StandardCombatLogTypes.StatusApplied,
            $"Status '{status.DefinitionId}' stolen from '{from.Id.value}' to '{to.Id.value}'.");
        combat.EnqueueEvent(new StatusAppliedCombatEvent(
            to.Id, moved.Id, moved.DefinitionId, moved.Stacks, moved.DurationTurns, moved.Charges,
            moved.SourceCombatantId));
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
    // The stack change itself, applied to a status instance in place and reported the way the handler does.
    // Shared so a collaborator that must change stacks SYNCHRONOUSLY — a status-application interceptor
    // spending a prohibition, which the next application in the same drain has to see already spent — makes
    // exactly the same state change and raises exactly the same events as the ordinary request path.
    internal static ModifyStatusStacksOutcome ApplyDelta(
        CombatState combat, CombatantState target, StatusInstance status, int delta)
    {
        var oldStacks = status.Stacks;
        var newStacks = (int)Math.Max(0, Math.Min(int.MaxValue, (long)oldStacks + delta));
        var actualDelta = newStacks - oldStacks;

        if (actualDelta == 0)
            return new ModifyStatusStacksOutcome(oldStacks, oldStacks, 0, WasChanged: false, WasRemoved: false);

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

        return new ModifyStatusStacksOutcome(oldStacks, newStacks, actualDelta, WasChanged: true, WasRemoved: wasRemoved);
    }

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

        var outcome = ApplyDelta(combat, target, status, request.Delta);
        if (request.OutcomeSlot is { } outcomeSlot)
            outcomeSlot.Value = outcome;
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

        // AllStatuses: a cleanse sweeps pending notices out too, before they can take hold.
        var statusesToRemove = target.AllStatuses
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

