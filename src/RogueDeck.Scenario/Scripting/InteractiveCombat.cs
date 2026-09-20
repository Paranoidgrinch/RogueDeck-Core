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

    // How far into the trace the steps have read. What follows it belongs to nobody yet — see the hand-back
    // step at the end of EndTurn.
    private int _traced;

    public InteractiveCombat(
        CompiledScenario compiled,
        Func<CombatState, CombatantId, int, EnemyActionDefinitionId?> enemyIntent,
        string combatId = "sandbox",
        int randomSeed = 1,
        bool startOpeningTurn = true)
    {
        ArgumentNullException.ThrowIfNull(compiled);
        ArgumentNullException.ThrowIfNull(enemyIntent);

        _compiled = compiled;
        _registry = compiled.Registry;
        _enemyIntent = enemyIntent;
        _heroId = compiled.Hero.CombatantId;

        _combat = ScenarioCombatFactory.Build(compiled, combatId, randomSeed);
        _combat.TraceListener = _collector;
        InstallTelegraph();

        // Start the hero's first turn (draws the opening hand) — unless the caller means to do it itself.
        if (startOpeningTurn)
            StartOpeningTurn();
    }

    // Resume an in-progress fight from a RESTORED CombatState (mid-combat save/resume). The combat state is
    // rebuilt by the caller via CombatState.Restore(snapshot, registry) using the SAME content as `compiled`.
    // We resume exactly at the saved phase: only a between-turns save (WaitingToStartTurn) draws a fresh hand,
    // so a save taken mid-turn resumes with its captured hand intact (no double draw).
    public InteractiveCombat(
        CompiledScenario compiled,
        CombatState restoredState,
        Func<CombatState, CombatantId, int, EnemyActionDefinitionId?> enemyIntent,
        bool startOpeningTurn = true)
    {
        ArgumentNullException.ThrowIfNull(compiled);
        ArgumentNullException.ThrowIfNull(restoredState);
        ArgumentNullException.ThrowIfNull(enemyIntent);

        _compiled = compiled;
        _registry = compiled.Registry;
        _enemyIntent = enemyIntent;
        _heroId = compiled.Hero.CombatantId;

        _combat = restoredState;
        _combat.TraceListener = _collector;
        InstallTelegraph();

        if (startOpeningTurn)
            StartOpeningTurn();
    }

    // Deal the opening hand.
    //
    // Split out of the constructors because the opening hand is a moment rules SPEAK AT — a relic that draws
    // one more, a boss that takes a card into custody, a status that asks the player a question — and a rule
    // that raises a PROMPT there can only be answered if whoever is going to answer it is already installed.
    // A driver that owns a card or option chooser therefore builds the fight, puts its choosers on, publishes
    // the fight so the parked state has something to render, and only then opens the turn. Everything else
    // keeps the old shape: the constructor opens it, and a prompt falls through to the headless default.
    public void StartOpeningTurn()
    {
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

    // The enemy's telegraph: the action it would take when the CURRENT round reaches it (enemies act
    // after the hero within the same round), as its authored intent (label + kind). Null for the hero,
    // an unknown/actionless combatant, or a finished fight — a frontend renders it as the pre-turn
    // intent icon next to each enemy.
    // Let combat programs read the telegraph too: a card that says "if the target intends to Attack" needs
    // the same projection this class renders for the UI. The rules that decide an enemy's next action are
    // content and live here, so this is where the engine is handed a way to ask.
    private void InstallTelegraph() =>
        _combat.SetUpcomingIntentKind((state, combatant) =>
            combatant == _heroId
                ? null
                : _enemyIntent(state, combatant, state.CurrentRound) is { } actionId
                    ? _compiled.IntentFor(actionId)?.Kind.ToString()
                    : null);

    public ActionIntent? UpcomingIntentFor(CombatantId combatant)
    {
        if (IsOver || combatant == _heroId)
            return null;
        return _enemyIntent(_combat, combatant, _combat.CurrentRound) is { } actionId
            ? _compiled.IntentFor(actionId)
            : null;
    }

    // What the hero is currently allowed to see beyond the ordinary view (B&B's Article of Full Disclosure and
    // anything like it): granted by statuses in force on them.
    public DisclosureSpec HeroDisclosure => DisclosureSpec.For(_combat, _registry, _heroId);

    // The top of the hero's own draw pile, as far as their sight reaches. Empty without a disclosure.
    public IReadOnlyList<CardInstance> RevealedDrawPile
    {
        get
        {
            var reach = HeroDisclosure.DrawPileCards;
            if (reach <= 0)
                return Array.Empty<CardInstance>();

            var pile = _combat.GetCardZones(_heroId).DrawPile;
            // The draw takes from the END of the pile, so "the top" is the tail, nearest first.
            return pile.Reverse().Take(reach).ToList();
        }
    }

    // The enemy's telegraph, plus as many actions past it as the hero's sight reaches. The first entry is the
    // ordinary UpcomingIntentFor; the rest are the actions the enemy WOULD take on the rounds after it, read
    // off the state as it stands — a projection, exactly like the visible telegraph itself.
    public IReadOnlyList<ActionIntent> UpcomingIntentsFor(CombatantId combatant)
    {
        if (IsOver || combatant == _heroId)
            return Array.Empty<ActionIntent>();

        var depth = 1 + Math.Max(0, HeroDisclosure.IntentLookahead);
        var intents = new List<ActionIntent>(depth);
        for (var ahead = 0; ahead < depth; ahead++)
            if (_enemyIntent(_combat, combatant, _combat.CurrentRound + ahead) is { } actionId &&
                _compiled.IntentFor(actionId) is { } intent)
                intents.Add(intent);

        return intents;
    }

    // Whether a card in hand may be played AT ALL right now, cost aside — the question a frontend has to be
    // able to ask before it draws the card face. Affordability is a number the UI can work out for itself;
    // a RULE that forbids the play (a decree capping the turn's cards, a rule about what may follow what,
    // a stun, a curse) is not, and a card that is refused only when it is clicked is a rule the player was
    // never shown.
    public bool CanPlay(CardInstanceId cardInstanceId)
    {
        var zones = _combat.GetCardZones(_heroId);
        if (!zones.ContainsCard(cardInstanceId))
            return false;
        var card = zones.GetCard(cardInstanceId);
        if (card.Zone != CardZone.Hand)
            return false;
        if (!_registry.CardDefinitions.TryGetValue(card.DefinitionId, out var definition))
            return false;
        return CombatCardPlayProcessor.IsCardPlayAllowed(
            _combat, _registry, definition, _combat.GetCombatant(_heroId),
            requestedTargetId: null, cardInstanceId: cardInstanceId);
    }

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
    public void EndTurn() => EndTurn(watch: null);

    // `watch` is handed each enemy's blow as it lands — who acted, with what, and what it cost the hero in
    // health and in guard. Only a FORK ever passes one (see Foresee); a real fight passes null and this is
    // the method it always was.
    private void EndTurn(Action<CombatantId, EnemyActionDefinitionId?, int, int>? watch)
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
                var healthBefore = HeroHealth;
                var guardBefore = HeroGuard;
                _combat.EnqueueEffect(new ExecuteEnemyActionEffectRequest(enemyId, id, _heroId));
                _queues.ResolvePendingQueues(_combat, _registry);
                Record(new EnemyActs(enemyId.value, id.value, _heroId.value), round, turn, enemyId, new List<string>(), before);
                watch?.Invoke(enemyId, id, Math.Max(0, healthBefore - HeroHealth),
                    Math.Max(0, guardBefore - HeroGuard));
            }
            else
                watch?.Invoke(enemyId, actionId, 0, 0);

            if (_combat.Result != CombatResult.Ongoing)
                break;

            // Advance off this enemy's turn (and start the next combatant's, wrapping the round).
            if (_combat.ActiveCombatantId == enemyId)
                _turns.EndCurrentTurnAndStartNextTurn(_combat, _registry);

            if (++guard > maxSteps)
                break;
        }

        // ── THE HAND-BACK IS A STEP TOO (P2) ─────────────────────────────────────────────────────────────
        // ⚠⚠ EVERYTHING THE HERO'S OWN TURN START DID USED TO BELONG TO NO STEP AT ALL. The loop above ends
        // with the advance that makes the hero active again, and that advance is where a turn-start
        // automation fires: a poison ticking, a regeneration healing, a start-of-turn rule speaking. Those
        // trace events were emitted after the last Record and before the next one's mark, so nothing carried
        // them — not the narrative log, not the damage receipt. A curse could eat a run and leave no line.
        //
        // The step type for exactly this moment already existed ("advance real turns until it is the hero's
        // turn again"); it simply was never recorded here. It is recorded only when the hand-back actually
        // did something, so a quiet turn does not grow a line that says nothing.
        if (_collector.Events.Count > _traced)
            Record(new AdvanceToNextRound(), _combat.CurrentRound, _combat.CurrentTurn, _heroId,
                new List<string>(), _traced);
    }

    // ── WHAT IS ABOUT TO HAPPEN (B4) ─────────────────────────────────────────────────────────────────────
    // An intent used to be a WORD. `ActionIntent` carries a label and a kind, and the number it is about to
    // apply lived only inside the action's program — behind an amount expression, a strength stack, a
    // vulnerability, a guard and every passive modifier in the fight. So the screen showed "⚔ Shuffle
    // Forward" where every game in this genre shows a number, and the bot could not block the right amount,
    // which is the single most important skill there is.
    //
    // ⚠⚠ THE NUMBER IS NOT ANNOTATED, IT IS PLAYED OUT. Annotating it would mean authoring the same number
    // twice — once in the program that applies it and once in the label that promises it — and the two
    // would part company on the first relic that changes damage. Instead the fight is FORKED and the enemies
    // simply take their turn on the copy. What comes back is not an estimate: it is what will happen, with
    // every modifier already in it, because it is the same code that will happen.
    //
    // ⚠ A FORK HAS NOBODY SITTING AT IT. Its card and option choosers are not carried over, so an enemy
    // action that ASKS something is answered by the headless default (the first option) on the fork while
    // the real fight will ask the player. A projection of such an action can therefore differ from the
    // event; nothing else can.
    public InteractiveCombat Fork() =>
        new(_compiled, CombatState.Restore(_combat.CreateSnapshot(), _registry), _enemyIntent,
            startOpeningTurn: false);

    public int HeroHealth => _combat.TryGetCombatant(_heroId, out var hero) && hero is not null
        ? hero.Health.Current
        : 0;

    public int HeroGuard =>
        _combat.TryGetCombatant(_heroId, out var hero) && hero is not null
        && hero.DefensivePools.TryGetValue(StandardCombatIds.BlockDefensivePool, out var pool)
            ? pool.Current
            : 0;

    // What ending the turn right now would cost: blow by blow, and in total. Null when there is nothing to
    // foresee (the fight is over, or it is not the hero's turn to end).
    public Foresight? Foresee()
    {
        if (IsOver || !IsHeroTurn)
            return null;

        var fork = Fork();
        var blows = new List<IncomingBlow>();
        fork.EndTurn(watch: (enemy, action, health, guard) => blows.Add(new IncomingBlow(
            enemy,
            action is { } id ? _compiled.IntentFor(id) : null,
            health + guard,
            health,
            guard)));

        return new Foresight(
            blows.Sum(b => b.Amount),
            blows.Sum(b => b.Health),
            fork.HeroHealth,
            fork.Result == CombatResult.Defeat || fork.HeroHealth <= 0,
            blows);
    }

    private void Record(ScenarioStep step, int round, int turn, CombatantId? actor, List<string> problems, int before)
    {
        var trace = _collector.Events.Skip(before).ToList();
        var intent = step is EnemyActs e
            ? _compiled.IntentFor(new EnemyActionDefinitionId(e.ActionId))
            : null;
        _steps.Add(new ScenarioStepReport(_steps.Count, step, round, turn, actor, intent, trace, problems));
        _traced = _collector.Events.Count;
    }

    private int ResourceCurrent(ResourceId id) =>
        _combat.GetCombatant(_heroId).Resources.TryGetValue(id, out var pool) ? pool.Current : 0;

    private int ResourceMax(ResourceId id) =>
        _combat.GetCombatant(_heroId).Resources.TryGetValue(id, out var pool) ? (pool.Max ?? pool.Current) : 0;
}

// One enemy's blow, as it will land: who throws it, what it is telegraphed as, and what it costs. `Amount`
// is everything the blow takes off — the health it removes plus the guard it eats — because a player
// deciding how much to block wants the size of the swing, not what is left of it after this turn's guard.
public readonly record struct IncomingBlow(
    CombatantId Enemy,
    ActionIntent? Intent,
    int Amount,
    int Health,
    int Guard);

// What ending the turn now would cost, in the fight's own numbers rather than in words.
public sealed record Foresight(
    int Amount,
    int Health,
    int HeroHealthAfter,
    bool HeroDies,
    IReadOnlyList<IncomingBlow> Blows);
