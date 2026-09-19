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
internal enum FightVerdict
{
    // The hero could still have come out of this alive, and did not: the loss was the runner's.
    Avoidable,

    // No line survives, not even for a searcher that can see the deck. The loss was the fight's.
    Unavoidable,

    // The search ran out of budget. It says so rather than guessing.
    Undecided,
}

internal sealed record FightAutopsy(
    FightVerdict Verdict,
    int LastChance,     // how many turns before the end the hero could still have avoided dying
    int ProvenLost,     // how many of the last turns were searched OUT and found to have no way to live
    int Looked,         // how many turns back were kept and asked about
    int Positions,
    double Seconds);

internal sealed class FightSolver(int positionBudget = 60_000, int seconds = 60)
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

    // Is there ANY way to still be standing `turns` hero-turns from here?
    private bool Survives(InteractiveCombat at, int turns)
    {
        _deepest = Math.Max(_deepest, turns);

        if (at.Result == CombatResult.Defeat)
            return false;
        // Winning is surviving, and better.
        if (at.Result == CombatResult.Victory)
            return true;
        if (turns <= 0)
            return true;
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
