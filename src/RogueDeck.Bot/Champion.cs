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
