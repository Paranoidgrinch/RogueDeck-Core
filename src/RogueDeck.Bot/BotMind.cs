using System.Diagnostics;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Bot;

// How a walk ends when the runner itself calls it off — a turn that will not end, a fight that will not
// finish, an answer budget spent. Under the replay seat these were `break`s out of a poll loop; a seat the
// engine calls has no loop to break out of, so it unwinds the runner instead. Caught by whoever started the
// walk, and never by the run.
internal sealed class BotStopException(string reason) : Exception(reason)
{
    public string Reason { get; } = reason;
}

// ── WHAT THE RUNNER THINKS, WITH NO SEAT UNDER IT ────────────────────────────────────────────────────────
// Every decision the bot makes and every number it reports lives here, and NOTHING about how the decision
// reaches the engine does. There are two seats:
//
//   the REPLAY seat (RunBot.Play)     — polls a parked InteractiveRunSession and hands each answer back
//                                       through the same methods the mouse uses;
//   the DIRECT seat (BotSeat)         — IS the collaborator the engine asks, and answers inline.
//
// Both ask this object the same questions in the same order, so the two walks draw the same numbers out of
// the same Random and end in the same room. That equality is the gate on R5, and it is also the strongest
// statement the project has ever made that replay and direct play are the same game.
//
// ⚠⚠ THE ORDER THE RANDOM IS DRAWN IN IS PART OF THE CONTRACT. A card is chosen before its target, and a
// target is drawn ONLY when a card was chosen. Move one draw — even into a branch that looks equivalent —
// and the two seats stop playing the same game from the same seed.
internal sealed class BotMind
{
    private readonly RunPlayback _play;
    private readonly BotOptions _options;
    private readonly BotPolicy? _policy;
    private readonly IBotLog _log;
    private readonly Random _rng;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public BotMind(RunPlayback play, BotOptions options, IBotLog log)
    {
        _play = play ?? throw new ArgumentNullException(nameof(play));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _policy = options.Policy;
        _rng = new Random(options.Seed);
    }

    // ── What the report is made of ───────────────────────────────────────────────────────────────────────
    public int Step;
    public string Reason = "the run finished";
    public string Crash = "";
    public bool Stopped;
    public int Problems;
    public int Fights;
    public int Acts = 1;
    public int DamageTaken;

    // What the body lost after the last answer — the blow nobody was asked about, because the run was over.
    // See the note in Finish.
    public int ClosingDamage;
    public int Healed;
    public double Seconds => _clock.Elapsed.TotalSeconds;
    public readonly List<string> Rooms = [];

    // ── THE RECEIPT (P2) ─────────────────────────────────────────────────────────────────────────────────
    // `DamageTaken` says HOW MUCH the walk cost and has never said where it went. This does, out of the
    // trace the engine writes anyway, and it is held against `DamageTaken` at the end so that what it cannot
    // explain is printed rather than lost. See DamageLedger.
    public readonly DamageLedger Ledger = new();

    // Every room entered as "<act>:<node id>", in order. Rooms says what KIND of room each one was, which is
    // what a report reads; this says WHICH ROOM, which is what the map oracle needs to find the walk again on
    // the map it surveyed. Two lists because the first is a contract golden.sh diffs and the second is not.
    public readonly List<string> Walked = [];

    // ── EVERY ROOM, AS THE BALANCE MAP NEEDS IT (P3) ─────────────────────────────────────────────────────
    // `Rooms` says what KIND each room was and `Walked` says WHICH node — neither says what was authored in
    // it, and a tally kept per node id cannot be added up across seeds because the node is a different room
    // in the next one. This does: the act, the content's own id, its role, and the health the runner walked
    // in with. The last of those is what turns a heap of damage into a curve — where in an act the budget
    // actually breaks.
    public readonly List<BotResult.RoomVisit> Visits = [];
    public readonly Dictionary<int, int> HealthAtActBoss = [];
    public readonly Dictionary<int, int> DamageAtActBoss = [];

    // WHERE THE RUN WAS STANDING, in the words the log uses. A run that dies has one thing worth knowing
    // about it beyond the fact — the room it died in — and by the time anyone asks, the run is over and the
    // map is gone. So it is kept as it goes past.
    public string Where = "—";
    public string WhereRole = "—";

    private int _loggedNarration;
    // THE RUN AS IT LAST STOOD. Both seats hand it over before every answer (Observe), and a decision that
    // needs to know what the player already CARRIES — is this reward better than the deck I have? — reads it
    // from here rather than asking for a parameter no seat could fill at the moment it is asked.
    private RunState? _run;
    private string? _lastRoom;
    private int _hpBeforeRoom;
    private int _hpLastSeen;

    // What the ledger files a blow under, kept as the walk goes past: the room's CONTENT (the encounter or
    // door authored there), not the map coordinate, because a balance sweep adds up across seeds and a
    // coordinate means nothing in the next one. `_ledgerFight` is the fight still owed a last look — the
    // blow that kills the hero lands in a fight no seat hands over again, so Finish reads it once more.
    private string _roomContent = "—";
    private InteractiveCombat? _ledgerFight;
    private int _ledgerAct = 1;
    private string _ledgerRoom = "—";
    private string _ledgerRole = "—";

