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
    Random,        // a uniformly random living player, drawn per round and enemy from the combat's seed
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
    private readonly CompiledScenario _compiled;
    private readonly CombatDefinitionRegistry _registry;
    private readonly Func<CombatState, CombatantId, int, EnemyActionDefinitionId?> _enemyIntent;
    private readonly PartyEnemyTargeting _targeting;
    private readonly SimultaneousTurnProcessor _phases = new();
    private readonly CombatQueueProcessor _queues = new();

    public PartyCombat(
        CompiledScenario compiled,
        Func<CombatState, CombatantId, int, EnemyActionDefinitionId?> enemyIntent,
        string combatId = "party",
        int randomSeed = 1,
        PartyEnemyTargeting? targeting = null,
        bool startOpeningPhase = true)
    {
        ArgumentNullException.ThrowIfNull(compiled);
        ArgumentNullException.ThrowIfNull(enemyIntent);
        if (!compiled.SimultaneousTeamTurns)
            throw new InvalidOperationException(
                "PartyCombat requires a scenario with SimultaneousTeamTurns; use InteractiveCombat otherwise.");

        _compiled = compiled;
        _registry = compiled.Registry;
        _enemyIntent = enemyIntent;
        _targeting = targeting ?? compiled.PartyTargeting; // unnamed: the scenario's
        _combat = ScenarioCombatFactory.Build(compiled, combatId, randomSeed);
        InstallTelegraph(compiled);

        // Open the first player phase: every player-team member starts its turn and draws its own opening hand.
        if (startOpeningPhase)
            StartOpeningPhase();
    }

    // Resume a party fight from a RESTORED CombatState (a checkpoint or save taken at a phase boundary). The
    // state is rebuilt by the caller with CombatState.Restore against the SAME content as `compiled`, and it
    // already stands in its player phase with the hands dealt, so nothing is opened here.
    public PartyCombat(
        CompiledScenario compiled,
        CombatState restoredState,
        Func<CombatState, CombatantId, int, EnemyActionDefinitionId?> enemyIntent,
        PartyEnemyTargeting? targeting = null)
    {
        ArgumentNullException.ThrowIfNull(compiled);
        ArgumentNullException.ThrowIfNull(restoredState);
        ArgumentNullException.ThrowIfNull(enemyIntent);
        if (!compiled.SimultaneousTeamTurns)
            throw new InvalidOperationException(
                "PartyCombat requires a scenario with SimultaneousTeamTurns; use InteractiveCombat otherwise.");

        _compiled = compiled;
        _registry = compiled.Registry;
        _enemyIntent = enemyIntent;
        _targeting = targeting ?? compiled.PartyTargeting; // unnamed: the scenario's
        _combat = restoredState;
        InstallTelegraph(compiled);
    }

    // Let combat programs read the telegraph: a card that asks "does the target intend to Attack" gets the same
    // projection the UI renders. Player-team members do not telegraph.
    private void InstallTelegraph(CompiledScenario compiled) =>
        _combat.SetUpcomingIntentKind((state, combatant) =>
            state.TryGetCombatant(combatant, out var c) && c is not null && c.TeamId == PlayerTeam
                ? null
                : _enemyIntent(state, combatant, state.CurrentRound) is { } actionId
                    ? compiled.IntentFor(actionId)?.Kind.ToString()
                    : null);

    // Open the first player phase — see InteractiveCombat.StartOpeningTurn for why this is separable: a rule
    // that raises a prompt as the opening hands are dealt needs its chooser installed before they are.
    public void StartOpeningPhase() => _phases.StartTeamPhase(_combat, _registry, PlayerTeam);

    // ── State views ──────────────────────────────────────────────────────────────

    public CombatState State => _combat;
    public CompiledScenario Compiled => _compiled;
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

    // One member's own actions in this fight (its character's: HeroBlueprint.Actions for the hero, the projected
    // AllyBlueprint.Actions for everyone else), in the order the character lists them.
    public IReadOnlyList<CardDefinitionId> ActionsOf(CombatantId memberId) =>
        memberId == _compiled.Hero.CombatantId
            ? _compiled.Hero.Actions
            : _compiled.Allies.FirstOrDefault(a => a.CombatantId == memberId)?.Actions ?? [];

    // Whether a member could use one of its actions right now: it is that member's, it is in the phase, a rule
    // allows it, and the member can pay for it.
    public bool CanUse(CombatantId memberId, CardDefinitionId actionId, CombatantId? target = null)
    {
        if (IsOver || !ActiveMembers().Contains(memberId) || !ActionsOf(memberId).Contains(actionId)
            || !_registry.CardDefinitions.TryGetValue(actionId, out var action) || !action.IsAction)
            return false;
        var member = _combat.GetCombatant(memberId);
        if (!CombatCardPlayProcessor.IsCardPlayAllowed(_combat, _registry, action, member, target, cardInstanceId: null))
            return false;
        return ActionCost(memberId, actionId, target)
            .All(cost => member.Resources.TryGetValue(cost.ResourceId, out var pool) && pool.Current >= cost.Amount);
    }

    // What one member's action costs right now, every modifier applied.
    public IReadOnlyList<CalculatedResourceCost> ActionCost(
        CombatantId memberId, CardDefinitionId actionId, CombatantId? target = null) =>
        _registry.CardDefinitions.TryGetValue(actionId, out var action)
            ? CombatCardPlayProcessor.PreviewCosts(_combat, _registry, action, _combat.GetCombatant(memberId), target)
            : [];

    // ── Actions ──────────────────────────────────────────────────────────────────

    // Use one of a member's own actions — the action counterpart of PlayCard. No-op unless the member is in the
    // phase and the action is its own; a prompt the action raises parks the replay from the caller's loop.
    public void UseAction(CombatantId memberId, CardDefinitionId actionId, CombatantId? target)
    {
        if (IsOver || !ActiveMembers().Contains(memberId) || !ActionsOf(memberId).Contains(actionId))
            return;

        _combat.EnqueueEffect(new UseActionEffectRequest(memberId, actionId, target, new UseActionOutcomeSlot()));
        _queues.ResolvePendingQueues(_combat, _registry);
    }

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

    // The enemy's telegraph for the round in hand (or `ahead` rounds past it): its authored intent. Null for a
    // player-team member, an actionless enemy, or a finished fight.
    public ActionIntent? UpcomingIntentFor(CombatantId combatant, int ahead = 0)
    {
        if (IsOver || !_combat.TryGetCombatant(combatant, out var c) || c is null || c.TeamId == PlayerTeam)
            return null;
        return _enemyIntent(_combat, combatant, _combat.CurrentRound + ahead) is { } actionId
            ? _compiled.IntentFor(actionId)
            : null;
    }

    // The same fight, rebuilt from its snapshot — a copy to try things on (the end-of-turn forecast).
    public PartyCombat Fork() =>
        new(_compiled, CombatState.Restore(_combat.CreateSnapshot(), _registry), _enemyIntent, _targeting);

    // WHAT THE ENEMIES WILL DO TO ONE MEMBER if every hero ends their turn now: played out on a fork, blow by
    // blow, exactly as InteractiveCombat.Foresee does for a hero alone. Null when the fight is over or the
    // player phase is not open.
    public Foresight? Foresee(CombatantId member)
    {
        if (IsOver || ActiveMembers().Count == 0)
            return null;
        var fork = Fork();
        var blows = new List<IncomingBlow>();
        fork._watch = (enemy, action, target, health, guard) =>
        {
            if (target == member)
                blows.Add(new IncomingBlow(enemy, action is { } id ? _compiled.IntentFor(id) : null, health + guard, health, guard));
        };
        foreach (var active in fork.ActiveMembers())
            fork.EndTurn(active);
        var after = fork._combat.TryGetCombatant(member, out var hero) && hero is not null ? hero.Health.Current : 0;
        return new Foresight(blows.Sum(b => b.Amount), blows.Sum(b => b.Health), after,
            fork.Result == CombatResult.Defeat || after <= 0, blows);
    }

    // Handed each enemy's blow as it lands on a FORK: who acted, with what, at whom, and the health and guard
    // it took. Never set on a real fight.
    private Action<CombatantId, EnemyActionDefinitionId?, CombatantId, int, int>? _watch;

    private (int Health, int Guard) Standing(CombatantId id) =>
        _combat.TryGetCombatant(id, out var c) && c is not null
            ? (c.Health.Current,
               c.DefensivePools.TryGetValue(StandardCombatIds.BlockDefensivePool, out var pool) ? pool.Current : 0)
            : (0, 0);

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
            if (actionId is { } id && _registry.TryGetEnemyAction(id, out _) && IntendedTarget(enemy.Id) is { } targetId)
            {
                var before = Standing(targetId);
                _combat.EnqueueEffect(new ExecuteEnemyActionEffectRequest(enemy.Id, id, targetId));
                _queues.ResolvePendingQueues(_combat, _registry);
                if (_watch is { } watch)
                {
                    var after = Standing(targetId);
                    watch(enemy.Id, id, targetId, Math.Max(0, before.Health - after.Health), Math.Max(0, before.Guard - after.Guard));
                }
            }

            if (!IsOver && !_combat.HasMemberEnded(enemy.Id))
                _phases.EndMemberTurn(_combat, _registry, enemy.Id);
        }

        // Enemy phase complete → wrap to a fresh player phase (new round; players draw again).
        if (!IsOver)
            _phases.AdvanceToNextTeamPhase(_combat, _registry);
    }

    // WHOM THIS ENEMY MEANS TO HIT this round, per the configured targeting strategy (party deckbuilding B2d) —
    // asked by the enemy phase as it acts, and by a frontend that shows it on the enemy's intent beforehand
    // (multiplayer M3). Only living players are candidates; ties break by roster order. A pure function of the
    // fight as it stands: the random strategy draws from the combat's seed by round and enemy, never from a
    // counter of its own, so a fight rebuilt from its snapshot aims exactly where the live one did.
    public CombatantId? IntendedTarget(CombatantId enemyId)
    {
        var players = _combat.GetLivingCombatantsOnTeam(PlayerTeam).ToArray();
        if (players.Length == 0)
            return null;

        return _targeting switch
        {
            PartyEnemyTargeting.LowestHealth => Extreme(players, lowest: true).Id,
            PartyEnemyTargeting.HighestHealth => Extreme(players, lowest: false).Id,
            PartyEnemyTargeting.Random => players[
                CombatRandom.CreateShuffledIndexes(players.Length, _combat.RandomSeed, TargetDraw(enemyId))[0]].Id,
            PartyEnemyTargeting.Nearest => Nearest(players, enemyId).Id,
            _ => players[0].Id, // FirstAlive
        };
    }

    // One draw per round and enemy, far from the steps the fight's own dice use.
    private int TargetDraw(CombatantId enemyId) =>
        1_000_000 + _combat.CurrentRound * 64 + _combat.TurnOrder.ToList().IndexOf(enemyId);

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
