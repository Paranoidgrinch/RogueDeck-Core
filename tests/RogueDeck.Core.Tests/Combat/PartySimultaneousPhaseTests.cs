using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Party deckbuilding A2a (simultaneous team phase — state model + transitions): SimultaneousTurnProcessor runs a
// team's whole roster at once (TurnStarted together), each member ends independently, then the phase passes to the
// next team and wraps into a new round. The round-robin CombatTurnProcessor is untouched (flag off ⇒ unchanged).
public class PartySimultaneousPhaseTests
{
    private static readonly TeamId Player = StandardCombatIds.PlayerTeam;
    private static readonly TeamId Enemy = StandardCombatIds.EnemyTeam;
    private static readonly CombatantId P1 = new("p1");
    private static readonly CombatantId P2 = new("p2");
    private static readonly CombatantId E1 = new("e1");

    private sealed class TurnCapture : CombatEventHandler<TurnStartedCombatEvent>
    {
        public List<(CombatantId Id, int Round, int Turn)> Started { get; } = new();
        protected override void Handle(CombatState combat, CombatDefinitionRegistry registry, TurnStartedCombatEvent e) =>
            Started.Add((e.CombatantId, e.Round, e.Turn));
    }

    private sealed class EndCapture : CombatEventHandler<TurnEndedCombatEvent>
    {
        public List<CombatantId> Ended { get; } = new();
        protected override void Handle(CombatState combat, CombatDefinitionRegistry registry, TurnEndedCombatEvent e) =>
            Ended.Add(e.CombatantId);
    }

    private static CombatantState Unit(CombatantId id, TeamId team) =>
        new(id, new CombatantDefinitionId("unit"), "combatant.unit", team, new HealthState(20, 20));

    private static (CombatState combat, CombatDefinitionRegistry registry, TurnCapture started, EndCapture ended) Build(
        params (CombatantId id, TeamId team)[] members)
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var started = new TurnCapture();
        var ended = new EndCapture();
        builder.RegisterCombatEventHandler(started);
        builder.RegisterCombatEventHandler(ended);
        var registry = builder.Build();

        var combat = new CombatState(new CombatId("party"), randomSeed: 1) { SimultaneousTeamTurns = true };
        foreach (var (id, team) in members)
            combat.AddCombatant(Unit(id, team));
        return (combat, registry, started, ended);
    }

    [Fact]
    public void StartTeamPhase_starts_every_living_member_of_the_team_at_once()
    {
        var (combat, registry, started, _) = Build((P1, Player), (P2, Player), (E1, Enemy));
        var proc = new SimultaneousTurnProcessor();

        proc.StartTeamPhase(combat, registry, Player);

        Assert.Equal(Player, combat.CurrentPhaseTeam);
        Assert.Equal(new[] { P1, P2 }, started.Started.Select(s => s.Id)); // both players, not the enemy
        Assert.All(started.Started, s => Assert.Equal((1, 1), (s.Round, s.Turn)));
        Assert.Equal(2, combat.ActivePhaseMembers().Count);
        Assert.False(combat.AllPhaseMembersEnded());
    }

    [Fact]
    public void Members_end_independently_and_the_phase_completes_when_all_have_ended()
    {
        var (combat, registry, _, ended) = Build((P1, Player), (P2, Player), (E1, Enemy));
        var proc = new SimultaneousTurnProcessor();
        proc.StartTeamPhase(combat, registry, Player);

        proc.EndMemberTurn(combat, registry, P1);
        Assert.True(combat.HasMemberEnded(P1));
        Assert.False(combat.AllPhaseMembersEnded());     // p2 still active
        Assert.Equal(new[] { P2 }, combat.ActivePhaseMembers().Select(m => m.Id));

        proc.EndMemberTurn(combat, registry, P1);        // ending again is a no-op
        Assert.Single(ended.Ended);

        proc.EndMemberTurn(combat, registry, P2);
        Assert.True(combat.AllPhaseMembersEnded());
        Assert.Equal(new[] { P1, P2 }, ended.Ended);
    }

    [Fact]
    public void Advancing_hands_the_phase_to_the_next_team_then_wraps_into_a_new_round()
    {
        var (combat, registry, started, _) = Build((P1, Player), (P2, Player), (E1, Enemy));
        var proc = new SimultaneousTurnProcessor();

        proc.StartTeamPhase(combat, registry, Player);
        proc.EndMemberTurn(combat, registry, P1);
        proc.EndMemberTurn(combat, registry, P2);

        proc.AdvanceToNextTeamPhase(combat, registry); // → enemy phase
        Assert.Equal(Enemy, combat.CurrentPhaseTeam);
        Assert.Equal(2, combat.CurrentTurn);            // turn advanced across the phase, round unchanged
        Assert.Equal(1, combat.CurrentRound);
        Assert.Contains(started.Started, s => s.Id == E1 && s.Round == 1 && s.Turn == 2);

        proc.EndMemberTurn(combat, registry, E1);
        proc.AdvanceToNextTeamPhase(combat, registry); // wraps → new round, player phase again
        Assert.Equal(Player, combat.CurrentPhaseTeam);
        Assert.Equal(2, combat.CurrentRound);
        Assert.Equal(4, started.Started.Count(s => s.Id == P1 || s.Id == P2)); // 2 players × 2 rounds
    }

    [Fact]
    public void A_single_member_team_reduces_to_the_familiar_turn_counters()
    {
        // One player + one enemy with the flag on: player phase (turn 1) → enemy phase (turn 2) → wrap → round 2,
        // matching the round-robin counters for the degenerate case.
        var (combat, registry, _, _) = Build((P1, Player), (E1, Enemy));
        var proc = new SimultaneousTurnProcessor();

        proc.StartTeamPhase(combat, registry, Player);
        Assert.Equal((1, 1), (combat.CurrentRound, combat.CurrentTurn));
        proc.EndMemberTurn(combat, registry, P1);

        proc.AdvanceToNextTeamPhase(combat, registry);
        Assert.Equal((1, 2), (combat.CurrentRound, combat.CurrentTurn));
        proc.EndMemberTurn(combat, registry, E1);

        proc.AdvanceToNextTeamPhase(combat, registry);
        Assert.Equal((2, 1), (combat.CurrentRound, combat.CurrentTurn));
    }
}
