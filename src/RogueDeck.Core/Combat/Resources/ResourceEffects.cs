namespace RogueDeck.Core.Combat;

// Shared derivation-trace emit for the four native resource operations. Each handler calls this at the
// point it commits a change (alongside the ResourceGained/Lost/Modified/Refilled log), so the trace
// stream mirrors where the coarse log records an outcome. No-op early returns emit no log and no trace.
file static class ResourceChangeTracing
{
    internal static void Emit(
        CombatState combat,
        ResourceChangeKind kind,
        CombatantId combatantId,
        ResourceId resourceId,
        int requestedAmount,
        int appliedDelta,
        int previousCurrent,
        int newCurrent,
        bool reachedMinimum,
        bool reachedMaximum)
    {
        if (combat.TraceListener is null)
            return;

        combat.Trace(new ResourceChangeResolvedTraceEvent(
            combat.CurrentRound, combat.CurrentTurn,
            combatantId, resourceId, kind,
            requestedAmount, appliedDelta, previousCurrent, newCurrent,
            reachedMinimum, reachedMaximum));
    }
}

public sealed record RefillResourceEffectRequest(
    CombatantId CombatantId,
    ResourceId ResourceId,
    int DefaultMax,
    RefillResourceOutcomeSlot? OutcomeSlot = null
) : IEffectRequest;

public sealed class RefillResourceEffectHandler : EffectRequestHandler<RefillResourceEffectRequest>
{
    protected override void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        RefillResourceEffectRequest request)
    {
        if (request.DefaultMax < 0)
            throw new ArgumentOutOfRangeException(nameof(request.DefaultMax), "Default max cannot be negative.");

        var combatant = combat.GetCombatant(request.CombatantId);

        if (!combatant.Resources.TryGetValue(request.ResourceId, out var resource))
        {
            combatant.AddResource(
                request.ResourceId,
                new ValuePoolState(
                    current: request.DefaultMax,
                    max: request.DefaultMax));

            if (request.OutcomeSlot is { } createdSlot)
                createdSlot.Value = new RefillResourceOutcome(
                    PreviousCurrent: 0,
                    NewCurrent: request.DefaultMax,
                    DefaultMax: request.DefaultMax);

            combat.AddLogEntry(
                StandardCombatLogTypes.ResourceRefilled,
                $"Created and refilled resource '{request.ResourceId}' on '{request.CombatantId}' to {request.DefaultMax}.");

            ResourceChangeTracing.Emit(combat, ResourceChangeKind.Refilled,
                request.CombatantId, request.ResourceId,
                requestedAmount: request.DefaultMax, appliedDelta: request.DefaultMax,
                previousCurrent: 0, newCurrent: request.DefaultMax,
                reachedMinimum: false, reachedMaximum: true);

            combat.EnqueueEvent(new ResourceRefilledCombatEvent(
                request.CombatantId,
                request.ResourceId,
                PreviousCurrent: 0,
                NewCurrent: request.DefaultMax,
                Max: request.DefaultMax));

            return;
        }

        var previousCurrent = resource.Current;
        var refillTarget = resource.Max ?? request.DefaultMax;

        resource.SetCurrent(refillTarget);

        if (request.OutcomeSlot is { } refillSlot)
            refillSlot.Value = new RefillResourceOutcome(
                PreviousCurrent: previousCurrent,
                NewCurrent: refillTarget,
                DefaultMax: request.DefaultMax);

        combat.AddLogEntry(
            StandardCombatLogTypes.ResourceRefilled,
            $"Refilled resource '{request.ResourceId}' on '{request.CombatantId}' from {previousCurrent} to {refillTarget}.");

        ResourceChangeTracing.Emit(combat, ResourceChangeKind.Refilled,
            request.CombatantId, request.ResourceId,
            requestedAmount: refillTarget, appliedDelta: refillTarget - previousCurrent,
            previousCurrent: previousCurrent, newCurrent: refillTarget,
            reachedMinimum: false, reachedMaximum: true);

        combat.EnqueueEvent(new ResourceRefilledCombatEvent(
            request.CombatantId,
            request.ResourceId,
            PreviousCurrent: previousCurrent,
            NewCurrent: refillTarget,
            Max: resource.Max));
    }
}