    // ── The guards ───────────────────────────────────────────────────────────────────────────────────────
    // Never re-offer a play the engine refused; never repeat a play that moved nothing on the table (a card
    // may put a copy of itself back in hand for ever); give both the turn and the fight a ceiling.
    private bool _inFight;
    // THE FIGHT AS IT STOOD AT THE DOOR, kept for the autopsy: one copy per fight, replaced at the next
    // bell, so what is left at the end of a run is the fight the run died in.
    // ⚠ NOT THE DOOR BUT THE LAST FEW TURNS. The whole fight is too big a tree to decide (see FightSolver),
    // and the sharp question is smaller anyway: how far back was the death still avoidable? So the start of
    // each of the hero's turns is kept, and only the last handful of them are asked about.
    private readonly Queue<InteractiveCombat> _turnsBack = new();
    private const int TurnsKept = 5;
    private bool _keptThisTurn;
    private string _doorName = "—";
    private int _enemyHealthAtFightStart;
    private int _turn;
    private int _playsThisTurn;
    private readonly HashSet<CardInstanceId> _refused = [];
    private readonly HashSet<string> _barren = new(StringComparer.Ordinal);
    private string? _lastPlayed;
    private string _tableBeforeThePlay = "";

    private void NewTurn()
    {
        _plan.Clear();
        _keptThisTurn = false;
        _playsThisTurn = 0;
        _lastPlayed = null;
        _refused.Clear();
        _barren.Clear();
    }

    // The header line: who is walking, from what number, with what.
    public void Opening(RunState run)
    {
        ArgumentNullException.ThrowIfNull(run);
        _run = run;
        _hpLastSeen = run.Health.Current;
        _log.Line($"sim: policy={_policy?.Name ?? "random"} seed={_options.Seed} maps={_options.Maps} "
            + $"character={_options.Character ?? "—"} "
            + $"hp={run.Health.Current}/{run.Health.Max} deck={run.Deck.Count} "
            + $"relics={string.Join(",", run.Relics.Select(r => r.Id.Value))}");
    }

    // ── EVERYTHING THAT IS NOTICED RATHER THAN DECIDED ───────────────────────────────────────────────────
    // Run once per answer, before the answer, by both seats: the engine's own narration since last time
    // (Narrate is its own method only because the replay seat reads its session's Error between the two),
    // what the health did, whether the walk has entered a new room, whether a fight has just ended. The
    // order matters — the log is read as a story, and a room line after the fight line inside it is a lie.
    public void Narrate(RunState run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var narration = run.Log;
        // ⚠ A RESTORED RUN'S LOG STARTS AGAIN. The replay seat moves its baseline by rebuilding the run from
        // a snapshot, and the rebuilt run carries a SHORTER log — so a mark kept from before it would sit
        // past the end and silently swallow everything the run said next, until the log grew back past it.
        // What was printed stays printed; the mark simply cannot stand beyond what there is to read.
        if (_loggedNarration > narration.Count)
            _loggedNarration = narration.Count;
        for (; _loggedNarration < narration.Count; _loggedNarration++)
            _log.Line($"    | {narration[_loggedNarration].Message}");
    }

    public void Observe(RunState run, InteractiveCombat? combat)
    {
        ArgumentNullException.ThrowIfNull(run);
        _run = run;

        var hpNow = run.Health.Current;
        if (hpNow < _hpLastSeen)
        {
            DamageTaken += _hpLastSeen - hpNow;
            // ⚠ ONLY WHAT NO FIGHT CAN ACCOUNT FOR. Inside a fight the ledger reads the damage pipeline's
            // own trace, which names who swung; the run's health is the same loss seen from further away,
            // and adding both would count every blow twice. `_inFight` is still true on the first answer
            // after a fight ends, which is what keeps that fight's last sync out of here too.
            if (combat is null && !_inFight)
                Ledger.Outside(_hpLastSeen - hpNow, run.ActNumber, _roomContent, WhereRole);
        }
        else if (hpNow > _hpLastSeen)
            Healed += hpNow - _hpLastSeen;
        _hpLastSeen = hpNow;

        if (combat is not null)
        {
            (_ledgerFight, _ledgerAct, _ledgerRoom, _ledgerRole) =
                (combat, run.ActNumber, _roomContent, WhereRole);
            Ledger.Read(combat, run.ActNumber, _roomContent, WhereRole);
        }

        if (run.CurrentNodeId?.Value is { } here && here != _lastRoom)
        {
            _lastRoom = here;
            var node = run.Map.Nodes.FirstOrDefault(n => n.Id.Value == here);
            var role = node is null ? "?" : MapRole.Of(node);
            _roomContent = RunBot.Content(run);
            Where = RunBot.Where(run);
            WhereRole = role;
            Rooms.Add($"{run.ActNumber}:{role}");
            Walked.Add($"{run.ActNumber}:{here}");
            Visits.Add(new BotResult.RoomVisit(run.ActNumber, _roomContent, role, run.Health.Current));
            Acts = Math.Max(Acts, run.ActNumber);
            var spent = _hpBeforeRoom == 0 ? 0 : _hpBeforeRoom - run.Health.Current;
            _hpBeforeRoom = run.Health.Current;
            if (node is not null && node.HasTag(MapNodeTags.Boss))
            {
                HealthAtActBoss[run.ActNumber] = run.Health.Current;
                DamageAtActBoss[run.ActNumber] = DamageTaken;
            }
            _log.Line($"[{_clock.Elapsed.TotalSeconds,6:0.0}s {Step,5}] ROOM {RunBot.Where(run)} {role} "
                + $"cost={spent} hp={run.Health.Current}/{run.Health.Max} "
                + $"gold={run.GetResource(StandardRunIds.Gold)} "
                + $"deck={run.Deck.Count} relics={run.Relics.Count}");
        }

        if (combat is null && _inFight)
        {
            _inFight = false;
            _log.Line($"  fight ends: hp={run.Health.Current}/{run.Health.Max} after {_turn} turns");
            _turn = 0;
            NewTurn();
        }
    }

