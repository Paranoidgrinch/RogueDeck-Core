using System.Diagnostics;
using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Bot;

// ── THE AUTOPSY ──────────────────────────────────────────────────────────────────────────────────────────
// A runner that dies tells you it died. It does not tell you whether it COULD have lived, and that is the
// only thing the balance question wants to know: was this fight lost by the content or by the player?
//
// So when a run ends, the fight it ended in is played again — not once, but every way it could have gone.
// The fight is a single-agent puzzle with no hidden information from here: the dice are bound to the seed
// and the step count, so a fork draws what the real fight would draw; the enemy's answer is a function of
// the state; and CombatStateHasher fingerprints every position, so two orders that arrive at the same table
// are searched once.
//
// ⚠⚠ THE SEARCHER CAN SEE THE DECK, AND THAT IS THE POINT. It forks the fight, so it knows what it will
// draw — which a player does not. That makes it strictly stronger than any fair player, and therefore makes
// exactly one of its two answers a PROOF:
//
//     UNWINNABLE — nothing wins from here, and a player who could see the future could not either.
//                  That is a statement about the FIGHT.
//     WINNABLE   — a line exists for someone who knew the order of their deck. It is NOT a claim that a
//                  fair player could find it, and must never be read as one.
//     UNDECIDED  — the search ran out of budget. It says so rather than guessing.
//
// ⚠ A fork has nobody sitting at it, so a card that ASKS something is answered on the copy by the headless
// default. A fight whose cards ask a lot is explored through one of its answers, not all of them; the
// verdict is then about that reading of the fight. Nothing here pretends otherwise.
public enum FightVerdict
{
    // The hero could still have come out of this alive, and did not: the loss was the runner's.
    Avoidable,

    // No line survives, not even for a searcher that can see the deck. The loss was the fight's.
    Unavoidable,

    // The search ran out of budget. It says so rather than guessing.
    Undecided,
}

public sealed record FightAutopsy(
    FightVerdict Verdict,
    int LastChance,     // how many turns before the end the hero could still have avoided dying
    int ProvenLost,     // how many of the last turns were searched OUT and found to have no way to live
    int Looked,         // how many turns back were kept and asked about
    int Positions,
    double Seconds);

public sealed class FightSolver(int positionBudget = 60_000, int seconds = 60)
{
    // How many cards one turn may lay down while being searched. Beyond this the turn is explored no
    // further — a hand that loops would otherwise have no end.
    private const int PlaysDeep = 6;

    // …and how many positions one TURN may cost, so that a single wide turn cannot eat the whole budget.
    private const int PositionsPerTurn = 800;

    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly Stopwatch _clock = new();
    private int _positions;
    private int _deepest;
    private bool _cutShort;

    private bool OutOfBudget()
    {
        if (_positions < positionBudget && _clock.Elapsed.TotalSeconds < seconds)
            return false;
        _cutShort = true;
        return true;
    }

    // ⚠⚠ THE WHOLE FIGHT IS OUT OF REACH, AND PRETENDING OTHERWISE WOULD BE THE LIE. Measured: a fight runs
    // about five and a half turns and a turn reaches on the order of a few hundred distinct positions, so
    // the tree is somewhere past ten billion leaves. Sixty thousand positions of searching got four turns
    // deep and answered "undecided" every time. A verdict nobody can afford to compute is not a verdict.
    //
    // So the question is asked backwards, where it is both cheap and sharp: not "was this fight winnable?"
    // but "WHEN DID IT STOP BEING SURVIVABLE?" From the last turn, could the hero have ended it alive? From
    // two turns out, could it have lived two turns? Three? Each of those is a shallow tree, and the answer
    // is the thing a balance question actually wants:
    //
    //     Avoidable   — there was a line, and the runner did not take it. The loss is the player's.
    //     Unavoidable — nothing survives as far back as we kept, not even for a searcher that sees the
    //                   deck. The loss was already decided before this window opened.
    //
    // `turnsBack` positions are handed over newest LAST, exactly as the fight went.
    public FightAutopsy Examine(IReadOnlyList<InteractiveCombat> turnsBack)
    {
        ArgumentNullException.ThrowIfNull(turnsBack);
        _clock.Restart();
        _positions = 0;
        _cutShort = false;

        // ⚠ A LOSS PROVEN AT ONE DEPTH DOES NOT END THE QUESTION. The last turn can be hopeless while three
        // turns out there was still a line — that is precisely the interesting case, and it says the mistake
        // was made three turns before anyone died. So a proven loss keeps asking deeper; only a search that
        // could not decide stops the walk, because past that point the answers would be guesses.
        var last = 0;
        var proven = 0;
        var undecided = false;
        for (var back = 1; back <= turnsBack.Count; back++)
        {
            _seen.Clear();
            _cutShort = false;
            if (Survives(turnsBack[^back], back))
            {
                last = back;
                continue;
            }
            if (_cutShort)
            {
                undecided = true;
                break;
            }
            proven = back;
        }

        return new FightAutopsy(
            last > 0 ? FightVerdict.Avoidable
                : undecided ? FightVerdict.Undecided
                : FightVerdict.Unavoidable,
            last,
            proven,
            turnsBack.Count,
            _positions,
            _clock.Elapsed.TotalSeconds);
    }

