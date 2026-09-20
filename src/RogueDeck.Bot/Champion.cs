using RogueDeck.Core.Combat;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Bot;

// ── THE CHAMPION ─────────────────────────────────────────────────────────────────────────────────────────
// Every other way this project decides a play asks what a card is WORTH — a number read off the card, the
// same whoever is holding it and whatever is standing opposite. The champion does not ask. It FORKS the
// fight, plays the cards on the copy, lets the enemies answer, and looks at what is left.
//
// ⚠⚠ IT PLANS THE WHOLE TURN, NOT THE NEXT CARD. The first version picked the best single card, played it,
// and asked again — which is greedy, and a greedy player can never find "A alone is worse than doing
// nothing, but A then B wins". So the search is over SEQUENCES: at every point it may stop (end the turn and
// be scored) or play one more card, and what is compared is whole turns.
//
// What makes that affordable is that a fight is a DETERMINISTIC PUZZLE. The dice are bound to the seed and
// the step count, so a fork draws what the real fight would draw; the enemy's intent is a function of the
// state; and CombatStateHasher gives every position a fingerprint. Orders that arrive at the same position
// are therefore the same position, and are searched once. That single fact is what collapses a combinatorial
// fan-out into a few hundred forks.
//
// ⚠ IT LIVES ALONE IN THIS FILE SO THAT IT CAN BE MEASURED ALONE (C0). While it sat inside BotMind the only
// way to ask whether it played well was to play whole runs and count rooms — dozens of runs per answer, with
// the variance of five acts on top. Out here it can be handed a single position and asked what it does with
// it, which is what lets `ChampionExam` hold it against a proof.
//
// ⚠ THE TWO CEILINGS ARE NAMED, because an unbounded search is not a plan but a hang: a turn is planned to
// at most PlansAhead cards deep and ForksPerTurn positions wide. Both are hit only by hands that loop (a card
// that puts a copy of itself back), and hitting them costs the turn its optimality, not its correctness.
//
// ⚠⚠ NO DICE ARE THROWN HERE. The search is deterministic, so the champion draws nothing from the run's
// Random — which is what lets the two seats still walk the same run.
public sealed class Champion(RunPlayback play, BotPolicy? policy)
{
    public const int PlansAhead = 6;
    public const int ForksPerTurn = 500;

    // One card of a planned turn, plus the position the fight is expected to be in before it is played. The
    // expectation is what lets a plan be torn up the moment the real fight disagrees with it.
    public readonly record struct PlannedPlay(CardInstanceId Card, CombatantId? Target, string Expected);

    public sealed record Plan(IReadOnlyList<PlannedPlay> Plays, double Worth, int Forks);

    // ── HOW MANY TURNS AHEAD (C2) ────────────────────────────────────────────────────────────────────────
    // ⚠⚠ THE EXAM SAID THIS WAS THE PROBLEM, AND SAID IT WITH NUMBERS. Graded against the Pareto frontier of
    // what was reachable, the champion is beaten at 10 % of positions over ONE turn and at 70 % over THREE
    // — and what it loses is health, almost never damage (`lostDmg` ≈ 0, 3 to 11 health a position). It
    // chooses this turn about as well as anything could and then walks into the next one.
    //
    // So a turn is no longer scored where it ends. Above a horizon of 1 the search plays the turn, lets the
    // enemies answer, and goes again — keeping the best `Beam` positions at each boundary rather than every
    // one of them, because keeping every one is the fan-out this whole file exists to avoid.
    //
    // ⚠⚠ AND THE EVALUATOR'S THUMB ON THE SCALE HAS TO BE UNDERSTOOD BEFORE THE DEPTH IS ADDED, or the two
    // fight each other. `Worth` deliberately refuses to let a turn that takes nothing off them beat one that
    // does — a bias put there BECAUSE a one-turn horizon "cannot see the enemy's damage ramping while it
    // waits, so it is not allowed to wait". A deeper search CAN see the ramp. The bias is kept anyway and
    // kept honest by measuring both ends over the WHOLE window: `Worth` is applied at the leaf against the
    // health and the enemies as they stood at the ROOT, so "took nothing off them" now means "took nothing
    // off them in N turns", which is a lost fight by any reading rather than a lost tempo.
    //
    // ⚠ DEPTH BUYS SIGHT OF CARDS THE PLAYER HAS NOT DRAWN. A fork draws what the real fight would draw, so
    // a three-turn plan is built around a hand nobody has seen yet. At a horizon of 1 that is harmless — you
    // know your own hand — and past it this player is no longer a fair one. That is the switch the arc still
    // has to decide (C4); until it is decided, a deep champion's results are an upper bound and must be read
    // as one.
    //
    // 0 or 1 ⇒ exactly the search this file has always done, which is what keeps the golden set meaningful.
    public int Horizon => Math.Max(1, (int)Math.Round(policy?.Horizon ?? 0));

