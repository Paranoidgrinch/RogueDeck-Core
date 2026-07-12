using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Reporting;

namespace RogueDeck.Scenario.Scripting;

// A stepwise, interactive playthrough driver. It builds the combat, starts the hero's turn, then lets a
// caller play cards one at a time and end the turn (on which each enemy auto-acts its per-round intent).
// It drives REAL turns through CombatTurnProcessor and records a ScenarioStepReport per action, so the
// same NarrativeLogRenderer renders a live log. Like the rest of the harness it only sequences engine
// calls — no new combat semantics. The caller chooses which enemy action fires each round through the
// supplied enemyIntent selector (combatant id + 1-based round → action id, or null to pass).
public sealed class InteractiveCombat
{
    private readonly CombatState _combat;
    private readonly CombatDefinitionRegistry _registry;
    private readonly CompiledScenario _compiled;
    private readonly CombatantId _heroId;
    private readonly Func<CombatState, CombatantId, int, EnemyActionDefinitionId?> _enemyIntent;
    private readonly CollectingTraceListener _collector = new();
    private readonly CombatTurnProcessor _turns = new();
    private readonly CombatQueueProcessor _queues = new();
    private readonly List<ScenarioStepReport> _steps = new();

    public InteractiveCombat(
        CompiledScenario compiled,
        Func<CombatState, CombatantId, int, EnemyActionDefinitionId?> enemyIntent,
        string combatId = "sandbox",
        int randomSeed = 1)
    {
        ArgumentNullException.ThrowIfNull(compiled);
        ArgumentNullException.ThrowIfNull(enemyIntent);

        _compiled = compiled;
        _registry = compiled.Registry;
        _enemyIntent = enemyIntent;
        _heroId = compiled.Hero.CombatantId;

        _combat = ScenarioCombatFactory.Build(compiled, combatId, randomSeed);
        _combat.TraceListener = _collector;

        // Start the hero's first turn (draws the opening hand).
        if (_combat.TurnPhase == CombatTurnPhase.WaitingToStartTurn)
            _turns.StartCurrentTurn(_combat, _registry);
    }

    // ── State views for the UI ───────────────────────────────────────────────────

    public CombatState State => _combat;
    public CombatantId HeroId => _heroId;
    public int Round => _combat.CurrentRound;
    public int Turn => _combat.CurrentTurn;
    public CombatResult Result => _combat.Result;
    public bool IsOver => _combat.Result != CombatResult.Ongoing;
    public bool IsHeroTurn => !IsOver && _combat.ActiveCombatantId == _heroId;
    public IReadOnlyList<ScenarioStepReport> Steps => _steps;

    public IReadOnlyList<CardInstance> Hand =>
        _combat.GetCardZones(_heroId).GetCardsInZone(CardZone.Hand);

    public int HeroEnergy => ResourceCurrent(StandardCombatIds.EnergyResource);
    public int HeroEnergyMax => ResourceMax(StandardCombatIds.EnergyResource);

    public ScenarioReport ToReport() => new(_steps.ToList(), _combat.Result, _combat);

    public string RenderLog() => new NarrativeLogRenderer().Render(ToReport());

    // ── Actions ──────────────────────────────────────────────────────────────────

    // Play one card from the hero's hand at an optional target. No-op unless it is the hero's turn.
    public void PlayCard(CardInstanceId cardInstanceId, CombatantId? target)
    {
        if (!IsHeroTurn)
            return;

        var before = _collector.Events.Count;
        var round = _combat.CurrentRound;
        var turn = _combat.CurrentTurn;
        var problems = new List<string>();

        var zones = _combat.GetCardZones(_heroId);
        if (!zones.ContainsCard(cardInstanceId))
        {
            problems.Add("That card is not in the hero's hand.");
            Record(new HeroPlaysCard("?", target?.value), round, turn, _heroId, problems, before);
            return;
        }

        var cardId = zones.GetCard(cardInstanceId).DefinitionId.value;

        var slot = new PlayCardOutcomeSlot();
        try
        {
            _combat.EnqueueEffect(new PlayCardEffectRequest(_heroId, cardInstanceId, target, slot));
            _queues.ResolvePendingQueues(_combat, _registry);

            if (slot.Value is { WasPlayed: false })
                problems.Add($"Card '{cardId}' was not played (unaffordable or rejected by a validator).");
        }
        catch (Exception ex)
        {
            // An effect threw mid-resolution (e.g. installing a temporary rule that is already installed).
            // Surface it as a step problem rather than tearing down the interactive session.
            problems.Add($"Step threw resolving '{cardId}': {ex.GetType().Name}: {ex.Message}");
        }

        Record(new HeroPlaysCard(cardId, target?.value), round, turn, _heroId, problems, before);
    }