public sealed record GainResourceEffectRequest(
    CombatantId CombatantId,
    ResourceId ResourceId,
    int Amount,
    int? DefaultMax = null,
    GainResourceOutcomeSlot? OutcomeSlot = null
) : IEffectRequest;

public sealed class GainResourceEffectHandler : EffectRequestHandler<GainResourceEffectRequest>
{
    protected override void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        GainResourceEffectRequest request)
    {
        if (request.Amount < 0)
            throw new ArgumentOutOfRangeException(nameof(request.Amount), "Resource gain amount cannot be negative.");

        if (request.DefaultMax is < 0)
            throw new ArgumentOutOfRangeException(nameof(request.DefaultMax), "Default max cannot be negative.");

        if (request.Amount == 0)
        {
            var zeroTarget = combat.GetCombatant(request.CombatantId);
            var zeroCurrent = zeroTarget.Resources.TryGetValue(request.ResourceId, out var zeroPool)
                ? zeroPool.Current
                : 0;
            if (request.OutcomeSlot is { } zeroSlot)
                zeroSlot.Value = new GainResourceOutcome(0, 0, zeroCurrent, zeroCurrent, false);
            return;
        }

        var combatant = combat.GetCombatant(request.CombatantId);

        if (!combatant.Resources.TryGetValue(request.ResourceId, out var resource))
        {
            var createdCurrent = request.Amount;

            if (request.DefaultMax.HasValue)
                createdCurrent = Math.Min(createdCurrent, request.DefaultMax.Value);

            combatant.AddResource(
                request.ResourceId,
                new ValuePoolState(
                    current: createdCurrent,
                    max: request.DefaultMax));

            if (createdCurrent == 0)
            {
                if (request.OutcomeSlot is { } cappedZeroSlot)
                    cappedZeroSlot.Value = new GainResourceOutcome(
                        RequestedAmount: request.Amount,
                        GainedAmount: 0,
                        PreviousCurrent: 0,
                        NewCurrent: 0,
                        ReachedMaximum: request.DefaultMax.HasValue && request.DefaultMax.Value == 0);
                return;
            }

            if (request.OutcomeSlot is { } createdSlot)
                createdSlot.Value = new GainResourceOutcome(
                    RequestedAmount: request.Amount,
                    GainedAmount: createdCurrent,
                    PreviousCurrent: 0,
                    NewCurrent: createdCurrent,
                    ReachedMaximum: request.DefaultMax.HasValue && createdCurrent == request.DefaultMax.Value);

            combat.AddLogEntry(
                StandardCombatLogTypes.ResourceGained,
                $"Created resource '{request.ResourceId}' on '{request.CombatantId}' and gained {createdCurrent}.");

            ResourceChangeTracing.Emit(combat, ResourceChangeKind.Gained,
                request.CombatantId, request.ResourceId,
                requestedAmount: request.Amount, appliedDelta: createdCurrent,
                previousCurrent: 0, newCurrent: createdCurrent,
                reachedMinimum: false,
                reachedMaximum: request.DefaultMax.HasValue && createdCurrent == request.DefaultMax.Value);

            combat.EnqueueEvent(new ResourceGainedCombatEvent(
                request.CombatantId,
                request.ResourceId,
                PreviousCurrent: 0,
                NewCurrent: createdCurrent,
                GainedAmount: createdCurrent,
                Max: request.DefaultMax));

            return;
        }

        var previousCurrent = resource.Current;
        var requestedCurrent = (long)previousCurrent + request.Amount;

        if (resource.Max.HasValue && !resource.CanExceedMax)
            requestedCurrent = Math.Min(requestedCurrent, resource.Max.Value);

        if (requestedCurrent > int.MaxValue)
            throw new OverflowException("Resource value exceeded Int32.MaxValue.");

        var newCurrent = (int)requestedCurrent;
        var gainedAmount = newCurrent - previousCurrent;

        if (gainedAmount <= 0)
        {
            if (request.OutcomeSlot is { } zeroSlot)
                zeroSlot.Value = new GainResourceOutcome(
                    RequestedAmount: request.Amount,
                    GainedAmount: 0,
                    PreviousCurrent: previousCurrent,
                    NewCurrent: previousCurrent,
                    ReachedMaximum: resource.Max.HasValue && previousCurrent == resource.Max.Value);
            return;
        }

        resource.SetCurrent(newCurrent);

        if (request.OutcomeSlot is { } slot)
            slot.Value = new GainResourceOutcome(
                RequestedAmount: request.Amount,
                GainedAmount: gainedAmount,
                PreviousCurrent: previousCurrent,
                NewCurrent: newCurrent,
                ReachedMaximum: resource.Max.HasValue && newCurrent == resource.Max.Value);

        combat.AddLogEntry(
            StandardCombatLogTypes.ResourceGained,
            $"Combatant '{request.CombatantId}' gained {gainedAmount} resource '{request.ResourceId}'.");

        ResourceChangeTracing.Emit(combat, ResourceChangeKind.Gained,
            request.CombatantId, request.ResourceId,
            requestedAmount: request.Amount, appliedDelta: gainedAmount,
            previousCurrent: previousCurrent, newCurrent: newCurrent,
            reachedMinimum: false,
            reachedMaximum: resource.Max.HasValue && newCurrent == resource.Max.Value);

        combat.EnqueueEvent(new ResourceGainedCombatEvent(
            request.CombatantId,
            request.ResourceId,
            PreviousCurrent: previousCurrent,
            NewCurrent: newCurrent,
            GainedAmount: gainedAmount,
            Max: resource.Max));
    }
}