    // ── THE ONE QUESTION, ASKED DIRECTLY (C0) ────────────────────────────────────────────────────────────
    // `Examine` walks a death backwards to find when it became unavoidable. This asks the single question
    // underneath it about ONE position, which is what lets a PLAYER be held against a proof: from here, does
    // a line exist that is still standing `turns` hero-turns later?
    //
    // ⚠ The same three answers and the same honesty. Yes is a line a searcher that SEES THE DECK can walk,
    // so it is an upper bound on any fair player rather than a promise. No is the proof — nothing survives.
    // Undecided says the budget ran out rather than guessing.
    public FightVerdict CanSurvive(InteractiveCombat from, int turns) => Ask(from, turns, mustWin: false);

    // ⚠⚠ AND THE QUESTION THAT ACTUALLY MATTERS, WHICH IS NOT THE SAME ONE. Surviving is not winning, and
    // a player graded only on surviving is graded on the one thing a standstill is perfect at — measured:
    // the champion held 186 of 187 survivable positions and still lost every run it was in. A ceiling a
    // player is already at measures nothing.
    //
    // So: from here, is there a line that has the fight WON within `turns`? Same search, same three answers,
    // one different goal — and a far harder question, because surviving accepts any line that is still
    // standing while winning has to put something down. Expect more `Undecided`, and read it as the budget
    // speaking rather than as a verdict.
    public FightVerdict CanWin(InteractiveCombat from, int turns) => Ask(from, turns, mustWin: true);

    private FightVerdict Ask(InteractiveCombat from, int turns, bool mustWin)
    {
        ArgumentNullException.ThrowIfNull(from);
        _clock.Restart();
        _positions = 0;
        _cutShort = false;
        _mustWin = mustWin;
        _seen.Clear();

        if (Survives(from, turns))
            return FightVerdict.Avoidable;
        return _cutShort ? FightVerdict.Undecided : FightVerdict.Unavoidable;
    }

    // Whether the goal is to have WON by the horizon rather than merely to be standing at it. Held on the
    // solver rather than threaded through the recursion because the recursion is the same walk either way.
    private bool _mustWin;

    public int Positions => _positions;

    public double Seconds => _clock.Elapsed.TotalSeconds;

    // ── WHAT WAS ACTUALLY AVAILABLE FROM HERE (C0b) ──────────────────────────────────────────────────────
    // The yes/no questions above are only asked where the answer is interesting, and that turned out to be a
    // narrow place: "winnable within three turns" only ever means "the enemy is nearly dead", and over four
    // runs the champion won 82 of the 84 such positions. A grade a player is already at the ceiling of
    // measures nothing, which is exactly what `CanSurvive` had to be replaced for.
    //
    // So this asks a question with an answer at EVERY position: what outcomes were reachable at all?
    //
    // ⚠⚠ AND IT REFUSES TO WEIGH THEM AGAINST EACH OTHER, because that is where this project keeps going
    // wrong. "Best" needs a trade between health kept and damage dealt, and every number we have invented
    // for that trade has eventually rewarded standing still — twice in the champion's own evaluator. So no
    // trade is invented here. What comes back is the PARETO FRONTIER: every outcome that nothing else beats
    // on both counts at once.
    //
    // A player is then graded by DOMINANCE, which needs no weights: is there a line that would have kept at
    // least as much health AND taken at least as much off them, and strictly more of one? If there is, the
    // player left something on the table, and the amount is not an opinion. If there is not, it played on
    // the frontier — whatever trade it chose was a defensible one.
    //
    // ⚠ Blocking forever cannot farm this. A line that keeps every point of health and deals nothing is on
    // the frontier only while nothing else deals more at the same health, which in a real fight is rare.
    public readonly record struct Outcome(int HeroHealth, int Dealt)
    {
        public bool Beats(Outcome other) =>
            HeroHealth >= other.HeroHealth && Dealt >= other.Dealt
            && (HeroHealth > other.HeroHealth || Dealt > other.Dealt);
    }

    // Every outcome reachable in `turns` hero-turns that nothing else beats on both counts. Empty means the
    // search could not afford an answer — which it says rather than pretending the frontier is empty.
    public IReadOnlyList<Outcome> Frontier(InteractiveCombat from, int turns)
    {
        ArgumentNullException.ThrowIfNull(from);
        _clock.Restart();
        _positions = 0;
        _cutShort = false;
        _mustWin = false;
        _seen.Clear();

        var standing = Enemies(from);
        var found = Reachable(from, turns, standing);
        return _cutShort ? [] : found;
    }

