using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Reporting;

namespace RogueDeck.Scenario.Scripting;

// Runs a Playthrough against the real combat engine: it compiles the blueprints, builds a CombatState,
// drives REAL turns through CombatTurnProcessor (so Round/Turn actually advance and turn automation fires),
// and slices the trace stream per step. It adds NO combat semantics — it only sequences engine calls and
// observes the result. Every problem it surfaces (faulted plays, no-op plays, premature combat end,
// step exceptions) is a real observation about how the engine behaved, not an authored assertion.
public sealed class ScenarioRunner
{
    private readonly CombatTurnProcessor _turns = new();
    private readonly CombatQueueProcessor _queues = new();

    public ScenarioReport Run(Playthrough playthrough)
    {
        ArgumentNullException.ThrowIfNull(playthrough);

        var compiled = playthrough.Blueprint.Compile();
        var registry = compiled.Registry;
        var combat = ScenarioCombatFactory.Build(compiled, playthrough.CombatId, playthrough.RandomSeed);

        // Attach the collector only after setup, so blueprint seeding does not pollute step slices.
        var collector = new CollectingTraceListener();
        combat.TraceListener = collector;

        var heroId = compiled.Hero.CombatantId;
        var reports = new List<ScenarioStepReport>();

        for (var i = 0; i < playthrough.Steps.Count; i++)
        {
            var step = playthrough.Steps[i];
            var before = collector.Events.Count;
            var problems = new List<string>();
            var intent = IntentFor(step, compiled);

            StepContext context;
            if (combat.Result != CombatResult.Ongoing)
            {
                problems.Add($"Combat already ended ('{combat.Result}'); step not executed.");
                context = new StepContext(combat.CurrentRound, combat.CurrentTurn, combat.ActiveCombatantId);
            }
            else if (!combat.TryGetCombatant(heroId, out var hero) || !hero!.IsAlive)
            {
                // The hero can be downed while combat continues (e.g. a summoned ally keeps the team alive).
                // The hero-driven runner can't drive turns past that, so stop cleanly instead of faulting.
                problems.Add("The hero is downed; the scenario cannot drive further turns.");
                context = new StepContext(combat.CurrentRound, combat.CurrentTurn, combat.ActiveCombatantId);
            }
            else
            {
                try
                {
                    context = ExecuteStep(step, combat, registry, heroId, problems);
                }
                catch (Exception ex)
                {
                    problems.Add($"Step threw {ex.GetType().Name}: {ex.Message}");
                    context = new StepContext(combat.CurrentRound, combat.CurrentTurn, combat.ActiveCombatantId);
                }
            }

            var trace = collector.Events.Skip(before).ToList();
            reports.Add(new ScenarioStepReport(
                i, step, context.Round, context.Turn, context.Actor, intent, trace, problems));
        }

        return new ScenarioReport(reports, combat.Result, combat);
    }

    // ── Step dispatch ────────────────────────────────────────────────────────────

    private StepContext ExecuteStep(
        ScenarioStep step,
        CombatState combat,
        CombatDefinitionRegistry registry,
        CombatantId heroId,
        List<string> problems) => step switch
        {
            HeroPlaysCard play => ExecuteHeroPlays(play, combat, registry, heroId, problems),
            HeroEndsTurn => ExecuteHeroEndsTurn(combat, registry, heroId, problems),
            EnemyActs enemy => ExecuteEnemyActs(enemy, combat, registry, heroId, problems),
            AdvanceToNextRound => ExecuteNextRound(combat, registry, heroId, problems),
            _ => Unknown(step, combat, problems),
        };

    private static StepContext Unknown(ScenarioStep step, CombatState combat, List<string> problems)
    {
        problems.Add($"Unknown step type '{step.GetType().Name}'.");
        return new StepContext(combat.CurrentRound, combat.CurrentTurn, combat.ActiveCombatantId);
    }

    private StepContext ExecuteHeroPlays(
        HeroPlaysCard play,
        CombatState combat,
        CombatDefinitionRegistry registry,
        CombatantId heroId,
        List<string> problems)
    {
        if (combat.ActiveCombatantId != heroId)
            problems.Add($"HeroPlays but the active combatant is '{combat.ActiveCombatantId}', not the hero.");

        EnsureTurnStarted(combat, registry); // starts the hero's turn (and draws its hand) on the first play
        var context = new StepContext(combat.CurrentRound, combat.CurrentTurn, heroId);

        var cardDefId = new CardDefinitionId(play.CardId);
        var instance = combat.GetCardZones(heroId)
            .GetCardsInZone(CardZone.Hand)
            .FirstOrDefault(card => card.DefinitionId == cardDefId);

        if (instance is null)
        {
            problems.Add($"Card '{play.CardId}' is not in the hero's hand; cannot play it.");
            return context;
        }

        var target = ParseTarget(play.TargetId);
        var outcome = new PlayCardOutcomeSlot();
        combat.EnqueueEffect(new PlayCardEffectRequest(heroId, instance.Id, target, outcome));
        _queues.ResolvePendingQueues(combat, registry);

        if (outcome.Value is { WasPlayed: false })
            problems.Add($"Card '{play.CardId}' was not played (unaffordable or rejected by a validator).");

        return context;
    }

