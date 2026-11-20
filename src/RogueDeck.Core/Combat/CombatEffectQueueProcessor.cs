namespace RogueDeck.Core.Combat;

public sealed class CombatEffectQueueProcessor
{
    private readonly CombatEffectResolver _resolver = new();

    public void ResolvePendingEffects(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CombatExecutionLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(registry);

        // Bind the registry so program expressions can read definition data (e.g. card cost).
        combat.DefinitionRegistry = registry;

        limits ??= CombatExecutionLimits.Default;

        var resolvedEffects = 0;

        while (combat.HasPendingEffects && combat.Result == CombatResult.Ongoing)
        {
            if (resolvedEffects >= limits.MaxEffectsPerCycle)
                throw new InvalidOperationException(
                    $"Stopped resolving pending effects after reaching the limit of {limits.MaxEffectsPerCycle} effects per cycle.");

            var entry = combat.DequeueNextEffectEntry();

            try
            {
                using (combat.EnterEffectChain(entry.EffectChain))
                    _resolver.Resolve(combat, registry, entry.Request);
            }
            catch (Exception ex)
            {
                // A native handler threw while resolving an effect a program enqueued. Bind the
                // failure to the owning frame (faults it, emits ProgramFaulted, runs terminal
                // cleanup) before propagating, so the frame never stays Running.
                if (entry.OwningProgramExecutionId is { } owner
                    && combat.TryGetActiveProgramFrame(owner, out var frame)
                    && frame is not null)
                {
                    frame.FaultDueToNativeHandler(ex);
                    combat.UnregisterActiveProgramFrame(frame);
                }

                throw;
            }

            combat.Trace(new EffectResolvedTraceEvent(
                combat.CurrentRound, combat.CurrentTurn,
                entry.Request.GetType().Name,
                entry.EffectChain.Id.Value));

            resolvedEffects++;
        }

        // ★ THE BOOKS ARE CLOSED AFTER THE FIGHT IS DECIDED. The blow that drops the last enemy queues the
        // victory first and everything answering the fall after it — so a rule that tallies what that fall was
        // worth (a fee per papered corpse, a bounty for an exact kill) never got to write it down, and in a
        // fight against one enemy it never paid at all (relic audit 2026-11-19). Counter bookkeeping already
        // queued is still written; everything else that was queued stays where it is, unresolved, as before.
        if (combat.Result != CombatResult.Ongoing && combat.HasPendingEffects)
        {
            var waiting = new List<PendingEffectQueueEntry>();
            while (combat.HasPendingEffects)
                waiting.Add(combat.DequeueNextEffectEntry());
            foreach (var entry in waiting)
            {
                if (entry.Request is not SetCombatantCounterEffectRequest)
                {
                    combat.RequeueEffectEntry(entry);
                    continue;
                }
                using (combat.EnterEffectChain(entry.EffectChain))
                    _resolver.Resolve(combat, registry, entry.Request);
                combat.Trace(new EffectResolvedTraceEvent(
                    combat.CurrentRound, combat.CurrentTurn,
                    entry.Request.GetType().Name,
                    entry.EffectChain.Id.Value));
            }
        }
    }
}