    // Run a consumable's combat-use program on the hero immediately (its "on use in combat" effects — gain block,
    // heal now, hit the enemy, …). Authored like a turnStarted rule (source = the hero), but executed on demand
    // rather than installed: a turnStarted context is fabricated for the hero (never dispatched, so no side effects)
    // and the program runs through the real Effect Program runtime against the live combat. No-op off the hero's turn.
    public bool UseHeroCombatProgram(EffectProgram<TurnStartedTriggeredEffectContext> program)
    {
        ArgumentNullException.ThrowIfNull(program);
        if (!IsHeroTurn)
            return false;

        var before = _collector.Events.Count;
        var round = _combat.CurrentRound;
        var turn = _combat.CurrentTurn;
        var problems = new List<string>();
        try
        {
            var hero = _combat.GetCombatant(_heroId);
            var context = new TurnStartedTriggeredEffectContext(
                _combat, _registry, new TurnStartedCombatEvent(_heroId, round, turn), hero);
            var execution = new EffectExecutionContext<TurnStartedTriggeredEffectContext>(
                context, TurnStartedTriggeredEffectTargetResolver.CreateActionBuildContext(context));
            EffectProgramExecutor.Execute(program, execution, _combat);
            _queues.ResolvePendingQueues(_combat, _registry);
        }
        catch (Exception ex)
        {
            problems.Add($"Consumable use threw: {ex.GetType().Name}: {ex.Message}");
        }

        Record(new HeroUsesConsumable(), round, turn, _heroId, problems, before);
        return true;
    }

    // End the hero's turn; every enemy then acts its intent for the current round, in turn order, until
    // the turn wraps back to the hero (whose next turn starts automatically). No-op unless it is the
    // hero's turn.
    public void EndTurn()
    {
        if (!IsHeroTurn)
            return;

        var heroBefore = _collector.Events.Count;
        var heroRound = _combat.CurrentRound;
        var heroTurn = _combat.CurrentTurn;
        _turns.EndCurrentTurnAndStartNextTurn(_combat, _registry);
        Record(new HeroEndsTurn(), heroRound, heroTurn, _heroId, new List<string>(), heroBefore);

        var guard = 0;
        var maxSteps = _combat.TurnOrder.Count + 2;
        while (_combat.Result == CombatResult.Ongoing && _combat.ActiveCombatantId != _heroId)
        {
            var enemyId = _combat.ActiveCombatantId!.Value;
            var actionId = _enemyIntent(_combat, enemyId, _combat.CurrentRound);

            if (actionId is { } id && _registry.TryGetEnemyAction(id, out _))
            {
                var before = _collector.Events.Count;
                var round = _combat.CurrentRound;
                var turn = _combat.CurrentTurn;
                _combat.EnqueueEffect(new ExecuteEnemyActionEffectRequest(enemyId, id, _heroId));
                _queues.ResolvePendingQueues(_combat, _registry);
                Record(new EnemyActs(enemyId.value, id.value, _heroId.value), round, turn, enemyId, new List<string>(), before);
            }

            if (_combat.Result != CombatResult.Ongoing)
                break;

            // Advance off this enemy's turn (and start the next combatant's, wrapping the round).
            if (_combat.ActiveCombatantId == enemyId)
                _turns.EndCurrentTurnAndStartNextTurn(_combat, _registry);

            if (++guard > maxSteps)
                break;
        }
    }

    private void Record(ScenarioStep step, int round, int turn, CombatantId? actor, List<string> problems, int before)
    {
        var trace = _collector.Events.Skip(before).ToList();
        var intent = step is EnemyActs e
            ? _compiled.IntentFor(new EnemyActionDefinitionId(e.ActionId))
            : null;
        _steps.Add(new ScenarioStepReport(_steps.Count, step, round, turn, actor, intent, trace, problems));
    }

    private int ResourceCurrent(ResourceId id) =>
        _combat.GetCombatant(_heroId).Resources.TryGetValue(id, out var pool) ? pool.Current : 0;

    private int ResourceMax(ResourceId id) =>
        _combat.GetCombatant(_heroId).Resources.TryGetValue(id, out var pool) ? (pool.Max ?? pool.Current) : 0;
}