    // The answer budget, checked exactly where the replay seat's `for` checked it: before an answer, never
    // after one.
    public void CheckBudget(RunState run)
    {
        if (Step < _options.Budget)
            return;
        Reason = $"the step budget ran out at {RunBot.Where(run)}";
        Stopped = true;
        throw new BotStopException(Reason);
    }

    // The first sight of a fight, named by who is in it.
    public void FightStarts(RunState run, InteractiveCombat combat)
    {
        ArgumentNullException.ThrowIfNull(combat);
        if (_inFight)
            return;
        _inFight = true;
        Fights++;
        if (_options.Autopsy)
        {
            _turnsBack.Clear();
            _keptThisTurn = false;
            _doorName = RunBot.Where(run);
        }
        // The denominator the champion measures its progress against, fixed at the bell so that emptying the
        // enemy is worth the same at the start of the fight as at the end of it.
        _enemyHealthAtFightStart = combat.State.Combatants
            .Where(c => c.Id != combat.HeroId && c.TeamId == StandardCombatIds.EnemyTeam)
            .Sum(c => c.Health.Current);
        var enemies = combat.State.Combatants
            .Where(c => c.Id != combat.HeroId)
            .Select(c => $"{c.Id.value}({c.Health.Current})");
        _log.Line($"  FIGHT {RunBot.Where(run)} vs {string.Join(" ", enemies)}");
    }

    // ── The decisions ────────────────────────────────────────────────────────────────────────────────────

    public IReadOnlyList<int> OptionPicks(IReadOnlyList<string> options, int count)
    {
        ArgumentNullException.ThrowIfNull(options);
        var picks = RunBot.Pick(_rng, options.Count, count);
        _log.Line($"    option {string.Join(",", picks)} of {options.Count}");
        return picks;
    }

    public IReadOnlyList<int> CardChoicePicks(IReadOnlyList<CardInstance> cards, int count)
    {
        ArgumentNullException.ThrowIfNull(cards);
        var picks = RunBot.Pick(_rng, cards.Count, count);
        _log.Line($"    card-choice {string.Join(",", picks.Select(i => cards[i].DefinitionId.value))}");
        return picks;
    }

    // What to do with the turn: a card and who at, or nothing — which means end it.
    public CardPlay? ChoosePlay(InteractiveCombat combat)
    {
        ArgumentNullException.ThrowIfNull(combat);
        KeepThisTurn(combat);
        if (_options.Champion)
            return ChampionPlay(combat);

        // A play only counts as barren once it has fully resolved, which is the answer AFTER it was made.
        if (_lastPlayed is { } finished)
        {
            if (RunBot.TableState(combat) == _tableBeforeThePlay)
                _barren.Add(finished);
            _lastPlayed = null;
        }

        var hero = combat.State.GetCombatant(combat.HeroId);
        var playable = combat.Hand
            .Where(c => !_refused.Contains(c.Id) && !_barren.Contains(c.DefinitionId.value)
                && RunBot.CanPay(_play, hero, c.DefinitionId.value))
            .ToList();
        var living = combat.State.Combatants
            .Where(c => c.Id != combat.HeroId && c.IsAlive && c.TeamId == StandardCombatIds.EnemyTeam)
            .ToList();
        // A random player ends the turn early sometimes — the same hand played to the last point every time
        // never shows what a held card does on the enemy's turn.
        CardInstance? card;
        if (_policy is null)
            card = playable.Count > 0 && _rng.NextDouble() > 0.12
                ? playable[_rng.Next(playable.Count)]
                : null;
        else
        {
            // The best card in hand, and a turn that ends when the best is not worth it.
            var best = playable
                .Select(c => (card: c, score: Score(c.DefinitionId.value)))
                .OrderByDescending(x => x.score)
                .FirstOrDefault();
            card = best.card is not null && best.score >= _policy.EndTurnBelow ? best.card : null;
        }
        if (card is null)
            return null;

        var target = living.Count == 0 ? (CombatantId?)null
            : _policy is null ? living[_rng.Next(living.Count)].Id
            : _rng.NextDouble() < _policy.TargetLowestHp
                ? living.OrderBy(e => e.Health.Current).First().Id
                : living.OrderByDescending(e => e.Health.Current).First().Id;
        _tableBeforeThePlay = RunBot.TableState(combat);
        _lastPlayed = card.DefinitionId.value;
        _log.Line($"    play {card.DefinitionId.value} -> {target?.value ?? "—"} "
            + $"(hp {hero.Health.Current}, hand {combat.Hand.Count})");
        return new CardPlay(card, target, combat.Steps.Count);
    }

    // ── THE CHAMPION ─────────────────────────────────────────────────────────────────────────────────────
    // The player itself lives in Champion.cs, alone, so that it can be measured alone (C0). What is left
    // here is the SEAT's half of it: holding the plan the champion made, handing it out one card at a time
    // the way the seat asks, and noticing when the real fight has gone somewhere the plan did not.
    // The turn as planned, handed out one card at a time because that is how the seat asks — plus the
    // position the fight is expected to be in before each of them. If the real fight ever disagrees with the
    // plan (a card that asked the player something and was answered differently on the fork), the plan is
    // torn up and the turn is planned again from where it actually stands.
    private readonly Queue<Champion.PlannedPlay> _plan = new();
    private Champion? _champion;