    private StepContext ExecuteHeroEndsTurn(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CombatantId heroId,
        List<string> problems)
    {
        if (combat.ActiveCombatantId != heroId)
            problems.Add($"HeroEndsTurn but the active combatant is '{combat.ActiveCombatantId}', not the hero.");

        EnsureTurnStarted(combat, registry);
        var context = new StepContext(combat.CurrentRound, combat.CurrentTurn, combat.ActiveCombatantId);
        // Starting the turn can end combat (e.g. a turn-start trigger downs the last enemy); don't then
        // try to advance turns on a finished combat.
        if (combat.Result == CombatResult.Ongoing)
            _turns.EndCurrentTurnAndStartNextTurn(combat, registry);
        return context;
    }

    private StepContext ExecuteEnemyActs(
        EnemyActs step,
        CombatState combat,
        CombatDefinitionRegistry registry,
        CombatantId heroId,
        List<string> problems)
    {
        var enemyId = new CombatantId(step.EnemyId);

        if (!combat.TryGetCombatant(enemyId, out var enemy) || enemy is null)
        {
            problems.Add($"Enemy '{step.EnemyId}' does not exist in this combat.");
            return new StepContext(combat.CurrentRound, combat.CurrentTurn, enemyId);
        }

        AdvanceTurnsUntilActive(combat, registry, enemyId, heroId, problems);
        var context = new StepContext(combat.CurrentRound, combat.CurrentTurn, enemyId);

        if (combat.Result != CombatResult.Ongoing)
        {
            problems.Add($"Combat ended ('{combat.Result}') before enemy '{step.EnemyId}' could act.");
            return context;
        }

        if (combat.ActiveCombatantId != enemyId)
        {
            problems.Add($"Could not reach enemy '{step.EnemyId}'s turn (active: '{combat.ActiveCombatantId}').");
            return context;
        }

        if (!enemy.IsAlive)
        {
            problems.Add($"Enemy '{step.EnemyId}' is not alive; action skipped.");
            return context;
        }

        var actionId = new EnemyActionDefinitionId(step.ActionId);
        if (!registry.TryGetEnemyAction(actionId, out _))
        {
            problems.Add($"Enemy action '{step.ActionId}' is not registered.");
            return context;
        }

        combat.EnqueueEffect(new ExecuteEnemyActionEffectRequest(enemyId, actionId, ParseTarget(step.TargetId)));
        _queues.ResolvePendingQueues(combat, registry);
        return context;
    }

    private StepContext ExecuteNextRound(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CombatantId heroId,
        List<string> problems)
    {
        EnsureTurnStarted(combat, registry);
        var context = new StepContext(combat.CurrentRound, combat.CurrentTurn, combat.ActiveCombatantId);

        // If it is already the hero's turn, end it first so a full round actually passes.
        if (combat.ActiveCombatantId == heroId && combat.Result == CombatResult.Ongoing)
            _turns.EndCurrentTurnAndStartNextTurn(combat, registry);

        var guard = 0;
        var maxSteps = combat.TurnOrder.Count + 2;
        while (combat.Result == CombatResult.Ongoing && combat.ActiveCombatantId != heroId)
        {
            _turns.EndCurrentTurnAndStartNextTurn(combat, registry);
            if (++guard > maxSteps)
            {
                problems.Add("NextRound could not return to the hero's turn (turn order did not wrap).");
                break;
            }
        }

        return context;
    }

    // End real turns one at a time until it is the target combatant's turn. Each skipped turn is faithfully
    // ended and the next started (firing its automation). The hero's turn is never ended this way.
    private void AdvanceTurnsUntilActive(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CombatantId targetId,
        CombatantId heroId,
        List<string> problems)
    {
        EnsureTurnStarted(combat, registry);

        var guard = 0;
        var maxSteps = combat.TurnOrder.Count + 1;
        while (combat.Result == CombatResult.Ongoing && combat.ActiveCombatantId != targetId)
        {
            if (combat.ActiveCombatantId == heroId)
            {
                problems.Add("It is still the hero's turn — call HeroEndsTurn before EnemyActs.");
                return;
            }

            _turns.EndCurrentTurnAndStartNextTurn(combat, registry);
            if (++guard > maxSteps)
            {
                problems.Add($"Could not advance to enemy '{targetId}'s turn.");
                return;
            }
        }
    }

    private void EnsureTurnStarted(CombatState combat, CombatDefinitionRegistry registry)
    {
        if (combat.Result == CombatResult.Ongoing && combat.TurnPhase == CombatTurnPhase.WaitingToStartTurn)
            _turns.StartCurrentTurn(combat, registry);
    }

    private static ActionIntent? IntentFor(ScenarioStep step, CompiledScenario compiled) =>
        step is EnemyActs enemy ? compiled.IntentFor(new EnemyActionDefinitionId(enemy.ActionId)) : null;

    private static CombatantId? ParseTarget(string? targetId) =>
        targetId is null ? null : new CombatantId(targetId);

    private readonly record struct StepContext(int Round, int Turn, CombatantId? Actor);
}
