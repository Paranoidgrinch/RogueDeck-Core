using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Scenario.Scripting;

// How an enemy chooses which player-team member to hit in a party fight (party deckbuilding B2d). All strategies
// consider only LIVING players (a downed member is out for the fight) and break ties by roster order, so every
// pick is deterministic and reproducible. Random draws from the combat RNG; Nearest needs board positions and
// falls back to FirstAlive for an enemy that has none.
public enum PartyEnemyTargeting
{
    FirstAlive,    // the first living player in roster order — the historical default
    LowestHealth,  // the most wounded living player (focus-fire the weak)
    HighestHealth, // the healthiest living player (a crude "biggest threat")
    Random,        // a uniformly random living player, drawn from the combat RNG
    Nearest,       // the living player at least Manhattan distance from the acting enemy (positional)
}

// A stepwise driver for the simultaneous team-phase combat (party deckbuilding A2c/A3), the party counterpart of
// the hero-centric InteractiveCombat. Every player-team member is "in its turn" at once: the caller plays cards for
// ANY active member from that member's own hand/energy, and each member ends independently. When all living members
// have ended, the enemy phase runs (each enemy acts its intent, targeting a player), then a fresh player phase
// begins. Requires the compiled scenario to have SimultaneousTeamTurns on. Single-threaded and deterministic by the
// order calls are made — the multiplayer seam: concurrent players' inputs are just interleaved calls here.
public sealed class PartyCombat
{
    private static readonly TeamId PlayerTeam = StandardCombatIds.PlayerTeam;
    private static readonly TeamId EnemyTeam = StandardCombatIds.EnemyTeam;

    private readonly CombatState _combat;
    private readonly CombatDefinitionRegistry _registry;
    private readonly Func<CombatState, CombatantId, int, EnemyActionDefinitionId?> _enemyIntent;
    private readonly PartyEnemyTargeting _targeting;
    private int _targetStep;
    private readonly SimultaneousTurnProcessor _phases = new();
    private readonly CombatQueueProcessor _queues = new();

    public PartyCombat(
        CompiledScenario compiled,
        Func<CombatState, CombatantId, int, EnemyActionDefinitionId?> enemyIntent,
        string combatId = "party",
        int randomSeed = 1,
        PartyEnemyTargeting targeting = PartyEnemyTargeting.FirstAlive)
    {
        ArgumentNullException.ThrowIfNull(compiled);
        ArgumentNullException.ThrowIfNull(enemyIntent);
        if (!compiled.SimultaneousTeamTurns)
            throw new InvalidOperationException(
                "PartyCombat requires a scenario with SimultaneousTeamTurns; use InteractiveCombat otherwise.");

        _registry = compiled.Registry;
        _enemyIntent = enemyIntent;
        _targeting = targeting;
        _combat = ScenarioCombatFactory.Build(compiled, combatId, randomSeed);

        // Open the first player phase: every player-team member starts its turn and draws its own opening hand.
        _phases.StartTeamPhase(_combat, _registry, PlayerTeam);
    }

    // ── State views ──────────────────────────────────────────────────────────────

    public CombatState State => _combat;
    public CombatResult Result => _combat.Result;
    public bool IsOver => _combat.Result != CombatResult.Ongoing;
    public int Round => _combat.CurrentRound;

    // The player members still able to act this phase (living, not yet ended).
    public IReadOnlyList<CombatantId> ActiveMembers() =>
        _combat.CurrentPhaseTeam == PlayerTeam
            ? _combat.ActivePhaseMembers().Select(m => m.Id).ToArray()
            : [];

    public bool HasEnded(CombatantId memberId) => _combat.HasMemberEnded(memberId);

    public IReadOnlyList<CardInstance> HandOf(CombatantId memberId) =>
        _combat.GetCardZones(memberId).GetCardsInZone(CardZone.Hand);

    public int EnergyOf(CombatantId memberId) =>
        _combat.GetCombatant(memberId).Resources.TryGetValue(StandardCombatIds.EnergyResource, out var pool)
            ? pool.Current : 0;

    // ── Actions ──────────────────────────────────────────────────────────────────