    private CardPlay? ChampionPlay(InteractiveCombat combat)
    {
        if (_lastPlayed is { } finished)
        {
            if (RunBot.TableState(combat) == _tableBeforeThePlay)
                _barren.Add(finished);
            _lastPlayed = null;
        }

        var standing = Champion.Position(combat);
        if (_plan.Count > 0 && _plan.Peek().Expected != standing)
        {
            _log.Line("    (the fight went somewhere the plan did not — planning the turn again)");
            _plan.Clear();
        }

        if (_plan.Count == 0)
        {
            _champion ??= new Champion(_play, _policy);
            var planned = _champion.PlanTurn(combat, _refused, _barren);
            _plan.Clear();
            foreach (var step in planned.Plays)
                _plan.Enqueue(step);
            if (Environment.GetEnvironmentVariable("ROGUEDECK_PLANS") is not null)
                _log.Line($"    (turn planned: {_plan.Count} cards, {planned.Forks} forks, "
                    + $"worth {planned.Worth:0.###})");
        }

        if (_plan.Count == 0)
            return null;

        var next = _plan.Dequeue();
        var card = combat.Hand.FirstOrDefault(c => c.Id == next.Card);
        if (card is null)
        {
            // The card the plan meant is not in hand any more. Nothing to salvage: plan again.
            _plan.Clear();
            return ChampionPlay(combat);
        }

        var hero = combat.State.GetCombatant(combat.HeroId);
        _tableBeforeThePlay = RunBot.TableState(combat);
        _lastPlayed = card.DefinitionId.value;
        _log.Line($"    play {card.DefinitionId.value} -> {next.Target?.value ?? "—"} "
            + $"(hp {hero.Health.Current}, hand {combat.Hand.Count})");
        return new CardPlay(card, next.Target, combat.Steps.Count);
    }

    // What the fight recorded about the play that was just made — and whether the turn has now gone on longer
    // than any turn goes on.
    public void AfterPlay(RunState run, InteractiveCombat? combat, CardPlay play)
    {
        ArgumentNullException.ThrowIfNull(play);
        foreach (var bad in (combat?.Steps ?? []).Skip(play.StepsBefore).Where(s => s.HasProblems))
        {
            // Two of these are the engine working, not failing: a card the rules REFUSE (a random player
            // will try a curse) and a card that PARKS to ask its own question (the replay seat reports the
            // park as a throw, and the prompt the bot answers next arrives right behind it — the direct seat
            // never raises one at all, because nothing parks there). Everything else is a finding.
            var text = string.Join(" | ", bad.Problems);
            var expected = text.Contains("was not played", StringComparison.Ordinal)
                || text.Contains("ReplayParked", StringComparison.Ordinal);
            if (expected)
            {
                _log.Line($"    (refused/asked: {play.Card.DefinitionId.value})");
                continue;
            }
            Problems++;
            _log.Line($"    !! PROBLEM playing {play.Card.DefinitionId.value} at {RunBot.Where(run)}: {text}");
        }
        if (RunBot.Refused(combat, play.StepsBefore))
            _refused.Add(play.Card.Id);
        if (++_playsThisTurn >= RunBot.PlaysInATurnNobodyMakes)
        {
            Reason = $"a turn at {RunBot.Where(run)} played {_playsThisTurn} cards without ending — "
                + $"last '{play.Card.DefinitionId.value}'";
            Stopped = true;
        }
    }

    public void EndingTurn(InteractiveCombat combat)
    {
        ArgumentNullException.ThrowIfNull(combat);
        var hero = combat.State.GetCombatant(combat.HeroId);
        _log.Line($"    end turn {_turn + 1} (hp {hero.Health.Current}, hand {combat.Hand.Count})");
    }

    public void AfterEndTurn(RunState run)
    {
        NewTurn();
        if (++_turn >= RunBot.TurnsAFightShouldNotNeed)
        {
            Reason = $"the fight at {RunBot.Where(run)} did not end in {_turn} turns";
            Stopped = true;
        }
    }

    public void EnemyTurnWall(RunState run)
    {
        Reason = $"the fight at {RunBot.Where(run)} parked on the enemy's turn";
        Stopped = true;
    }

    public Node Fork(IReadOnlyList<Node> forks)
    {
        ArgumentNullException.ThrowIfNull(forks);

        // TOLD, NOT CHOSEN. A run walking a named route takes the door the route names and asks nothing. The
        // route may run out (it names one act, the run walks five) — past its end the runner decides again,
        // which is what lets one act be interrogated while the rest of the run stays itself.
        if (_options.Route is { Count: > 0 } route
            && forks.FirstOrDefault(n => route.Contains(n.Id.Value, StringComparer.Ordinal)
                                         && !_walkedRoute.Contains(n.Id.Value)) is { } told)
        {
            _walkedRoute.Add(told.Id.Value);
            _log.Line($"  fork -> {told.Id.Value} {MapRole.Of(told)} (told)");
            return told;
        }

        var seeing = Foresight();
        var pick = _policy is null
            ? forks[_rng.Next(forks.Count)]
            : seeing is null
                ? forks.OrderByDescending(n => PathWeight(_policy, n)).First()
                : forks.OrderByDescending(seeing.Of).First();
        _log.Line($"  fork -> {pick.Id.Value} {MapRole.Of(pick)} "
            + $"(of {string.Join(" ", forks.Select(MapRole.Of))})"
            + (seeing is null ? "" : $" route={seeing.Of(pick):0.##}"));
        return pick;
    }

