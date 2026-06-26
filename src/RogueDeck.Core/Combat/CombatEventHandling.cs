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
                    $"Stopped resolving pending queues after reaching the limit of {limits.MaxQueueCycles} cycles.");

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
}
