using System.Diagnostics;
using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Bot;

// ── THE PLANNER: A WHOLE FIGHT, PLAYED BY LOOKING FIVE TURNS AHEAD ───────────────────────────────────────
// The solver (FightSolver) answers yes/no questions about one position and runs out of budget four turns in,
// because it tries to walk the whole tree. The planner asks the question balance actually needs — "how much
// health does the best line through this fight cost?" — and gets there by RECEDING HORIZON:
//
//   1. From the current position, every way this turn can be played is laid out (the same enumeration the
//      solver walks, positions merged by their shape), and the enemies answer each one.
//   2. That is repeated `horizon` turns deep as a BEAM: after every turn, positions that another position beats
//      on every count are dropped, the rest are ranked, and the best `beam` go on.
//   3. The best position at the far end is found, and ONLY ITS FIRST TURN is played. Then the whole search
//      starts again from there — so every turn of the fight is chosen with five turns of future in view.
//
// ⚠⚠ IT SEES THE DECK. Every position is a fork of the real fight: what it draws is what the fight will draw,
// and the enemy's dice are bound to the seed. So the planner is CLAIRVOYANT, and its result is an UPPER BOUND
// on what a player who cannot see the draw pile could do. That is the point of it for balance: when even this
// line costs a lot, the fight costs a lot; when it costs little and players lose a lot, the gap is skill.
//
// ⚠ A fork has nobody sitting at it, so a card that ASKS something is answered on the copy by the headless
// default (the first option). A fight whose cards ask a lot is read through that answer.
public sealed record PlannedFight(
    bool Won,
    bool Over,
    int HeroHealthAtStart,
    int HeroHealthAtEnd,
    int Turns,
    long Positions,
    double Seconds,
    IReadOnlyList<string> Lines)
{
    public int HealthLost => HeroHealthAtStart - HeroHealthAtEnd;
}

public sealed class FightPlanner(int horizon = 5, int beam = 16, int perTurn = 300, int turnCap = 40)
{
    // How many cards one turn may lay down while being enumerated — a hand that loops has no end otherwise.
    private const int PlaysDeep = 8;

    private long _positions;

    public PlannedFight Play(InteractiveCombat start)
    {
        ArgumentNullException.ThrowIfNull(start);
        var clock = Stopwatch.StartNew();
        _positions = 0;
        var now = start.Fork();
        var heroAtStart = now.HeroHealth;
        var heroMax = Math.Max(1, now.State.GetCombatant(now.HeroId).Health.Max);
        var enemyMax = Math.Max(1, now.State.Combatants
            .Where(c => c.Id != now.HeroId && c.TeamId == StandardCombatIds.EnemyTeam)
            .Sum(c => c.Health.Max));
        var lines = new List<string>();
        var turns = 0;

        while (!now.IsOver && turns < turnCap)
        {
            if (!now.IsHeroTurn)
            {
                // Not the hero's to play (a prompt the fork answered, an enemy mid-turn): let the fight move on.
                now.EndTurn();
                turns++;
                continue;
            }
            if (Plan(now, heroMax, enemyMax) is not { } chosen)
                break;
            now = chosen.After;
            lines.Add(chosen.Played);
            turns++;
        }

        return new PlannedFight(
            now.Result == CombatResult.Victory, now.IsOver, heroAtStart,
            now.Result == CombatResult.Defeat ? 0 : now.HeroHealth, turns, _positions,
            clock.Elapsed.TotalSeconds, lines);
    }

    private sealed record Line(InteractiveCombat State, InteractiveCombat? FirstAfter, string? FirstPlayed);

    private (InteractiveCombat After, string Played)? Plan(InteractiveCombat from, int heroMax, int enemyMax)
    {
        var layer = new List<Line> { new(from, null, null) };
        for (var depth = 0; depth < horizon; depth++)
        {
            var next = new Dictionary<string, Line>(StringComparer.Ordinal);
            foreach (var line in layer)
            {
                if (line.State.IsOver)
                {
                    // A decided fight is a leaf; it stays in the running as itself.
                    next.TryAdd("over:" + next.Count, line);
                    continue;
                }
                foreach (var (after, played) in TurnEnds(line.State))
                {
                    var carried = line.FirstAfter is null ? new Line(after, after, played) : line with { State = after };
                    next.TryAdd(FightSolver.Shape(after), carried);
                }
            }
            if (next.Count == 0)
                break;
            layer = Prune(next.Values, heroMax, enemyMax);
            if (layer.All(l => l.State.IsOver))
                break;
        }

        var best = layer
            .Where(l => l.FirstAfter is not null)
            .OrderByDescending(l => Score(l.State, heroMax, enemyMax))
            .FirstOrDefault();
        return best is null ? null : (best.FirstAfter!, best.FirstPlayed!);
    }