    public int Beam => Math.Max(1, (int)Math.Round(policy?.Beam ?? 0));

    // Search every way this turn could be played, and keep the best whole turn.
    //
    // `refused` and `barren` are the seat's memory of plays the rules turned down and plays that moved
    // nothing — they are passed in rather than kept here because they belong to the RUN, not to one turn.
    public Plan PlanTurn(
        InteractiveCombat combat,
        IReadOnlySet<CardInstanceId>? refused = null,
        IReadOnlySet<string>? barren = null)
    {
        ArgumentNullException.ThrowIfNull(combat);
        refused ??= new HashSet<CardInstanceId>();
        barren ??= new HashSet<string>(StringComparer.Ordinal);

        return Horizon > 1
            ? PlanAhead(combat, refused, barren)
            : PlanOneTurn(combat, refused, barren);
    }

    private Plan PlanOneTurn(
        InteractiveCombat combat, IReadOnlySet<CardInstanceId> refused, IReadOnlySet<string> barren)
    {
        var heroBefore = combat.HeroHealth;
        var enemyBefore = Standing(combat);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var forks = 0;

        // Doing nothing at all, played out to the same depth as every plan: the turn ends, the enemies answer.
        var idle = combat.Fork();
        idle.EndTurn();
        var best = Worth(idle, heroBefore, enemyBefore);
        List<PlannedPlay>? plan = null;

        void Explore(InteractiveCombat node, List<PlannedPlay> path)
        {
            if (path.Count >= PlansAhead || forks >= ForksPerTurn || node.IsOver || !node.IsHeroTurn)
                return;

            var hero = node.State.GetCombatant(node.HeroId);
            var playable = node.Hand
                .Where(c => !refused.Contains(c.Id) && !barren.Contains(c.DefinitionId.value)
                    && RunBot.CanPay(play, hero, c.DefinitionId.value))
                .ToList();
            var living = node.State.Combatants
                .Where(c => c.Id != node.HeroId && c.IsAlive && c.TeamId == StandardCombatIds.EnemyTeam)
                .Select(c => (CombatantId?)c.Id)
                .ToList();

            foreach (var card in playable)
                foreach (var target in living.Count == 0 ? [null] : living)
                {
                    if (forks >= ForksPerTurn)
                        return;

                    var after = node.Fork();
                    forks++;
                    var steps = after.Steps.Count;
                    after.PlayCard(card.Id, target);
                    // A play the RULES refuse is not a candidate, and the fork is where that is found out
                    // for free — the real fight never hears about it.
                    if (RunBot.Refused(after, steps))
                        continue;

                    var position = Position(after);
                    // ⚠ THE WHOLE SEARCH RESTS ON THIS LINE. Two orders that arrive at the same table are
                    // the same turn from here on, and the second one is not worth walking.
                    if (!seen.Add(position))
                        continue;

                    var here = new List<PlannedPlay>(path) { new(card.Id, target, Position(node)) };

                    // Stopping here is a candidate turn in its own right: the energy left over may be worth
                    // less than the guard already standing.
                    var stopped = after.Fork();
                    forks++;
                    stopped.EndTurn();
                    var worth = Worth(stopped, heroBefore, enemyBefore);
                    if (worth > best)
                    {
                        best = worth;
                        plan = here;
                    }

                    Explore(after, here);
                }
        }

        Explore(combat, []);
        return new Plan(plan ?? [], best, forks);
    }

