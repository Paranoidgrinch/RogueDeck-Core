using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Bot;

// ── HOW GOOD IS THE PLAYER, IN THE FIGHT, AS A NUMBER (C0) ───────────────────────────────────────────────
// Until now the only way to ask whether the champion played well was to play whole runs and count rooms:
// dozens of runs per answer, the variance of five acts on top, and an answer that mixes the fighting with
// the doors, the shops and the luck of the draw. A change to how a card is chosen could not be told from
// noise in an afternoon.
//
// This asks it directly, and the two halves of the question were both already here:
//
//     THE PROOF   — FightSolver.CanSurvive: from this position, does a line exist that is still standing
//                   N hero-turns later? It forks the fight, so it sees the deck, which makes a YES an upper
//                   bound on any fair player and a NO a proof that nobody could.
//     THE PLAYER  — the champion, handed the same position and asked to play those N turns for real.
//
//     A position the proof calls survivable and the player dies in is a MISS, and a miss is not an opinion.
//
// ⚠⚠ AND SURVIVING IS NOT WINNING, WHICH THE FIRST RUN OF THIS EXAM SAID OUT LOUD. The champion held 186 of
// 187 survivable positions — and lost every run it was in. A player that blocks is perfect at not dying, so
// grading on survival grades the standstill this project has already had twice. A ceiling a player is
// standing on measures nothing.
//
// So the exam asks BOTH questions of every position, and the second one is the grade:
//
//     HELD   — of the positions a line survives, how many did the champion survive? A guard against
//              regressions, near its ceiling, and never read as a score.
//     WON    — of the positions a line WINS from within the horizon, how many did the champion win?
//
// ⚠⚠ AND BOTH OF THOSE TURNED OUT TO BE CEILINGS TOO: held 186/187, won 82/84. "Winnable within three
// turns" only ever means the enemy is nearly dead, so the grade was being taken over the easy positions and
// the hard ones were exactly the `undecided` ones. A grade a player is already standing on measures nothing.
//
//     BEATEN — the one with an answer at EVERY position. The solver returns the PARETO FRONTIER of what was
//              reachable (FightSolver.Frontier), and the champion's own outcome is held against it: was
//              there a line that kept at least as much health AND took at least as much off them, with
//              strictly more of one? No trade between health and damage is invented anywhere — which
//              matters, because every number this project has invented for that trade has eventually
//              rewarded standing still. Blocking forever does not farm it either: a line that keeps
//              everything and deals nothing is only on the frontier while nothing else deals more at the
//              same health.
//
// `beaten` is the grade. `lostHealth` and `lostDamage` say by how much, added up, so that a smaller number
// of worse mistakes is not confused with a larger number of small ones.
//
// ⚠ THE PROOF SEES THE DECK AND THE PLAYER DOES NOT — at one turn of horizon. A champion given a deeper
// horizon would start seeing it too, and then this comparison would be a player graded against itself. That
// is the fairness switch the arc has to decide before the horizon grows (C4), and it is named here so that
// nobody reads a future number as this one.
public static class ChampionExam
{
    public sealed record Verdict(
        int Positions,       // how many were asked about
        int Survivable,      // …of which a line survives the horizon
        int Held,            // …of which the champion was still standing. A guard, not a score.
        int Winnable,        // …of which a line has the fight WON inside the horizon
        int Won,             // …of which the champion won it. THE NUMBER.
        int Hopeless,        // positions nothing survives — not counted either way
        int Undecided,       // a proof ran out of budget and said so
        int Dealt,           // enemy health the champion took off
        int Judged,          // positions whose frontier the search could afford
        int Beaten,          // …at which a line beat the champion on both counts. THE GRADE.
        int LostHealth,      // health those lines would have kept and it did not, added up
        int LostDamage,      // damage those lines would have dealt and it did not, added up
        double Seconds);

    // `positions` are hero-turn starts, taken off real fights. Each is asked about on its own: the proof
    // walks one copy, the champion walks another, and neither touches the fight they came from.
    public static Verdict Sit(
        IReadOnlyList<InteractiveCombat> positions, Champion champion, int turns = 3,
        int solverSeconds = 10, int solverPositions = 200_000)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(champion);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        int survivable = 0, held = 0, winnable = 0, won = 0, hopeless = 0, undecided = 0, dealt = 0;
        int judged = 0, beaten = 0, lostHealth = 0, lostDamage = 0;