// Explicit, non-cost resource loss. Subtracts up to Amount from the resource (floored at 0) and
// emits the distinct ResourceLostCombatEvent / ResourceLost log when anything is actually lost.
// A missing resource, a zero/empty resource, or Amount == 0 is a legal no-op (LostAmount 0, no event).
public sealed record LoseResourceEffectRequest(
    CombatantId CombatantId,
    ResourceId ResourceId,
    int Amount,
    LoseResourceOutcomeSlot? OutcomeSlot = null
) : IEffectRequest;

public sealed class LoseResourceEffectHandler : EffectRequestHandler<LoseResourceEffectRequest>
{
    protected override void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        LoseResourceEffectRequest request)
    {
        if (request.Amount < 0)
            throw new ArgumentOutOfRangeException(nameof(request.Amount), "Resource loss amount cannot be negative.");

        var combatant = combat.GetCombatant(request.CombatantId);

        var hasResource = combatant.Resources.TryGetValue(request.ResourceId, out var resource);
        var previousCurrent = hasResource ? resource!.Current : 0;

        var newCurrent = previousCurrent - request.Amount;
        if (newCurrent < 0) newCurrent = 0;
        var lostAmount = previousCurrent - newCurrent;

        if (lostAmount <= 0)
        {
            if (request.OutcomeSlot is { } noopSlot)
                noopSlot.Value = new LoseResourceOutcome(
                    RequestedAmount: request.Amount,
                    LostAmount: 0,
                    PreviousCurrent: previousCurrent,
                    NewCurrent: previousCurrent,
                    ReachedZero: previousCurrent == 0);
            return;
        }

        resource!.SetCurrent(newCurrent);

        if (request.OutcomeSlot is { } slot)
            slot.Value = new LoseResourceOutcome(
                RequestedAmount: request.Amount,
                LostAmount: lostAmount,
                PreviousCurrent: previousCurrent,
                NewCurrent: newCurrent,
                ReachedZero: newCurrent == 0);

        combat.AddLogEntry(
            StandardCombatLogTypes.ResourceLost,
            $"Combatant '{request.CombatantId}' lost {lostAmount} resource '{request.ResourceId}' ({previousCurrent} → {newCurrent}).");

        ResourceChangeTracing.Emit(combat, ResourceChangeKind.Lost,
            request.CombatantId, request.ResourceId,
            requestedAmount: request.Amount, appliedDelta: -lostAmount,
            previousCurrent: previousCurrent, newCurrent: newCurrent,
            reachedMinimum: newCurrent == 0, reachedMaximum: false);

        combat.EnqueueEvent(new ResourceLostCombatEvent(
            request.CombatantId,
            request.ResourceId,
            PreviousCurrent: previousCurrent,
            NewCurrent: newCurrent,
            LostAmount: lostAmount,
            Max: resource.Max));
    }
}