    // ── THE SAME TURN, PLAYED OUT AND THEN ANSWERED, AS MANY TIMES AS THE HORIZON SAYS ───────────────────
    // A beam, not a tree. Every whole turn reachable from a position is enumerated exactly as above, the
    // best `Beam` of them are kept, and each of those is played on to the next hero-turn. What is compared
    // at the end is the LEAF, scored against the root — so a line is judged by where N turns leave the
    // fight, not by where this one does.
    //
    // ⚠ WHAT IS RETURNED IS STILL ONE TURN. The seat asks for cards to play now, and the rest of the line
    // is a reason rather than a promise: the plan is re-made at the next turn anyway, from wherever the
    // fight actually stands. Keeping the tail would only be a way of being wrong for longer.
    private Plan PlanAhead(
        InteractiveCombat combat, IReadOnlySet<CardInstanceId> refused, IReadOnlySet<string> barren)
    {
        var heroBefore = combat.HeroHealth;
        var enemyBefore = Standing(combat);
        var forks = 0;
        var budget = ForksPerTurn * Horizon;

        // A line under consideration: where the fight now stands, and the FIRST turn that led there.
        var level = new List<(InteractiveCombat At, List<PlannedPlay> Opening)> { (combat, []) };
        var leaves = new List<(double Worth, List<PlannedPlay> Opening)>();

        for (var depth = 0; depth < Horizon; depth++)
        {
            var next = new List<(InteractiveCombat At, List<PlannedPlay> Opening, double Worth)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var (at, opening) in level)
            {
                foreach (var (after, plays) in WholeTurns(at, refused, barren, seen, ref forks, budget))
                {
                    // The opening turn is what the seat will be handed; deeper turns only justify it.
                    var first = depth == 0 ? plays : opening;
                    var worth = Worth(after, heroBefore, enemyBefore);

                    // A fight that has ended cannot be played on, so it is a leaf wherever it is found —
                    // and a won fight found at depth 1 must not be thrown away for a line that is still
                    // going at depth 3.
                    if (after.IsOver || !after.IsHeroTurn || depth == Horizon - 1)
                        leaves.Add((worth, first));
                    else
                        next.Add((after, first, worth));
                }
                if (forks >= budget)
                    break;
            }

            if (next.Count == 0)
                break;
            level = [.. Cut(next).Select(n => (n.At, n.Opening))];
        }

        if (leaves.Count == 0)
            return new Plan([], double.NegativeInfinity, forks);