    private List<Outcome> Reachable(InteractiveCombat at, int turns, int enemiesAtTheStart)
    {
        // A decided fight is one outcome and no further question. Death keeps no health whatever it dealt,
        // which is what puts it under every line that is still standing.
        if (at.Result == CombatResult.Defeat)
            return [new Outcome(0, enemiesAtTheStart - Enemies(at))];
        if (at.Result == CombatResult.Victory || turns <= 0)
            return [new Outcome(at.HeroHealth, enemiesAtTheStart - Enemies(at))];
        if (OutOfBudget())
            return [];

        var ends = new List<(InteractiveCombat After, string Played)>();
        Turn(at, depth: 0, played: [], ends);

        var frontier = new List<Outcome>();
        foreach (var (after, _) in ends)
        {
            if (OutOfBudget())
                return frontier;
            foreach (var outcome in Reachable(after, turns - 1, enemiesAtTheStart))
                Keep(frontier, outcome);
        }
        return frontier;
    }

    // Add an outcome and drop everything it beats; skip it if anything already there beats it.
    private static void Keep(List<Outcome> frontier, Outcome candidate)
    {
        foreach (var held in frontier)
            if (held.Beats(candidate) || held == candidate)
                return;
        frontier.RemoveAll(candidate.Beats);
        frontier.Add(candidate);
    }

    private static int Enemies(InteractiveCombat combat) => combat.State.Combatants
        .Where(c => c.Id != combat.HeroId && c.IsAlive && c.TeamId == StandardCombatIds.EnemyTeam)
        .Sum(c => c.Health.Current);

    // Is there ANY way to still be standing `turns` hero-turns from here?
    private bool Survives(InteractiveCombat at, int turns)
    {
        _deepest = Math.Max(_deepest, turns);

        if (at.Result == CombatResult.Defeat)
            return false;
        // Winning is surviving, and better.
        if (at.Result == CombatResult.Victory)
            return true;
        // ⚠ THE HORIZON MEANS THE OPPOSITE THING FOR THE TWO QUESTIONS. Still standing when the turns run
        // out is a SURVIVAL; it is not a win, and for the winning question it is the line failing.
        if (turns <= 0)
            return !_mustWin;
        if (OutOfBudget())
            return false;

        var ends = new List<(InteractiveCombat After, string Played)>();
        Turn(at, depth: 0, played: [], ends);

        // The healthiest futures first: a line that survives is usually found in one of them, and finding it
        // early is the whole difference between an answer and a budget.
        foreach (var (after, _) in ends.OrderByDescending(e => e.After.HeroHealth))
        {
            if (OutOfBudget())
                return false;
            if (Survives(after, turns - 1))
                return true;
        }

        return false;
    }

    // Every way THIS turn could be played, each handed back as the position the enemies have already
    // answered — the same enumeration the champion plans with, but keeping every leaf instead of the best.
    private void Turn(
        InteractiveCombat node, int depth, IReadOnlyList<string> played,
        List<(InteractiveCombat, string)> ends)
    {
        if (ends.Count >= PositionsPerTurn || OutOfBudget())
            return;

        // ⚠⚠ A FIGHT THAT IS ALREADY DECIDED HAS NO TURN LEFT TO PLAY OUT, and treating it as if it had was
        // a real defect: the code below forks it and calls EndTurn on the copy, and the position that came
        // back was no longer the win. So a line that WON was invisible to the search — measured, when the
        // winning question was added: a hero holding a card that kills the enemy outright was told no win
        // existed, over 559 positions of looking. Survival never noticed, because it had other lines to
        // find; only asking about winning brought it out.
        //
        // A decided position is therefore handed over AS ITSELF and the walk stops there.
        // ⚠ AND IT IS ADDED WITHOUT ASKING `_seen`, which is the second half of the same defect. The caller
        // fingerprints a position BEFORE walking into it, so by the time the walk arrives the position is
        // already in `_seen` — and a dedup check here would drop the very thing it came to hand over. A
        // decided position is terminal: there is no subtree behind it to walk twice.
        if (node.IsOver)
        {
            _positions++;
            ends.Add((node, played.Count == 0 ? "(nothing)" : string.Join(" + ", played)));
            return;
        }

        // Stopping here is a way the turn can go.
        var stopped = node.Fork();
        _positions++;
        stopped.EndTurn();
        // …and the same for the positions the turn hands on: several ways of spending a turn very often end
        // it in the same place, and that place is worth searching once.
        if (_seen.Add(Shape(stopped)))
            ends.Add((stopped, played.Count == 0 ? "(nothing)" : string.Join(" + ", played)));

        if (depth >= PlaysDeep || node.IsOver || !node.IsHeroTurn)
            return;

        var hero = node.State.GetCombatant(node.HeroId);
        var living = node.State.Combatants
            .Where(c => c.Id != node.HeroId && c.IsAlive && c.TeamId == StandardCombatIds.EnemyTeam)
            .Select(c => (CombatantId?)c.Id)
            .ToList();

        foreach (var card in node.Hand.ToList())
            foreach (var target in living.Count == 0 ? [null] : living)
            {
                if (ends.Count >= PositionsPerTurn || OutOfBudget())
                    return;

                var after = node.Fork();
                _positions++;
                var steps = after.Steps.Count;
                after.PlayCard(card.Id, target);
                if (RunBot.Refused(after, steps))
                    continue;
                if (!_seen.Add(Shape(after)))
                    continue;

                Turn(after, depth + 1,
                    [.. played, $"{card.DefinitionId.value}{(target is { } at ? $"->{at.value}" : "")}"],
                    ends);
            }
    }