    // Play a card from one member's hand. No-op unless that member is currently an active player (in the player
    // phase, alive, not yet ended) and holds the card.
    public void PlayCard(CombatantId memberId, CardInstanceId cardInstanceId, CombatantId? target)
    {
        if (IsOver || !ActiveMembers().Contains(memberId))
            return;
        if (!_combat.GetCardZones(memberId).ContainsCard(cardInstanceId))
            return;

        _combat.EnqueueEffect(new PlayCardEffectRequest(memberId, cardInstanceId, target));
        _queues.ResolvePendingQueues(_combat, _registry);
    }

    // End one member's turn. When the last living player member ends, the enemy phase runs and a fresh player phase
    // begins (unless the combat has ended).
    public void EndTurn(CombatantId memberId)
    {
        if (IsOver || !ActiveMembers().Contains(memberId))
            return;

        _phases.EndMemberTurn(_combat, _registry, memberId);

        if (!IsOver && _phases.IsPhaseComplete(_combat))
            RunEnemyPhaseThenNextPlayerPhase();
    }

    private void RunEnemyPhaseThenNextPlayerPhase()
    {
        // Players all ended → hand off to the enemy phase (every enemy gets TurnStarted at once).
        _phases.AdvanceToNextTeamPhase(_combat, _registry);
        if (IsOver)
            return;

        // Each living enemy acts its intent this round, targeting a player, then ends its turn.
        foreach (var enemy in _combat.GetLivingCombatantsOnTeam(EnemyTeam).ToArray())
        {
            if (IsOver)
                return;
            if (!enemy.IsAlive)
                continue;

            var actionId = _enemyIntent(_combat, enemy.Id, _combat.CurrentRound);
            if (actionId is { } id && _registry.TryGetEnemyAction(id, out _) && EnemyTarget(enemy.Id) is { } targetId)
            {
                _combat.EnqueueEffect(new ExecuteEnemyActionEffectRequest(enemy.Id, id, targetId));
                _queues.ResolvePendingQueues(_combat, _registry);
            }

            if (!IsOver && !_combat.HasMemberEnded(enemy.Id))
                _phases.EndMemberTurn(_combat, _registry, enemy.Id);
        }

        // Enemy phase complete → wrap to a fresh player phase (new round; players draw again).
        if (!IsOver)
            _phases.AdvanceToNextTeamPhase(_combat, _registry);
    }

    // Which player the acting enemy hits, per the configured targeting strategy (party deckbuilding B2d). Only
    // living players are candidates; ties break by roster order so the pick stays deterministic.
    private CombatantId? EnemyTarget(CombatantId enemyId)
    {
        var players = _combat.GetLivingCombatantsOnTeam(PlayerTeam).ToArray();
        if (players.Length == 0)
            return null;

        return _targeting switch
        {
            PartyEnemyTargeting.LowestHealth => Extreme(players, lowest: true).Id,
            PartyEnemyTargeting.HighestHealth => Extreme(players, lowest: false).Id,
            PartyEnemyTargeting.Random => players[
                CombatRandom.CreateShuffledIndexes(players.Length, _combat.RandomSeed, _targetStep++)[0]].Id,
            PartyEnemyTargeting.Nearest => Nearest(players, enemyId).Id,
            _ => players[0].Id, // FirstAlive
        };
    }

    // The living player with the min (or max) current HP, first in roster order on a tie.
    private static CombatantState Extreme(IReadOnlyList<CombatantState> players, bool lowest)
    {
        var best = players[0];
        foreach (var player in players.Skip(1))
        {
            var better = lowest
                ? player.Health.Current < best.Health.Current
                : player.Health.Current > best.Health.Current;
            if (better)
                best = player;
        }
        return best;
    }

    // The living player at least Manhattan distance from the acting enemy; falls back to roster order when the
    // enemy (or every candidate) has no board position, and breaks ties by roster order.
    private CombatantState Nearest(IReadOnlyList<CombatantState> players, CombatantId enemyId)
    {
        if (_combat.GetCombatant(enemyId).Position is not { } from)
            return players[0];

        var best = players[0];
        var bestDistance = int.MaxValue;
        foreach (var player in players)
        {
            if (player.Position is not { } at)
                continue;
            var distance = Math.Abs(at.X - from.X) + Math.Abs(at.Y - from.Y);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = player;
            }
        }
        return best;
    }
}