        var best = leaves.MaxBy(l => l.Worth);
        return new Plan(best.Opening, best.Worth, forks);
    }

    // ── WHICH LINES SURVIVE THE TURN BOUNDARY (P1) ───────────────────────────────────────────────────────
    // ⚠⚠ THE BEAM WAS WHERE THE DEPTH DIED. This cut used to be `OrderByDescending(Worth).Take(Beam)`, and
    // `Worth` has two strictly separated bands by design: a turn that takes nothing off them scores around
    // -100, a turn that takes something off them around +100. So with Beam = 4 and a real hand holding a
    // dozen ways to deal damage, a PURE BLOCK TURN WAS NEVER IN THE BEAM — it was cut before any depth was
    // allowed to score it. The search could not find "block now, kill next turn" in a real fight, which is
    // the one thing a second turn of sight is for. ChampionHorizonTests stayed green throughout because its
    // toy hand has three candidates and the beam cuts nothing.
    //
    // The band is NOT removed: it has twice stopped this player from standing still (Core 81a382c), and it
    // is what a leaf needs. It is only kept out of the CUT — which is not the same as ranking the cut by a
    // band-free score. That was tried on paper first and does not work: with toKill pinned at the `forever`
    // ceiling whenever nothing was dealt, a blocking line still ranks below every damaging one, just by 8
    // points instead of 200. A line that is always last is cut whatever the gap is.
    //
    // So the beam has RESERVED SEATS: half of them (at least one, when there is more than one) go to the
    // lines that kept the most health, the rest to the lines with the best `Worth`. The defensive line is
    // then carried to the next turn boundary and scored by what it BUYS there — and if it buys nothing, the
    // leaf's band throws it away exactly as before. Nothing here says blocking is good; it says blocking is
    // allowed to be asked about.
    private List<(InteractiveCombat At, List<PlannedPlay> Opening, double Worth)> Cut(
        List<(InteractiveCombat At, List<PlannedPlay> Opening, double Worth)> next)
    {
        if (next.Count <= Beam)
            return next;

        var guarded = Beam > 1 ? Math.Max(1, Beam / 2) : 0;
        var taken = new HashSet<int>();
        var chosen = new List<(InteractiveCombat, List<PlannedPlay>, double)>();

        // ⚠ Indices, not the tuples themselves: two lines can stand at the same table with the same worth,
        // and dropping one of them as a duplicate would quietly narrow the beam.
        void Seat(IEnumerable<int> order, int seats)
        {
            foreach (var i in order)
            {
                if (chosen.Count >= seats)
                    return;
                if (taken.Add(i))
                    chosen.Add(next[i]);
            }
        }

        var byHealth = Enumerable.Range(0, next.Count)
            .OrderByDescending(i => next[i].At.HeroHealth).ThenByDescending(i => next[i].Worth);
        var byWorth = Enumerable.Range(0, next.Count).OrderByDescending(i => next[i].Worth);

        Seat(byHealth, guarded);
        Seat(byWorth, Beam);
        return chosen;
    }

    // Every whole turn playable from here, each handed back as the position the enemies have already
    // answered — the same enumeration PlanOneTurn walks, kept instead of collapsed to its best.
    private IEnumerable<(InteractiveCombat After, List<PlannedPlay> Plays)> WholeTurns(
        InteractiveCombat from, IReadOnlySet<CardInstanceId> refused, IReadOnlySet<string> barren,
        HashSet<string> seen, ref int forks, int budget)
    {
        var found = new List<(InteractiveCombat, List<PlannedPlay>)>();
        var spent = forks;

        void Walk(InteractiveCombat node, List<PlannedPlay> path)
        {
            if (path.Count >= PlansAhead || spent >= budget || node.IsOver || !node.IsHeroTurn)
                return;

            var hero = node.State.GetCombatant(node.HeroId);
            var playable = node.Hand
                .Where(c => !refused.Contains(c.Id) && !barren.Contains(c.DefinitionId.value)
                    && RunBot.CanPay(play, hero, c.DefinitionId.value))
                .ToList();
            var living = node.State.Combatants
                .Where(c => c.Id != node.HeroId && c.IsAlive && c.TeamId == StandardCombatIds.EnemyTeam)
                .Select(c => (CombatantId?)c.Id)
                .ToList();

            foreach (var card in playable)
                foreach (var target in living.Count == 0 ? [null] : living)
                {
                    if (spent >= budget)
                        return;

                    var after = node.Fork();
                    spent++;
                    var steps = after.Steps.Count;
                    after.PlayCard(card.Id, target);
                    if (RunBot.Refused(after, steps))
                        continue;
                    if (!seen.Add(Position(after)))
                        continue;

                    var here = new List<PlannedPlay>(path) { new(card.Id, target, Position(node)) };

                    var stopped = after.Fork();
                    spent++;
                    stopped.EndTurn();
                    found.Add((stopped, here));

                    Walk(after, here);
                }
        }

        // Ending the turn having played nothing is a whole turn too, and the one a stall would choose.
        var idle = from.Fork();
        spent++;
        idle.EndTurn();
        found.Add((idle, []));

        Walk(from, []);
        forks = spent;
        return found;
    }

    // ── THE EVALUATOR ────────────────────────────────────────────────────────────────────────────────────
    // ⚠⚠ A FIGHT IS A RACE, AND THE FIRST VERSION OF THIS METHOD DID NOT KNOW THAT. It scored a turn by what
    // was left of each side — health kept against enemy health removed — and the champion promptly stopped
    // playing cards altogether. It was not confused: the very first enemy of this game, the Contradictory
    // Signpost, PUNISHES ACTING. Measured on the fork: ending the turn having done nothing cost 0 health;
    // playing a six-damage jab cost 15. At one ply, and on a scale where standing still is free, refusing to
    // play is correct — and it lost every fight, because the enemy's damage ramps while you wait.
    //
    // So what is scored is not what is LEFT but how the race is going, in the only currency a fight has:
    // TURNS. This turn removed so much of them and cost so much of me — at that rate, how many turns until
    // they are down, and how many until I am? A turn that removes nothing never ends the fight, and says so
    // by naming the ceiling. Both rates are MEASURED on the fork; nothing here is a model of the game.
    public double Worth(InteractiveCombat after, int heroBefore, int enemyBefore)
    {
        ArgumentNullException.ThrowIfNull(after);
        if (after.Result == CombatResult.Victory)
            return 1000;
        if (after.Result == CombatResult.Defeat)
            return -1000;

        var forever = (double)RunBot.TurnsAFightShouldNotNeed;
        var dealt = enemyBefore - Standing(after);
        var taken = heroBefore - after.HeroHealth;
        var toDie = taken <= 0 ? forever : Math.Min(forever, after.HeroHealth / (double)taken);

        // ⚠⚠ A TURN THAT TAKES NOTHING OFF THEM IS NOT A CHEAP TURN, IT IS A LOST ONE — and the first
        // version of this line said the opposite, out loud, in a real fight. Against the Contradictory
        // Signpost, which punishes acting, standing still costs no health: with both ceilings in play a
        // stall scored (1-lean)·100 - lean·100, which is POSITIVE for any runner leaning at all defensive —
        // and the bred champion leaned 0.43. It stood there for three turns at a time with a full hand, and
        // paid 52 of its 70 health for a single act-I room.
        if (dealt <= 0)
            return -forever + toDie;

        // ⚠ AND THE TWO BANDS MUST NOT OVERLAP. Standing still sits at a fixed value; a turn that makes
        // progress but kills slowly goes NEGATIVE, and at a high lean it goes below the stall — so the
        // runner went back to waiting, just for the opposite reason. Progress is therefore lifted clear of
        // the stall band entirely: any turn that takes something off them and does not kill the hero beats
        // any turn that does nothing. That is a deliberate bias, and it is the one a horizon of a single
        // turn needs — it cannot see the enemy's damage ramping while it waits, so it is not allowed to
        // wait.
        var toKill = Math.Min(forever, Standing(after) / (double)dealt);
        var lean = policy?.Aggression ?? 0.5;
        return forever + (1 - lean) * toDie - lean * toKill;
    }

    // A position's fingerprint — the same one the engine uses to prove two fights are the same fight.
    public static string Position(InteractiveCombat combat)
    {
        ArgumentNullException.ThrowIfNull(combat);
        return CombatStateHasher.ComputeHash(combat.State.CreateSnapshot());
    }

    public static int Standing(InteractiveCombat combat)
    {
        ArgumentNullException.ThrowIfNull(combat);
        return combat.State.Combatants
            .Where(c => c.Id != combat.HeroId && c.IsAlive && c.TeamId == StandardCombatIds.EnemyTeam)
            .Sum(c => c.Health.Current);
    }
}
