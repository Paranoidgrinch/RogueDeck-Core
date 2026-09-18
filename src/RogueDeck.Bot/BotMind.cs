using System.Diagnostics;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
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
    public int Healed;
    public double Seconds => _clock.Elapsed.TotalSeconds;
    public readonly List<string> Rooms = [];
    public readonly Dictionary<int, int> HealthAtActBoss = [];
    public readonly Dictionary<int, int> DamageAtActBoss = [];

    private int _loggedNarration;
    private string? _lastRoom;
    private int _hpBeforeRoom;
    private int _hpLastSeen;

    // ── The guards ───────────────────────────────────────────────────────────────────────────────────────
    // Never re-offer a play the engine refused; never repeat a play that moved nothing on the table (a card
    // may put a copy of itself back in hand for ever); give both the turn and the fight a ceiling.
    private bool _inFight;
    private int _turn;
    private int _playsThisTurn;
    private readonly HashSet<CardInstanceId> _refused = [];
    private readonly HashSet<string> _barren = new(StringComparer.Ordinal);
    private string? _lastPlayed;
    private string _tableBeforeThePlay = "";

    private void NewTurn()
    {
        _playsThisTurn = 0;
        _lastPlayed = null;
        _refused.Clear();
        _barren.Clear();
    }

    // The header line: who is walking, from what number, with what.
    public void Opening(RunState run)
    {
        ArgumentNullException.ThrowIfNull(run);
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

        var hpNow = run.Health.Current;
        if (hpNow < _hpLastSeen)
            DamageTaken += _hpLastSeen - hpNow;
        else if (hpNow > _hpLastSeen)
            Healed += hpNow - _hpLastSeen;
        _hpLastSeen = hpNow;

        if (run.CurrentNodeId?.Value is { } here && here != _lastRoom)
        {
            _lastRoom = here;
            var node = run.Map.Nodes.FirstOrDefault(n => n.Id.Value == here);
            var role = node is null ? "?" : MapRole.Of(node);
            Rooms.Add($"{run.ActNumber}:{role}");
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
        var pick = _policy is null
            ? forks[_rng.Next(forks.Count)]
            : forks.OrderByDescending(n => PathWeight(_policy, n)).First();
        _log.Line($"  fork -> {pick.Id.Value} {MapRole.Of(pick)} "
            + $"(of {string.Join(" ", forks.Select(MapRole.Of))})");
        return pick;
    }

    // A skippable offer is skipped now and then, on purpose: a deck that takes every card and a deck that
    // refuses one are different games.
    public IReadOnlyList<int> EntityPicks(IReadOnlyList<string> displays, int count, bool allowSkip, string purpose)
    {
        ArgumentNullException.ThrowIfNull(displays);
        var take = allowSkip && (_policy is null ? _rng.NextDouble() < 0.2 : _policy.RewardSkip > 0.5)
            ? []
            : RunBot.Pick(_rng, displays.Count, count);
        _log.Line($"  pick [{purpose}] -> "
            + (take.Count == 0 ? "skipped" : string.Join(", ", take.Select(i => displays[i])))
            + $" (of {displays.Count})");
        return take;
    }

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

    public BotResult Finish(RunState? run, string? error, bool complete) => new()
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
        DamageTaken = DamageTaken,
        Healed = Healed,
        DamageAtActBoss = DamageAtActBoss,
        HealthAtActBoss = HealthAtActBoss,
        Complete = complete,
    };

    // Which door a runner takes. A shop is answered as a shop — how eagerly it spends is a weight of its
    // own — and every other situation by one knob: the first option, the last, or somewhere in between.
    private int PickChoice(IReadOnlyList<EventChoice> choices)
    {
        if (_policy is null)
            return _rng.Next(choices.Count);
        var buys = Enumerable.Range(0, choices.Count)
            .Where(i => choices[i].Id.StartsWith("buy-", StringComparison.Ordinal)).ToList();
        var leave = choices.ToList().FindIndex(c => c.Id == "leave");
        if (leave >= 0)
            return buys.Count > 0 && _rng.NextDouble() < _policy.ShopBuy ? buys[_rng.Next(buys.Count)] : leave;
        return Math.Clamp((int)Math.Round(_policy.EventLate * (choices.Count - 1)), 0, choices.Count - 1);
    }

    private double Score(string cardId)
    {
        var f = _options.Features?.For(cardId) ?? new double[CardFeatures.Count];
        var cost = RunBot.FullCosts(_play, cardId).Sum(c => c.Amount);
        return _policy!.WDamage * f[0] + _policy.WBlock * f[1] + _policy.WStatus * f[2]
            + _policy.WDraw * f[3] + _policy.WResource * f[4] + _policy.WCost * cost;
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