    // ── THE ROUTE READER, BUILT ONCE PER ACT ─────────────────────────────────────────────────────────────
    // Null unless a policy asked to see past the next room. Kept against the map it was built for, because
    // an act boundary hands the run a different graph and a route table for the wrong map is worse than
    // none: it would answer confidently about rooms that are not there.
    private MapForesight? Foresight()
    {
        if (_policy is null || _run is null)
            return null;
        var horizon = (int)Math.Round(_policy.Foresight);
        if (horizon < 2)
            return null;
        if (!ReferenceEquals(_foresightMap, _run.Map))
        {
            _foresightMap = _run.Map;
            _foresight = new MapForesight(_run.Map, node => PathWeight(_policy, node), horizon);
        }
        return _foresight;
    }

    // Which rooms of the told route have already been walked. Node ids repeat across acts on a generated
    // map (r3c1 is a row and a column, not a name), so a route naming act one's rooms would otherwise be
    // matched again in act two by coincidence of naming.
    private readonly HashSet<string> _walkedRoute = new(StringComparer.Ordinal);

    private RunMap? _foresightMap;
    private MapForesight? _foresight;

    // ── WHAT IT TAKES ────────────────────────────────────────────────────────────────────────────────────
    // A reward, a relic, a card off a shelf — offered by name, and answered by index. The name is for the log
    // only: WHICH thing each offer is comes from `arts`, the same identity a reward screen draws its card
    // face from (RunEntityLabeler.ArtFor), because a display string cannot be turned back into an id.
    //
    // ⚠ THE DICE PLAYER'S DRAWS ARE FROZEN — the golden set is recorded through them. Its arm below is the
    // code that was here before anything was scored: the skip roll first, then the picks. The two arms are
    // whole rather than sharing a tail so that nothing done for the policy can reach into it.
    public IReadOnlyList<int> EntityPicks(
        IReadOnlyList<string> displays, IReadOnlyList<EntityArt?> arts, int count, bool allowSkip, string purpose,
        RunChoiceIntent intent = RunChoiceIntent.Keep)
    {
        ArgumentNullException.ThrowIfNull(displays);
        ArgumentNullException.ThrowIfNull(arts);

        if (_policy is null)
        {
            var rolled = allowSkip && _rng.NextDouble() < 0.2 ? [] : RunBot.Pick(_rng, displays.Count, count);
            _log.Line($"  pick [{purpose}{(intent == RunChoiceIntent.Remove ? ", giving up" : "")}] -> "
                + (rolled.Count == 0 ? "skipped" : string.Join(", ", rolled.Select(i => displays[i])))
                + $" (of {displays.Count})");
            return rolled;
        }

        // Every offer through the same evaluator that scores a card in hand. The tie is broken by ONE
        // permutation drawn whether it is needed or not: a relic pick, where the crude features make almost
        // everything score alike, must still vary between runs instead of always taking whatever the reward
        // source happened to print first — and the number of dice a pick throws must depend on how many
        // offers there were and on NOTHING ELSE, or the two seats would part company over a tie.
        var order = RunBot.Pick(_rng, displays.Count, displays.Count);
        var rank = new int[displays.Count];
        for (var at = 0; at < order.Count; at++)
            rank[order[at]] = at;

        var scores = new double[displays.Count];
        for (var i = 0; i < displays.Count; i++)
            scores[i] = ScoreOffer(i < arts.Count ? arts[i] : null);

        // ⚠⚠ WHICH END OF THE LIST IS THE GOOD END DEPENDS ON WHAT THE CHOICE IS FOR, and until the engine
        // was made to say so (RunChoiceIntent) this method always took the best — which is right for a
        // reward or an upgrade and exactly backwards for a removal. Asked to give up a card, the runner
        // handed over its best one, at forty-three authored prompts in this game alone.
        var ranked = intent == RunChoiceIntent.Remove
            ? [.. Enumerable.Range(0, displays.Count).OrderBy(i => scores[i]).ThenBy(i => rank[i])]
            : Enumerable.Range(0, displays.Count)
                .OrderByDescending(i => scores[i]).ThenBy(i => rank[i]).ToList();
        var best = ranked[0];
        var take = intent != RunChoiceIntent.Remove
            && allowSkip && WalksAway(best < arts.Count ? arts[best] : null, scores[best])
            ? []
            : ranked.Take(count).ToList();
        _log.Line($"  pick [{purpose}] -> "
            + (take.Count == 0
                ? $"skipped (best {displays[best]} {scores[best]:0.##})"
                : string.Join(", ", take.Select(i => $"{displays[i]} {scores[i]:0.##}")))
            + $" (of {displays.Count})");
        return take;
    }

    // WHETHER TO WALK AWAY, asked the way a player asks it: is this CARD better than the ones I already
    // carry? What is compared is not the score itself but the share of the deck it beats, against the
    // policy's fussiness — RewardSkip 0 takes everything, 1 takes only what beats the whole deck. Scale-free
    // on purpose: the weights are bred in a range where the absolute size of a score means nothing.
    //
    // ⚠⚠ ONLY A CARD IS EVER REFUSED, and the first version of this method is why the warning is here. It
    // ranked EVERY offer against the deck, so a relic — which the crude features score at about one point,
    // while a deck card scores several — beat almost nothing in the deck and was walked away from. One
    // measured run left act IV with FOUR relics where the dice player had twenty-seven, and it lost about
    // 4000 more health on the way. A relic is not a card, is not drawn, is not paid for out of a turn, and
    // comparing the two numbers was comparing nothing: what is free is taken.
    private bool WalksAway(EntityArt? art, double best)
    {
        if (art is not { Kind: EntityArt.Card } || _policy!.RewardSkip <= 0 || _run is not { Deck.Count: > 0 } run)
            return false;
        var beaten = run.Deck.Count(c => Score(c.DefinitionId.value) < best) / (double)run.Deck.Count;
        return beaten < _policy.RewardSkip;
    }