        foreach (var position in positions)
        {
            var lives = new FightSolver(solverPositions, solverSeconds).CanSurvive(position.Fork(), turns);
            if (lives == FightVerdict.Unavoidable)
            {
                hopeless++;
                continue;
            }
            if (lives == FightVerdict.Undecided)
            {
                undecided++;
                continue;
            }

            // ⚠ THE PLAYER PLAYS ONCE AND IS GRADED TWICE. Asking it to play again for the second question
            // would grade a different walk, and the two answers would no longer be about one decision.
            survivable++;
            var before = Champion.Standing(position);
            var (alive, finished, after, health) = Play(position.Fork(), champion, turns);
            if (alive)
                held++;
            dealt += Math.Max(0, before - after);

            if (new FightSolver(solverPositions, solverSeconds).CanWin(position.Fork(), turns)
                == FightVerdict.Avoidable)
            {
                winnable++;
                if (finished)
                    won++;
            }

            // ── AND THE GRADE THAT HAS AN ANSWER HERE WHATEVER HAPPENED ──────────────────────────────────
            var frontier = new FightSolver(solverPositions, solverSeconds).Frontier(position.Fork(), turns);
            if (frontier.Count == 0)
                continue;   // the search could not afford this one; it says so rather than scoring it

            judged++;
            var mine = new FightSolver.Outcome(health, Math.Max(0, before - after));
            var over = frontier.Where(o => o.Beats(mine)).ToList();
            if (over.Count == 0)
                continue;

            beaten++;
            // How much was left on the table: the most health any beating line kept, and the most damage
            // any of them dealt. Not one line's numbers — the frontier is not a single plan, and quoting
            // one point of it as "what it should have done" would be a claim nobody measured.
            lostHealth += over.Max(o => o.HeroHealth) - mine.HeroHealth;
            lostDamage += over.Max(o => o.Dealt) - mine.Dealt;
        }

        return new Verdict(positions.Count, survivable, held, winnable, won, hopeless, undecided, dealt,
            judged, beaten, lostHealth, lostDamage, clock.Elapsed.TotalSeconds);
    }

    // The champion playing `turns` of its own hero-turns on a copy, exactly as the seat would drive it:
    // plan the turn, lay the cards down, end it, let the enemies answer.
    //
    // ⚠ A PLAN THAT RUNS OUT IS AN ENDED TURN, not a stuck one. The champion may plan no cards at all —
    // doing nothing is a candidate turn it is allowed to prefer — and that must end the turn rather than
    // spin. The guard below is the same ceiling a real turn has.
    private static (bool Alive, bool Won, int EnemyHealth, int HeroHealth) Play(
        InteractiveCombat combat, Champion champion, int turns)
    {
        for (var turn = 0; turn < turns && !combat.IsOver; turn++)
        {
            var laid = 0;
            while (combat.IsHeroTurn && !combat.IsOver && laid < RunBot.PlaysInATurnNobodyMakes)
            {
                var plan = champion.PlanTurn(combat);
                if (plan.Plays.Count == 0)
                    break;

                var played = false;
                foreach (var step in plan.Plays)
                {
                    if (combat.Hand.FirstOrDefault(c => c.Id == step.Card) is null)
                        break;
                    var steps = combat.Steps.Count;
                    combat.PlayCard(step.Card, step.Target);
                    laid++;
                    if (RunBot.Refused(combat, steps))
                        break;
                    played = true;
                    if (combat.IsOver || !combat.IsHeroTurn)
                        break;
                }
                // A plan none of whose cards could be laid down is not a plan; ending the turn is the only
                // honest thing left, and looping on it would be the hang this guard exists to prevent.
                if (!played)
                    break;
            }

            if (combat.IsOver)
                break;
            combat.EndTurn();
        }

        return (combat.Result != CombatResult.Defeat, combat.Result == CombatResult.Victory,
            Champion.Standing(combat),
            // ⚠ A DEAD HERO KEEPS NOTHING, and the frontier says the same about the lines that die, so the
            // two are comparable. Reading the corpse's health here would put it above lines that lived.
            combat.Result == CombatResult.Defeat ? 0 : combat.HeroHealth);
    }
}
