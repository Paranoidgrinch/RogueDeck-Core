namespace RogueDeck.Core.Combat;

public interface ICombatEventHandler
{
    Type EventType { get; }

    void Handle(
        CombatState combat,
        CombatDefinitionRegistry registry,
        ICombatEvent combatEvent);
}

public abstract class CombatEventHandler<TEvent> : ICombatEventHandler
    where TEvent : ICombatEvent
{
    public Type EventType => typeof(TEvent);

    public void Handle(
        CombatState combat,
        CombatDefinitionRegistry registry,
        ICombatEvent combatEvent)
    {
        if (combatEvent is not TEvent typedEvent)
            throw new ArgumentException(
                $"Expected event type '{typeof(TEvent).Name}'.",
                nameof(combatEvent));

        Handle(combat, registry, typedEvent);
    }

    protected abstract void Handle(
        CombatState combat,
        CombatDefinitionRegistry registry,
        TEvent combatEvent);
}

public sealed class CombatEventQueueProcessor
{
    public void ResolvePendingEvents(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CombatExecutionLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(registry);

        limits ??= CombatExecutionLimits.Default;

        var resolvedEvents = 0;

        while (combat.HasPendingEvents && combat.Result == CombatResult.Ongoing)
        {
            if (resolvedEvents >= limits.MaxEventsPerCycle)
                throw new InvalidOperationException(
                    $"Stopped resolving pending events after reaching the limit of {limits.MaxEventsPerCycle} events per cycle.");

            var entry = combat.DequeueNextEventEntry();
            var handlers = registry.GetCombatEventHandlers(entry.CombatEvent.GetType());

            using (combat.EnterEffectChain(entry.EffectChain))
            {
                foreach (var handler in handlers)
                    handler.Handle(combat, registry, entry.CombatEvent);
            }

            combat.Trace(new CombatEventDispatchedTraceEvent(
                combat.CurrentRound, combat.CurrentTurn,
                entry.CombatEvent.GetType().Name,
                handlers.Count));

            resolvedEvents++;
        }
    }
}

public sealed class CombatQueueProcessor
{
    private readonly CombatEffectQueueProcessor _effectQueueProcessor = new();
    private readonly CombatEventQueueProcessor _eventQueueProcessor = new();

    public void ResolvePendingQueues(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CombatExecutionLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(registry);

        // Bind the registry so expressions evaluated during processing can read definition data
        // (e.g. a card's resource cost via CardCostExpression).
        combat.DefinitionRegistry = registry;

        limits ??= CombatExecutionLimits.Default;

        var resolvedCycles = 0;

        while ((combat.HasPendingEffects || combat.HasPendingEvents || combat.HasPendingContinuations) && combat.Result == CombatResult.Ongoing)
        {
            if (resolvedCycles >= limits.MaxQueueCycles)
                throw new InvalidOperationException(
                    $"Stopped resolving pending queues after reaching the limit of {limits.MaxQueueCycles} cycles."
                    + WhatIsLooping(combat));

            _effectQueueProcessor.ResolvePendingEffects(combat, registry, limits);
            _eventQueueProcessor.ResolvePendingEvents(combat, registry, limits);

            if (!combat.HasPendingEffects && !combat.HasPendingEvents
                && combat.TryDequeueContinuation(out var continuation))
            {
                continuation!(combat);
            }

            resolvedCycles++;
        }
    }

    // What the fight was carrying at the moment it was stopped. A cycle limit is only ever reached by a loop,
    // and a loop is a handful of requests handing each other back and forth for ever — so the fastest way to
    // name it is to say what is standing in the queues and what the fight said last. Without this the message
    // reports only that something went round in circles, which is the one thing already known.
    private static string WhatIsLooping(CombatState combat)
    {
        static string Short(object item)
        {
            var text = item.ToString() ?? item.GetType().Name;
            return text.Length <= 200 ? text : string.Concat(text.AsSpan(0, 200), "…");
        }

        var report = new System.Text.StringBuilder();
        report.Append($" Pending: {combat.PendingEffectCount} effect(s), {combat.PendingEventCount} event(s)");
        if (combat.HasPendingContinuations)
            report.Append(", continuations waiting");
        report.Append('.');
        foreach (var effect in combat.PendingEffects.Take(PendingShown))
            report.Append($" [effect] {Short(effect)}");
        foreach (var pending in combat.PendingEvents.Take(PendingShown))
            report.Append($" [event] {Short(pending)}");
        foreach (var entry in combat.CombatLog.TakeLast(LogTailShown))
            report.Append($" [log] {entry.Type}: {entry.Message}");
        return report.ToString();
    }

    private const int PendingShown = 6;
    private const int LogTailShown = 10;
}