    // What an offer is worth: a card by what its program does and what it costs, a relic by what its rules
    // and run effects do. Anything the run can offer that is neither — gold, healing, a nested reward — has
    // no identity to look up and scores as nothing, which ranks it under any card worth having and over any
    // card that is actively bad.
    private double ScoreOffer(EntityArt? art) => art switch
    {
        { Kind: EntityArt.Card } card => Score(card.Id),
        { Kind: EntityArt.Relic } relic => Weighted(_options.Features?.ForRelic(relic.Id)),
        _ => 0,
    };

    public EventChoice Choose(EventSituation situation, IReadOnlyList<EventChoice> choices)
    {
        ArgumentNullException.ThrowIfNull(situation);
        ArgumentNullException.ThrowIfNull(choices);
        var choice = choices[PickChoice(choices)];
        _log.Line($"  choice [{situation.Id}] -> {choice.Id} "
            + $"(of {string.Join(" ", choices.Select(c => c.Id))})");
        return choice;
    }

    public void Crashed(RunState? run, Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        Crash = ex.ToString();
        Reason = $"an exception escaped at {(run is null ? "—" : RunBot.Where(run))}";
        _log.Line($"!! CRASH {Crash}");
    }

    // ⚠ ONLY A RUN THAT DIED IS WORTH OPENING UP. A walk that stopped because a guard tripped or the budget
    // ran out did not lose the fight; it was never allowed to finish it.
    public string Autopsy(RunState? run)
    {
        if (!_options.Autopsy || _turnsBack.Count == 0 || run?.Result != RunResult.Defeat)
            return "";

        var found = new FightSolver(_options.AutopsyPositions, _options.AutopsySeconds).Examine([.. _turnsBack]);
        var read = found.Verdict switch
        {
            FightVerdict.Avoidable =>
                $"the hero could still have lived {found.LastChance} turns before it died — this loss is the runner's",
            FightVerdict.Unavoidable =>
                $"nothing survives the last {found.Looked} turns, not even a player who could see the deck",
            _ => $"the last {found.ProvenLost} turns were already lost; further back the search ran out",
        };
        _log.Line($"  AUTOPSY {_doorName}: {found.Verdict} — {read} "
            + $"({found.Positions} positions, {found.Seconds:0.0}s)");
        return $"verdict={found.Verdict} lastChance={found.LastChance} provenLost={found.ProvenLost} "
            + $"kept={found.Looked} positions={found.Positions} seconds={found.Seconds:0.0} at={_doorName}";
    }

    // The start of a hero turn, kept for the autopsy: one fork per turn, and only the last few.
    private void KeepThisTurn(InteractiveCombat combat)
    {
        if (_keptThisTurn || (!_options.Autopsy && !_options.Exam))
            return;
        _keptThisTurn = true;

        // ⚠ THE EXAM KEEPS EVERY TURN, THE AUTOPSY ONLY THE LAST FEW. They want different things from the
        // same fork: the autopsy asks how a death came about and only the end of the fight can answer that,
        // while the exam grades the player and every position it stood in is a question. The ceiling is
        // memory, not meaning — a run holds a few hundred hero-turns and each one is a fight-sized copy.
        if (_options.Exam && _satAt.Count < SeatsInAnExam)
            _satAt.Add(combat.Fork());

        if (!_options.Autopsy)
            return;
        _turnsBack.Enqueue(combat.Fork());
        while (_turnsBack.Count > TurnsKept)
            _turnsBack.Dequeue();
    }

    private const int SeatsInAnExam = 300;
    private readonly List<InteractiveCombat> _satAt = [];

    // What the champion did with the positions it actually stood in, against what a proof says was possible.
    // Empty unless someone asked.
    public string Exam()
    {
        if (!_options.Exam || _satAt.Count == 0)
            return "";

        var champion = new Champion(_play, _policy);
        var sat = ChampionExam.Sit(_satAt, champion, _options.ExamTurns, _options.ExamSeconds);
        _log.Line($"  EXAM beaten at {sat.Beaten}/{sat.Judged} positions "
            + $"(won {sat.Won}/{sat.Winnable}, held {sat.Held}/{sat.Survivable})");
        return $"turns={_options.ExamTurns} positions={sat.Positions} "
            + $"beaten={sat.Beaten}/{sat.Judged} lostHp={sat.LostHealth} lostDmg={sat.LostDamage} "
            + $"won={sat.Won}/{sat.Winnable} held={sat.Held}/{sat.Survivable} "
            + $"hopeless={sat.Hopeless} undecided={sat.Undecided} "
            + $"dealt={sat.Dealt} seconds={sat.Seconds:0.0}";
    }

    public BotResult Finish(RunState? run, string? error, bool complete)
    {
        // The last look at the fight the run ended in. A hero killed by the closing blow leaves a combat
        // nobody asks about again, and that blow is the single most interesting line in the whole receipt.
        if (_ledgerFight is { } last)
            Ledger.Read(last, _ledgerAct, _ledgerRoom, _ledgerRole);

        // ⚠⚠ THE BLOW THAT ENDS A RUN IS IN NO TALLY. `DamageTaken` is health watched BEFORE each answer,
        // and a run that dies answers nothing further — so for every losing run the number it reports is
        // short by exactly the killing blow (seed 1: 56 reported against 70 actually spent). It is left
        // that way on purpose here: `sim-fitness` is a contract the golden set diffs field for field, and
        // moving it is a decision somebody makes, not a side effect of building a receipt. What the closing
        // blow IS gets its own number, so that the ledger can be reconciled against the truth.
        ClosingDamage = Math.Max(0, _hpLastSeen - (run?.Health.Current ?? _hpLastSeen));
        return Report(run, error, complete);
    }

