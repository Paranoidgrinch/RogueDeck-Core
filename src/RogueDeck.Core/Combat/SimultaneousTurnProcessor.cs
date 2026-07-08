namespace RogueDeck.Core.Combat;

// The turn model for a simultaneous team phase (party deckbuilding A2), the opt-in alternative to the round-robin
// CombatTurnProcessor. A team's whole roster takes its turn at once: every living member gets TurnStarted together
// (drawing its own hand, refilling its own resources through the existing per-combatant handlers), then each member
// ENDS ITS TURN INDEPENDENTLY; when all have ended the phase passes to the next team, wrapping into a new round.
// It does NOT touch the round-robin processor, so a combat with the flag off is byte-for-byte unchanged. It does not
// use the single TurnPhase enum — a simultaneous phase tracks CurrentPhaseTeam + EndedThisPhase on CombatState.
public sealed class SimultaneousTurnProcessor
{
    private readonly CombatQueueProcessor _queues = new();

    // Open a team's phase: every living member starts its turn at once, then the phase awaits each member ending.
    public void StartTeamPhase(
        CombatState combat, CombatDefinitionRegistry registry, TeamId team, CombatExecutionLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(registry);
        EnsureOngoing(combat);

        combat.BeginTeamPhase(team);
        foreach (var member in combat.GetLivingCombatantsOnTeam(team).ToArray())
        {
            combat.SetActiveCombatant(member.Id); // so trigger contexts reading the active combatant see this member
            RaiseTurnStarted(combat, member.Id);
            _queues.ResolvePendingQueues(combat, registry, limits);
            if (combat.Result != CombatResult.Ongoing)
                return;
        }
    }

    // End one member's turn (TurnEnded → discard). A no-op if the member has already ended; guarded to a living
    // member of the current phase team.
    public void EndMemberTurn(
        CombatState combat, CombatDefinitionRegistry registry, CombatantId memberId, CombatExecutionLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(registry);
        EnsureOngoing(combat);

        if (combat.CurrentPhaseTeam is not { } team)
            throw new InvalidOperationException("Cannot end a member turn because no team phase is active.");
        var member = combat.GetCombatant(memberId);
        if (member.TeamId != team)
            throw new InvalidOperationException(
                $"Combatant '{memberId}' is not on the current phase team '{team.value}'.");
        if (combat.HasMemberEnded(memberId))
            return;

        combat.SetActiveCombatant(memberId);
        RaiseTurnEnded(combat, memberId);
        _queues.ResolvePendingQueues(combat, registry, limits);
        combat.MarkMemberEnded(memberId);
    }

    // Whether every living member of the current phase team has ended their turn.
    public bool IsPhaseComplete(CombatState combat) => combat.AllPhaseMembersEnded();

    // Hand the phase to the next team with living members; wrapping to the first team advances the round (mirroring
    // the round-robin round/turn counters), then starts that team's phase.
    public void AdvanceToNextTeamPhase(
        CombatState combat, CombatDefinitionRegistry registry, CombatExecutionLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(combat);
        ArgumentNullException.ThrowIfNull(registry);
        EnsureOngoing(combat);

        if (combat.CurrentPhaseTeam is not { } current)
            throw new InvalidOperationException("Cannot advance because no team phase is active.");

        var teams = TeamOrder(combat);
        var currentIndex = teams.IndexOf(current);

        for (var offset = 1; offset <= teams.Count; offset++)
        {
            var nextIndex = (currentIndex + offset) % teams.Count;
            var next = teams[nextIndex];
            if (!combat.HasLivingCombatantsOnTeam(next))
                continue;

            if (nextIndex <= currentIndex)
            {
                combat.EnqueueEvent(new RoundEndedCombatEvent(combat.CurrentRound, combat.ActiveCombatantId));
                combat.AdvanceRound();
                combat.EnqueueEvent(new RoundStartedCombatEvent(combat.CurrentRound));
                _queues.ResolvePendingQueues(combat, registry, limits);
                if (combat.Result != CombatResult.Ongoing)
                    return;
            }
            else
            {
                combat.AdvanceTurn();
            }

            StartTeamPhase(combat, registry, next, limits);
            return;
        }
    }

    // The distinct teams present, in the order their combatants first appear in the turn order.
    private static List<TeamId> TeamOrder(CombatState combat)
    {
        var seen = new List<TeamId>();
        foreach (var id in combat.TurnOrder)
            if (combat.TryGetCombatant(id, out var c) && c is not null && !seen.Contains(c.TeamId))
                seen.Add(c.TeamId);
        return seen;
    }

    private static void RaiseTurnStarted(CombatState combat, CombatantId id)
    {
        combat.Trace(new TurnStartedTraceEvent(combat.CurrentRound, combat.CurrentTurn, id));
        combat.AddLogEntry(StandardCombatLogTypes.TurnStarted, $"Started turn for '{id}'.");
        combat.EnqueueEvent(new TurnStartedCombatEvent(id, combat.CurrentRound, combat.CurrentTurn));
    }

    private static void RaiseTurnEnded(CombatState combat, CombatantId id)
    {
        combat.Trace(new TurnEndedTraceEvent(combat.CurrentRound, combat.CurrentTurn, id));
        combat.AddLogEntry(StandardCombatLogTypes.TurnEnded, $"Ended turn for '{id}'.");
        combat.EnqueueEvent(new TurnEndedCombatEvent(id, combat.CurrentRound, combat.CurrentTurn));
    }

    private static void EnsureOngoing(CombatState combat)
    {
        if (combat.Result != CombatResult.Ongoing)
            throw new InvalidOperationException($"Cannot process turns because combat result is '{combat.Result}'.");
    }
}