public sealed record ModifyResourceEffectRequest(
    CombatantId CombatantId,
    ResourceId ResourceId,
    int Delta,
    int? Min = null,
    int? Max = null,
    ModifyResourceOutcomeSlot? OutcomeSlot = null
) : IEffectRequest;

public sealed class ModifyResourceEffectHandler : EffectRequestHandler<ModifyResourceEffectRequest>
{
    protected override void Resolve(
        CombatState combat,
        CombatDefinitionRegistry registry,
        ModifyResourceEffectRequest request)
    {
        var combatant = combat.GetCombatant(request.CombatantId);

        var previous = 0;
        var current = 0;

        if (combatant.Resources.TryGetValue(request.ResourceId, out var pool))
        {
            previous = pool.Current;

            var raw = (long)previous + request.Delta;

            if (request.Min.HasValue) raw = Math.Max(raw, request.Min.Value);
            if (request.Max.HasValue) raw = Math.Min(raw, request.Max.Value);
            // The pool's OWN max is always the hard ceiling — a request.Max override is a clamp, it does NOT
            // raise the pool's cap, so the result can never exceed pool.Max (setting current above it throws).
            if (pool.Max.HasValue) raw = Math.Min(raw, pool.Max.Value);

            raw = Math.Max(raw, 0);

            if (raw > int.MaxValue)
                throw new OverflowException("Resource value exceeded Int32.MaxValue.");

            current = (int)raw;
            pool.SetCurrent(current);
        }
        else if (request.Delta > 0)
        {
            current = request.Delta;
            if (request.Min.HasValue) current = Math.Max(current, request.Min.Value);
            if (request.Max.HasValue) current = Math.Min(current, request.Max.Value);

            combatant.AddResource(request.ResourceId, new ValuePoolState(current: current, max: request.Max));
        }

        var applied = current - previous;
        var effectiveMin = request.Min ?? 0;
        var effectiveMax = request.Max ?? (combatant.Resources.TryGetValue(request.ResourceId, out var p) ? p.Max : null);

        if (request.OutcomeSlot is { } slot)
            slot.Value = new ModifyResourceOutcome(
                RequestedDelta: request.Delta,
                AppliedDelta: applied,
                PreviousValue: previous,
                CurrentValue: current,
                ReachedMinimum: current == effectiveMin,
                ReachedMaximum: effectiveMax.HasValue && current == effectiveMax.Value,
                WasChanged: applied != 0);

        // A general modification is not a gain — emit the distinct ResourceModified event/log so
        // triggers and UI can tell a modify (which may be a loss) from a gain.
        if (applied != 0)
        {
            combat.AddLogEntry(
                StandardCombatLogTypes.ResourceModified,
                $"Modified resource '{request.ResourceId}' on '{request.CombatantId}' by {applied} ({previous} → {current}).");

            ResourceChangeTracing.Emit(combat, ResourceChangeKind.Modified,
                request.CombatantId, request.ResourceId,
                requestedAmount: request.Delta, appliedDelta: applied,
                previousCurrent: previous, newCurrent: current,
                reachedMinimum: current == effectiveMin,
                reachedMaximum: effectiveMax.HasValue && current == effectiveMax.Value);

            combat.EnqueueEvent(new ResourceModifiedCombatEvent(
                CombatantId: request.CombatantId,
                ResourceId: request.ResourceId,
                PreviousCurrent: previous,
                NewCurrent: current,
                AppliedDelta: applied,
                Max: effectiveMax));
        }
    }
}