    private BotResult Report(RunState? run, string? error, bool complete) => new()
    {
        Seed = _options.Seed,
        Maps = _options.Maps,
        Policy = _policy?.Name ?? "random",
        Result = run?.Result.ToString() ?? "",
        Acts = Acts,
        Fights = Fights,
        Health = run?.Health.Current ?? 0,
        MaxHealth = run?.Health.Max ?? 0,
        Problems = Problems,
        Error = error ?? "none",
        Seconds = _clock.Elapsed.TotalSeconds,
        Reason = Reason,
        Crash = Crash,
        Rooms = Rooms,
        Walked = Walked,
        DamageTaken = DamageTaken,
        ClosingDamage = ClosingDamage,
        Healed = Healed,
        DamageAtActBoss = DamageAtActBoss,
        HealthAtActBoss = HealthAtActBoss,
        Complete = complete,
        Where = Where,
        WhereRole = WhereRole,
        WhereContent = _roomContent,
        Visits = Visits,
        Autopsy = Autopsy(run),
        Exam = Exam(),
        Damage = Ledger,
    };

    // Which door a runner takes. A shop is answered as a shop — how eagerly it spends is a weight of its own,
    // and WHAT it spends the gold on is the same evaluator that scores a card in hand — and every other
    // situation still by one knob: the first option, the last, or somewhere in between. (Doors by their
    // effect rather than by their position is B2, and it is not this change.)
    private int PickChoice(IReadOnlyList<EventChoice> choices)
    {
        if (_policy is null)
            return _rng.Next(choices.Count);
        var buys = Enumerable.Range(0, choices.Count)
            .Where(i => choices[i].Id.StartsWith("buy-", StringComparison.Ordinal)).ToList();
        var leave = choices.ToList().FindIndex(c => c.Id == "leave");

        // ⚠⚠ A REST SITE HAS A WAY OUT TOO, AND FOR THE WHOLE HISTORY OF THIS RUNNER THAT WAS ENOUGH TO MAKE
        // IT LEAVE. The shop arm below asked only whether a door said "leave" — a rest site says it, offers
        // `rest` and `amend` beside it, and has nothing to buy, so the runner walked in, walked out, and
        // never healed once. `healed=0` over eighteen rooms. Nobody saw it for the whole arc because every
        // measurement until B6 was taken on a 9999-hp body, where never resting costs exactly nothing.
        //
        // A body that can die rests when it is hurt. How hurt is a question for the search (RestBelow), not
        // for this comment. (Reading EVERY door by what it does, rather than these two by name, is B2.)
        if (buys.Count == 0 && _run is { } run && run.Health.Max > 0
            && run.Health.Current < run.Health.Max * (_policy.RestBelow <= 0 ? 0 : _policy.RestBelow))
        {
            var heals = Enumerable.Range(0, choices.Count)
                .Select(i => (at: i, heal: Heals(choices[i])))
                .Where(x => x.heal > 0)
                .ToList();
            if (heals.Count > 0)
                return heals.OrderByDescending(x => x.heal).ThenBy(x => x.at).First().at;
        }

        // ⚠⚠ EVERY OTHER DOOR, READ BY WHAT IT DOES (B2). Until this existed a door was chosen by WHERE IT
        // WAS PRINTED: one weight, clamped into the list's index. A bred EventLate of 0.1 meant "always take
        // the first door", through every event in the game, sight unseen. EventLate is still the answer for
        // a situation whose doors say nothing this can read — a conversation, a flag, a program by id — so
        // the gene keeps its meaning where it is the only thing there is.
        if (buys.Count == 0)
        {
            var worth = choices.Select(DoorWorth).ToArray();
            if (worth.Any(w => Math.Abs(w) > 0.0001))
                return Enumerable.Range(0, choices.Count)
                    .OrderByDescending(i => worth[i]).ThenBy(i => i).First();
        }

        if (leave >= 0)
        {
            if (buys.Count == 0 || _rng.NextDouble() >= _policy.ShopBuy)
                return leave;
            // The best thing on the shelf, and the cheaper of two that are worth the same. Price breaks a tie
            // instead of entering the score, because what a point of score is worth in gold is a number
            // nobody has bred — and a wrong exchange rate would be worse than none.
            return buys
                .OrderByDescending(i => ScoreOffer(RunEntityLabeler.ArtForGrant(choices[i].Effects)))
                .ThenBy(i => PriceOf(choices[i]))
                .First();
        }
        return Math.Clamp((int)Math.Round(_policy.EventLate * (choices.Count - 1)), 0, choices.Count - 1);
    }

    // What a door is worth, in average cards — the same unit a reward offer is scored in, so that a door
    // handing over a card and a door handing over gold can be held against each other at all. What it COSTS
    // is subtracted by the same reckoning: a door is its whole bargain, not its better half.
    //
    // ⚠ What cannot be read scores nothing rather than something: a flag, a program named by id, a rule
    // installed for the next fight. A door made only of those falls through to EventLate, which is honest —
    // the runner has no opinion, and says so by keeping the one it always had.
    private double DoorWorth(EventChoice choice) =>
        choice.Effects.Sum(Worth)
        // A price pays for itself: it is written as the effects that settle it, and gold leaving the purse
        // is a negative delta, so the cost side needs no sign of its own.
        + (choice.Costs ?? []).Sum(cost => cost.Pay.Sum(Worth));