    // WHAT A POSITION IS WORTH. A win is worth more than anything that is not a win, and more the more health it
    // keeps; a death is worth less than anything. Between them, health kept and enemy health taken count the
    // same, each as a share of its whole — no trade invented beyond "a tenth of your life is worth a tenth of
    // theirs". Standing still cannot farm this over five turns: the enemies' blows land inside the horizon.
    private static double Score(InteractiveCombat at, int heroMax, int enemyMax) => at.Result switch
    {
        CombatResult.Victory => 1000.0 + at.HeroHealth,
        CombatResult.Defeat => -1000.0,
        _ => (double)at.HeroHealth / heroMax - (double)EnemyHealth(at) / enemyMax
            // Pressure already applied is damage to come: a debuff stack on an enemy counts a little.
            + 0.002 * EnemyDebuffs(at),
    };

    // Drop every position another one beats on all counts, then keep the best `beam` of what is left.
    private List<Line> Prune(IEnumerable<Line> candidates, int heroMax, int enemyMax)
    {
        var scored = candidates
            .Select(l => (Line: l, Vector: Vector(l.State), Score: Score(l.State, heroMax, enemyMax)))
            .OrderByDescending(x => x.Score)
            .ToList();
        var kept = new List<(Line Line, int[] Vector, double Score)>();
        foreach (var candidate in scored)
        {
            if (candidate.Line.State.IsOver || !kept.Any(k => !k.Line.State.IsOver && Dominates(k.Vector, candidate.Vector)))
                kept.Add(candidate);
            if (kept.Count >= beam)
                break;
        }
        return [.. kept.Select(k => k.Line)];
    }

    // Hero health, enemy health taken, pressure on the enemies, the hero's own buffs and (negated) debuffs.
    private static int[] Vector(InteractiveCombat at)
    {
        var hero = at.State.GetCombatant(at.HeroId);
        return
        [
            at.HeroHealth,
            -EnemyHealth(at),
            EnemyDebuffs(at),
            hero.Statuses.Where(s => s.Polarity == StatusPolarity.Buff).Sum(s => Math.Max(1, s.Stacks)),
            -hero.Statuses.Where(s => s.Polarity == StatusPolarity.Debuff).Sum(s => Math.Max(1, s.Stacks)),
        ];
    }

    private static bool Dominates(int[] a, int[] b)
    {
        var strictly = false;
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] < b[i])
                return false;
            if (a[i] > b[i])
                strictly = true;
        }
        return strictly;
    }

    private static int EnemyHealth(InteractiveCombat at) => at.State.Combatants
        .Where(c => c.Id != at.HeroId && c.IsAlive && c.TeamId == StandardCombatIds.EnemyTeam)
        .Sum(c => c.Health.Current);

    private static int EnemyDebuffs(InteractiveCombat at) => at.State.Combatants
        .Where(c => c.Id != at.HeroId && c.IsAlive && c.TeamId == StandardCombatIds.EnemyTeam)
        .Sum(c => c.Statuses.Where(s => s.Polarity == StatusPolarity.Debuff).Sum(s => Math.Max(1, s.Stacks)));

    // Every way THIS turn can be played, each handed back as the position after the enemies answered it,
    // merged by shape so that two orders of the same cards are one position.
    private List<(InteractiveCombat After, string Played)> TurnEnds(InteractiveCombat from)
    {
        var ends = new List<(InteractiveCombat, string)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Walk(from, 0, [], ends, seen);
        return ends;
    }

    private void Walk(
        InteractiveCombat node, int depth, IReadOnlyList<string> played,
        List<(InteractiveCombat, string)> ends, HashSet<string> seen)
    {
        if (ends.Count >= perTurn)
            return;
        var label = played.Count == 0 ? "(nothing)" : string.Join(" + ", played);
        if (node.IsOver)
        {
            ends.Add((node, label));
            return;
        }

        var stopped = node.Fork();
        _positions++;
        stopped.EndTurn();
        if (seen.Add("end:" + FightSolver.Shape(stopped)))
            ends.Add((stopped, label));

        if (depth >= PlaysDeep || !node.IsHeroTurn)
            return;

        var living = node.State.Combatants
            .Where(c => c.Id != node.HeroId && c.IsAlive && c.TeamId == StandardCombatIds.EnemyTeam)
            .Select(c => (CombatantId?)c.Id)
            .ToList();
        // Two copies of a card are the same move: try each definition once per position.
        foreach (var card in node.Hand.GroupBy(c => c.DefinitionId).Select(g => g.First()).ToList())
            foreach (var target in living.Count == 0 ? [null] : living)
            {
                if (ends.Count >= perTurn)
                    return;
                var after = node.Fork();
                _positions++;
                var steps = after.Steps.Count;
                after.PlayCard(card.Id, target);
                if (RunBot.Refused(after, steps))
                    continue;
                if (!seen.Add("mid:" + FightSolver.Shape(after)))
                    continue;
                Walk(after, depth + 1,
                    [.. played, $"{card.DefinitionId.value}{(target is { } at ? $"->{at.value}" : "")}"],
                    ends, seen);
            }
    }
}