    // ⚠⚠ TWO COPIES OF THE SAME CARD ARE THE SAME MOVE, AND THE ENGINE'S OWN FINGERPRINT SAYS THEY ARE NOT.
    // CombatStateHasher writes card INSTANCE ids, because it exists to prove that a restored fight is the
    // same fight down to the identity of every object. For a search that is exactly wrong: playing the first
    // Paper Cut and playing the second leave positions that differ in nothing but which id sits where, and
    // the search then walks both, and both their children, and so on — the single largest source of
    // duplicated work there is, in a deck built out of copies.
    //
    // So the search fingerprints the SHAPE of a position: what each pile holds, IN ORDER, by definition
    // rather than by identity. Order is kept — a draw pile is a stack, and two piles holding the same cards
    // in a different order have different futures, so sorting them would merge positions that are not the
    // same. What is dropped is only the name of each copy.
    private static string Shape(InteractiveCombat combat)
    {
        var snapshot = combat.State.CreateSnapshot();
        var sb = new System.Text.StringBuilder(512);
        sb.Append(snapshot.RandomStep).Append('|').Append(snapshot.CurrentRound).Append('|')
            .Append(snapshot.CurrentTurn).Append('|').Append((int)snapshot.TurnPhase).Append('|')
            .Append((int)snapshot.Result).Append('|').Append(snapshot.ActiveCombatantId?.value).Append('\n');

        foreach (var c in snapshot.Combatants)
        {
            sb.Append(c.Id.value).Append(' ').Append((int)c.LifecycleState)
                .Append(' ').Append(c.HealthCurrent).Append('/').Append(c.HealthMax);
            foreach (var (key, pool) in c.Resources)
                sb.Append(" r:").Append(key.value).Append('=').Append(pool.Current);
            foreach (var (key, pool) in c.DefensivePools)
                sb.Append(" d:").Append(key.value).Append('=').Append(pool.Current);
            foreach (var status in c.Statuses)
                sb.Append(" s:").Append(status.DefinitionId.value).Append('=').Append(status.Stacks)
                    .Append('/').Append(status.DurationTurns).Append('/').Append(status.Charges)
                    .Append('/').Append(status.PendingTurns);
            foreach (var (key, value) in c.Counters)
                sb.Append(" c:").Append(key.value).Append('=').Append(value);
            sb.Append('\n');
        }

        foreach (var (combatant, zones) in snapshot.CardZones)
        {
            sb.Append(combatant.value);
            Pile(sb, "draw", zones.DrawPile);
            Pile(sb, "hand", zones.Hand);
            Pile(sb, "disc", zones.DiscardPile);
            Pile(sb, "exh", zones.ExhaustPile);
            Pile(sb, "ban", zones.BanishedPile);
            if (!zones.QueuePile.IsDefaultOrEmpty)
                Pile(sb, "queue", zones.QueuePile);
            sb.Append('\n');
        }

        return sb.ToString();
    }

    private static void Pile(
        System.Text.StringBuilder sb, string zone,
        System.Collections.Immutable.ImmutableArray<CardInstanceSnapshot> cards)
    {
        sb.Append(' ').Append(zone).Append(':');
        foreach (var card in cards)
        {
            sb.Append(card.DefinitionId.value);
            if (!card.Marks.IsDefaultOrEmpty)
                foreach (var mark in card.Marks)
                    sb.Append('#').Append(mark.value);
            if (card.QueuedTargetId is { } aimed)
                sb.Append('@').Append(aimed.value);
            sb.Append(',');
        }
    }

    private static int Standing(InteractiveCombat combat) => combat.State.Combatants
        .Where(c => c.Id != combat.HeroId && c.IsAlive && c.TeamId == StandardCombatIds.EnemyTeam)
        .Sum(c => c.Health.Current);
}
