namespace RogueDeck.Core.Combat;

public sealed class CombatTurnProcessor
{
    private readonly CombatQueueProcessor _queueProcessor = new();

    public void StartCurrentTurn(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CombatExecutionLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(registry);

        EnsureCombatIsOngoing(combat);
        EnsureActiveCombatantCanStartTurn(combat);

        combat.MarkTurnStarted();

        combat.Trace(new TurnStartedTraceEvent(
            combat.CurrentRound, combat.CurrentTurn,
            combat.ActiveCombatantId!.Value));

        combat.AddLogEntry(
            StandardCombatLogTypes.TurnStarted,
            $"Started turn for '{combat.ActiveCombatantId}'.");

        combat.EnqueueEvent(
            new TurnStartedCombatEvent(
                combat.ActiveCombatantId!.Value,
                combat.CurrentRound,
                combat.CurrentTurn));

        _queueProcessor.ResolvePendingQueues(
            combat,
            registry,
            limits);
    }

    public void EndCurrentTurn(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CombatExecutionLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(registry);

        EnsureCombatIsOngoing(combat);
        EnsureActiveCombatantExistsInTurnOrder(combat);

        combat.MarkTurnEnded();

        combat.Trace(new TurnEndedTraceEvent(
            combat.CurrentRound, combat.CurrentTurn,
            combat.ActiveCombatantId!.Value));

        combat.AddLogEntry(
            StandardCombatLogTypes.TurnEnded,
            $"Ended turn for '{combat.ActiveCombatantId}'.");

        combat.EnqueueEvent(
            new TurnEndedCombatEvent(
                combat.ActiveCombatantId!.Value,
                combat.CurrentRound,
                combat.CurrentTurn));

        _queueProcessor.ResolvePendingQueues(
            combat,
            registry,
            limits);

        if (combat.Result != CombatResult.Ongoing)
            return;

        AdvanceToNextCombatant(combat);

        _queueProcessor.ResolvePendingQueues(
            combat,
            registry,
            limits);

        // The combatant we advanced to can be downed by the resolution we just ran (e.g. a round-start
        // trigger that kills it) while the combat continues because other combatants are still alive. Skip
        // any such downed combatant on the hand-off so the next turn always begins on a living combatant.
        var skipGuard = 0;
        while (combat.Result == CombatResult.Ongoing
            && combat.ActiveCombatantId is { } activeId
            && combat.TryGetCombatant(activeId, out var active)
            && !active!.IsAlive
            && skipGuard++ <= combat.TurnOrder.Count)
        {
            AdvanceToNextCombatant(combat);

            _queueProcessor.ResolvePendingQueues(
                combat,
                registry,
                limits);
        }
    }

    public void EndCurrentTurnAndStartNextTurn(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CombatExecutionLimits? limits = null)
    {
        EndCurrentTurn(
            combat,
            registry,
            limits);

        if (combat.Result != CombatResult.Ongoing)
            return;

        StartCurrentTurn(
            combat,
            registry,
            limits);
    }

    private static void EnsureCombatIsOngoing(CombatState combat)
    {
        if (combat.Result != CombatResult.Ongoing)
            throw new InvalidOperationException(
                $"Cannot process turns because combat result is '{combat.Result}'.");
    }

    private static void EnsureActiveCombatantCanStartTurn(CombatState combat)
    {
        EnsureActiveCombatantExistsInTurnOrder(combat);

        var activeCombatantId = combat.ActiveCombatantId!.Value;
        var activeCombatant = combat.GetCombatant(activeCombatantId);

        if (!activeCombatant.IsAlive)
            throw new InvalidOperationException(
                $"Cannot start turn because active combatant '{activeCombatantId}' is not alive.");
    }

    private static void EnsureActiveCombatantExistsInTurnOrder(CombatState combat)
    {
        if (combat.TurnOrder.Count == 0)
            throw new InvalidOperationException("Cannot use turn processor because the combat has no turn order.");

        if (combat.ActiveCombatantId is null)
        {
            foreach (var combatantId in combat.TurnOrder)
            {
                if (!combat.TryGetCombatant(combatantId, out var combatant))
                    continue;

                if (!combatant!.IsAlive)
                    continue;

                combat.SetActiveCombatant(combatantId);
                return;
            }

            throw new InvalidOperationException("Cannot use turn processor because there are no living combatants in the turn order.");
        }

        if (!combat.TurnOrder.Contains(combat.ActiveCombatantId.Value))
            throw new InvalidOperationException(
                $"Active combatant '{combat.ActiveCombatantId}' is not part of the turn order.");

        if (!combat.TryGetCombatant(combat.ActiveCombatantId.Value, out _))
            throw new InvalidOperationException(
                $"Active combatant '{combat.ActiveCombatantId}' does not exist.");
    }

    private static void AdvanceToNextCombatant(CombatState combat)
    {
        EnsureActiveCombatantExistsInTurnOrder(combat);

        var activeId = combat.ActiveCombatantId!.Value;
        var currentIndex = combat.TurnOrder
            .Select((id, i) => (id, i))
            .First(x => x.id == activeId).i;

        for (var offset = 1; offset <= combat.TurnOrder.Count; offset++)
        {
            var nextIndex = (currentIndex + offset) % combat.TurnOrder.Count;
            var nextCombatantId = combat.TurnOrder[nextIndex];

            if (!combat.TryGetCombatant(nextCombatantId, out var nextCombatant))
                continue;

            if (!nextCombatant!.IsAlive)
                continue;

            if (nextIndex <= currentIndex)
            {
                combat.EnqueueEvent(
                    new RoundEndedCombatEvent(
                    combat.CurrentRound,
                    combat.ActiveCombatantId));

                combat.AdvanceRound();

                combat.EnqueueEvent(
                    new RoundStartedCombatEvent(combat.CurrentRound));
            }
            else
            {
                combat.AdvanceTurn();
            }

            combat.SetActiveCombatant(nextCombatantId);
            return;
        }

        throw new InvalidOperationException("Cannot advance turn because there are no living combatants in the turn order.");
    }
}
