using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Bot;

// ── THE SEAT THE ENGINE ACTUALLY ASKS ────────────────────────────────────────────────────────────────────
// The replay model exists so that a single-threaded UI can PARK at a prompt: RunRunner is a synchronous loop
// that asks for each decision, and a UI cannot answer from inside that loop, so the run is unwound and
// re-executed from its baseline once the answer arrives. A bot never parks. It answers. So it can simply BE
// the collaborator — the choice provider, the entity chooser, the interlude, the combat driver and both
// in-combat choosers at once — and the run is walked exactly once, in one pass, with nothing re-executed.
//
// ⚠⚠ THIS SEAT DECIDES NOTHING. Every answer comes from the same BotMind the replay seat asks, in the same
// order, out of the same Random. What lives here is only the shape of the conversation: which engine method
// is the question, and where the answer goes back. If the two seats ever disagree about a run, the
// disagreement is a finding about REPLAY — not about the bot — and `golden.sh --console` played both ways
// (once plain, once `--replay`) is how it is found. It was asked for the first time on 2026-09-18 and
// answered with three faults in the mid-fight capture; see the plan's R5 section.
//
// ⚠ A GUARD HAS TO UNWIND. The replay seat's ceilings (a turn that will not end, a fight that will not
// finish, the answer budget) were `break`s out of its own loop; here they are inside the engine's, so they
// throw BotStopException and whoever started the walk catches it.
internal sealed class BotSeat(RunPlayback play, BotOptions options, IBotLog log)
    : IRunChoiceProvider, IRunEntityChooser, IRunInterlude, ICombatDriver,
      ICombatCardChooser, ICombatOptionChooser
{
    private readonly BotMind _mind = new(play, options, log);
    private readonly RunPlayback _play = play ?? throw new ArgumentNullException(nameof(play));
    private RunState? _run;
    private InteractiveCombat? _current;

    // Handed the run before RunRunner is let loose on it, so the log opens with who is walking and with what.
    public void BeforeTheFirstQuestion(RunState run)
    {
        _run = run;
        _mind.Opening(run);
    }

    public BotResult Finish(string? error, bool complete) => _mind.Finish(_run, error, complete);

    // Everything that happens before an answer, in the order the replay seat's loop did it: the budget
    // (checked before an answer, never after one), the engine's narration, then what has changed since.
    private void Begin()
    {
        var run = _run ?? throw new InvalidOperationException("The seat was asked before the run was handed over.");
        _mind.CheckBudget(run);
        _mind.Narrate(run);
        _mind.Observe(run, _current);
    }

    // …and everything after it: the answer is spent, and a guard that tripped while giving it ends the walk.
    private void Answered()
    {
        _mind.Step++;
        if (_mind.Stopped)
            throw new BotStopException(_mind.Reason);
    }

    // ── The run's questions ──────────────────────────────────────────────────────────────────────────────

    public EventChoice Choose(EventSituation situation, IReadOnlyList<EventChoice> available, RunState run)
    {
        ArgumentNullException.ThrowIfNull(available);
        _run = run;
        Begin();
        var choice = _mind.Choose(situation, available);
        Answered();
        return choice;
    }

    public NodeId ChooseNextNode(IReadOnlyList<Node> candidates, RunState run)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        _run = run;
        Begin();
        var pick = _mind.Fork(candidates);
        Answered();
        return pick.Id;
    }

    // The between-nodes pause. It is an ANSWER, not a formality: the replay seat spends a step on it, so this
    // one does too, or the two walks would count their steps differently and the logs would stop lining up.
    public void BetweenNodes(RunState run)
    {
        _run = run;
        Begin();
        Answered();
    }

    public IReadOnlyList<T> ChooseEntities<T>(IReadOnlyList<T> candidates, int count, string purpose) =>
        ChooseEntities(candidates, count, purpose, allowSkip: false);

    public IReadOnlyList<T> ChooseEntities<T>(IReadOnlyList<T> candidates, int count, string purpose, bool allowSkip)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        // The same two doors the session's chooser closes before it ever publishes a request.
        if (candidates.Count == 0 || count <= 0)
            return [];
        Begin();
        var displays = candidates.Select(c => RunEntityLabeler.Display(c, _play.Labeler)).ToArray();
        var take = _mind.EntityPicks(displays, Math.Min(count, candidates.Count), allowSkip, purpose);
        Answered();
        return [.. take.Where(i => i >= 0 && i < candidates.Count).Select(i => candidates[i])];
    }

    // ── The fight ────────────────────────────────────────────────────────────────────────────────────────

    public CombatDriveResult Drive(Playthrough playthrough) => Drive(playthrough, null);

    // ⚠ `resume` IS IGNORED, and that is the whole point of the trade. A captured fight is put back on the
    // table only by a run that was interrupted — a save, or the replay baseline moving — and neither happens
    // to a walk that never stops. The resume path keeps its coverage through the `--sim-resume` probe.
    public CombatDriveResult Drive(Playthrough playthrough, CombatSaveData? resume)
    {
        ArgumentNullException.ThrowIfNull(playthrough);
        var compiled = playthrough.Blueprint.Compile();
        // The opening hand is dealt only once the choosers are on: a rule that asks something as the hand
        // arrives would otherwise be answered by the headless fallback — the first option, silently.
        var combat = new InteractiveCombat(
            compiled, EnemyIntentSelectors.Build(compiled), playthrough.CombatId, playthrough.RandomSeed,
            startOpeningTurn: false);
        combat.State.SetCardChooser(this);
        combat.State.SetOptionChooser(this);
        _current = combat;
        combat.StartOpeningTurn();

        while (true)
        {
            if (combat.IsOver)
            {
                _current = null;
                var heroHp = combat.State.TryGetCombatant(combat.HeroId, out var hero) && hero is not null
                    ? hero.Health.Current
                    : 0;
                return new CombatDriveResult(combat.Result, heroHp, Units: null,
                    HeroCounters: HeroCounterResults.Read(combat.State, combat.HeroId));
            }

            Begin();
            _mind.FightStarts(_run!, combat);

            if (!combat.IsHeroTurn)
            {
                // The fight handed the turn over and never took it back. Nothing to answer, so nothing to do
                // but say so — the same wall the replay seat runs into when a park lands on the enemy's turn.
                _mind.EnemyTurnWall(_run!);
                Answered();
                continue;
            }

            if (_mind.ChoosePlay(combat) is { } chosen)
            {
                combat.PlayCard(chosen.Card.Id, chosen.Target);
                _mind.AfterPlay(_run!, combat, chosen);
            }
            else
            {
                _mind.EndingTurn(combat);
                combat.EndTurn();
                _mind.AfterEndTurn(_run!);
            }
            Answered();
        }
    }

    // ── The fight's own questions ────────────────────────────────────────────────────────────────────────
    // These fire from INSIDE a card's resolution, which is the one thing the replay model cannot do: there
    // it is a park, a throw, a re-execution and an answer on the next poll. Here it is a method call. The
    // fight is announced first all the same, because the opening hand may ask before the bot has seen it.

    public IReadOnlyList<CardInstanceId> ChooseCards(IReadOnlyList<CardInstance> candidates, int count, string purpose)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0 || count <= 0)
            return [];
        Begin();
        if (_current is { } combat)
            _mind.FightStarts(_run!, combat);
        var picks = _mind.CardChoicePicks(candidates, Math.Min(count, candidates.Count));
        Answered();
        return [.. picks.Select(i => candidates[i].Id)];
    }

    public IReadOnlyList<int> ChooseOptions(IReadOnlyList<string> offered, int count, string purpose)
    {
        ArgumentNullException.ThrowIfNull(offered);
        if (offered.Count == 0 || count <= 0)
            return [];
        Begin();
        if (_current is { } combat)
            _mind.FightStarts(_run!, combat);
        var picks = _mind.OptionPicks(offered, Math.Min(count, offered.Count));
        Answered();
        return picks;
    }
}