    private double Worth(IRunEffectRequest effect)
    {
        var run = _run;
        var max = run?.Health.Max ?? 0;
        double InHealth(double points) => max <= 0 ? 0 : points / max * _policy!.DoorHealth;

        return effect switch
        {
            HealRunEffect heal => InHealth(Math.Min(heal.Amount, max - (run?.Health.Current ?? 0))),
            ComputedHealRunEffect computed when run is not null =>
                InHealth(Math.Min(computed.Amount.Evaluate(run), max - run.Health.Current)),
            ApplyRunDamageRunEffect hurt => -InHealth(hurt.Amount),
            ComputedDamageRunEffect hurt when run is not null => -InHealth(hurt.Amount.Evaluate(run)),
            ChangeMaxHealthRunEffect change => InHealth(change.Delta),
            // Gold, and the same line pays for the cost side: a price is a negative delta.
            ChangeResourceRunEffect resource => resource.Delta / 100.0 * _policy!.DoorGold,
            ComputedResourceRunEffect resource when run is not null =>
                resource.Amount.Evaluate(run) / 100.0 * _policy!.DoorGold,
            AddCardToDeckRunEffect card => Score(card.Card.value),
            AddRelicByIdRunEffect relic => Weighted(_options.Features?.ForRelic(relic.Relic.Value)),
            AddRelicRunEffect relic => Weighted(_options.Features?.ForRelic(relic.Relic.Id.Value)),
            // A reward that opens a reward: what is behind it is not rolled yet, so it is worth what an
            // average card is worth, which is what the unit is defined as.
            OfferRewardRunEffect further => Math.Max(1, further.PickCount),
            // Giving up the worst card in a deck is worth exactly what it costs that deck to carry it.
            RemoveCardsRunEffect => Thinning(),
            UpgradeCardsRunEffect upgrade => Sharpening() * Math.Max(1, upgrade.Levels),
            ConditionalRunEffect branch => 0,
            _ => 0,
        };
    }

    // What the deck gains by losing its worst card: the distance from that card to an ordinary one. A deck
    // of nothing but good cards gains nothing by thinning, and says so.
    private double Thinning()
    {
        if (_run is not { Deck.Count: > 1 } run)
            return 0;
        var scores = run.Deck.Select(c => Score(c.DefinitionId.value)).ToList();
        return Math.Max(0, scores.Average() - scores.Min());
    }

    // And what it gains by improving its best: an upgrade is worth a share of the card it sharpens. The
    // share is a guess and is named as one — the engine does not say what a level is worth until the
    // upgraded card exists.
    private const double AnUpgradeIsWorth = 0.25;

    private double Sharpening()
    {
        if (_run is not { Deck.Count: > 0 } run)
            return 0;
        return AnUpgradeIsWorth * run.Deck.Max(c => Score(c.DefinitionId.value));
    }

    // What a door would put back on the hero, in health. A rest that heals a SHARE of the maximum and one
    // that heals a flat amount are the same question to whoever is standing there hurt.
    private int Heals(EventChoice choice)
    {
        var run = _run;
        return choice.Effects.Sum(effect => effect switch
        {
            HealRunEffect heal => heal.Amount,
            // "A quarter of your maximum, rounded up" is an expression OVER THE RUN, and the run is standing
            // right here — so the number is read off the door rather than guessed at.
            ComputedHealRunEffect computed when run is not null => Math.Max(0, computed.Amount.Evaluate(run)),
            _ => 0,
        });
    }

    // What a shelf slot takes out of the purse. A price is written as the payment that settles it, so the
    // cheapest slot is the one whose cost effects subtract the least; a slot paid for some other way (credit,
    // a favour) reads as free here, which is the right answer for a tiebreak and the wrong one for a term.
    private static int PriceOf(EventChoice choice) =>
        (choice.Costs ?? [])
            .SelectMany(cost => cost.Pay)
            .OfType<ChangeResourceRunEffect>()
            .Sum(pay => Math.Max(0, -pay.Delta));

    private double Score(string cardId) =>
        Weighted(_options.Features?.For(cardId))
        // A card is paid for out of a turn; a relic is not, which is why the cost term lives here and not in
        // the weighing itself.
        + _policy!.WCost * RunBot.FullCosts(_play, cardId).Sum(c => c.Amount);

    private double Weighted(double[]? features)
    {
        var f = features ?? new double[CardFeatures.Count];
        return _policy!.WDamage * f[0] + _policy.WBlock * f[1] + _policy.WStatus * f[2]
            + _policy.WDraw * f[3] + _policy.WResource * f[4];
    }

    // The role weight of a room, so a runner can prefer elites (more spoils, more damage) or avoid them.
    private static double PathWeight(BotPolicy policy, Node node) => MapRole.Of(node) switch
    {
        "elite" => policy.PathElite,
        "shop" => policy.PathShop,
        "rest" => policy.PathRest,
        "event" => policy.PathEvent,
        "treasure" => policy.PathTreasure,
        _ => policy.PathCombat,
    };
}

// A card the mind has decided to play, at whom, and how long the fight's step list was before it went in —
// which is how the seat finds out afterwards what the engine made of it.
internal sealed record CardPlay(CardInstance Card, CombatantId? Target, int StepsBefore);
