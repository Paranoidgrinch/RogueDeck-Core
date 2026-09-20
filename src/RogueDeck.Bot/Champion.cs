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

    // ── HOW MANY DECKS IT IS ALLOWED TO BE WRONG ABOUT (C4) ──────────────────────────────────────────────
    // ⚠⚠ ABOVE A HORIZON OF 1 THE SEARCH WAS NOT A PLAYER, IT WAS A PROPHET. A fork draws what the real
    // fight would draw, so a three-turn plan was built around the cards nobody had drawn yet — every
    // result past a horizon of 1 has had to be read as an upper bound, and the whole arc has said so in
    // every table it printed.
    //
    // This is the switch that closes it. Above 1, the fight is forked SEVERAL times and the part of it the
    // player cannot see — the order of its own draw pile — is shuffled differently in each. Every world is
    // searched, and what is compared is not one world's best line but what each OPENING PLAY is worth ON
    // AVERAGE across the worlds. A card that only works if the next draw is kind wins in one world and
    // loses in the others; a card that works whatever comes up wins the mean. That is the standard reading
    // of an imperfect-information game, and it is the first time this player has been asked to make one
    // decision that has to survive more than one future.
    //
    // ⚠ WHAT IT COSTS. One decision is now `Samples` searches instead of one, and the decision is taken per
    // CARD rather than per turn (only the first play of the chosen line is kept — with the rest unknown,
    // promising four cards in a row is promising the future). The runs are several times dearer, which is
    // the trade the player asked for: a bot as strong as it can be made, and honest about it.
    //
    // ⚠ WHAT IS STILL NOT SHUFFLED: the discard pile. The player knows what is in it, and the engine's
    // reshuffle when the draw pile runs dry is bound to the seed, so a search that reaches that far still
    // sees the true order of the refill. It is a turn or two past where these searches reach, and it is
    // named here rather than papered over.
    //
    // 0 or 1 ⇒ exactly the search this file has always done. That is the default, and it is what keeps the
    // golden set meaningful.
    public int Samples => Math.Max(1, (int)Math.Round(policy?.Samples ?? 0));

    // What an opening was worth, and what it was. An opening is the part of a planned turn that every world
    // agrees exists — see Known.
    public readonly record struct Opening(IReadOnlyList<PlannedPlay> Plays, double Worth);

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

        if (Samples > 1)
            return PlanAcrossDecks(combat, refused, barren);

        return Horizon > 1
            ? PlanAhead(combat, refused, barren)
            : PlanOneTurn(combat, refused, barren);
    }

    // ── ONE DECISION, WEIGHED OVER SEVERAL DECKS (C4) ────────────────────────────────────────────────────
    // Each sample is the same fight with its unseen draw pile in a different order. The searches are the
    // ones above, unchanged; all this does is ask each of them what every OPENING was worth in its world
    // and then average.
    //
    // ⚠ THE OPENINGS ARE COMPARABLE ACROSS THE WORLDS, and that is not luck: a card's instance id belongs
    // to the card, and shuffling a pile moves the same cards into different places. The hand is identical
    // in every world, so "play this card at that enemy" names the same decision in all of them.
    private Plan PlanAcrossDecks(
        InteractiveCombat combat, IReadOnlySet<CardInstanceId> refused, IReadOnlySet<string> barren)
    {
        var totals = new Dictionary<string, (double Sum, int Seen, Opening Play)>(StringComparer.Ordinal);
        var forks = 0;

        // ⚠⚠ THE WORLDS SHARE ONE DECISION'S THINKING, they do not each get their own. Six full searches per
        // card was measured before this line existed and it is not a runner, it is a weekend: the searches
        // are per CARD rather than per turn, so the cost multiplies twice over. What a sample buys is not
        // more search, it is search that cannot see the future — so the same budget is spread over the
        // worlds, and what the sweep pays for fairness is depth inside each one rather than hours.
        var each = Math.Max(1, ForksPerTurn * Horizon / Samples);

        for (var sample = 0; sample < Samples; sample++)
        {
            var world = Shuffled(combat, sample);
            var openings = new Dictionary<string, Opening>(StringComparer.Ordinal);
            var plan = Horizon > 1
                ? PlanAhead(world, refused, barren, openings, each)
                : PlanOneTurn(world, refused, barren, openings, each);
            forks += plan.Forks;

            foreach (var (key, opening) in openings)
            {
                var (sum, seen, _) = totals.GetValueOrDefault(key);
                totals[key] = (sum + opening.Worth, seen + 1, opening);
            }
        }

        if (totals.Count == 0)
            return new Plan([], double.NegativeInfinity, forks);

        // ⚠ AVERAGED OVER THE WORLDS THAT COULD OFFER IT, not over all of them. An opening the search never
        // reached in one world (its budget ran out there) is not an opening that scored badly there, and
        // scoring it as though it had would punish the widest lines hardest.
        //
        // ⚠⚠ BUT AN AVERAGE OF ONE IS NOT AN AVERAGE, and that is the hole this whole mechanism exists to
        // close: a line that only one world ever reached would win on that world's number alone, which is
        // the prophet again wearing six hats. So the choice is made among the openings at least HALF the
        // worlds could speak about, and only if no opening clears that bar is the rest of the field asked.
        var spoken = Math.Max(1, Samples / 2);
        var field = totals.Where(x => x.Value.Seen >= spoken).ToList();
        if (field.Count == 0)
            field = [.. totals];
        var best = field
            .OrderByDescending(x => x.Value.Sum / x.Value.Seen)
            .ThenBy(x => x.Key, StringComparer.Ordinal)
            .First();

        // ⚠ THE EXPECTATIONS ARE TAKEN FROM THE REAL FIGHT, not from the world that suggested the line. The
        // seat tears a plan up the moment the fight disagrees with the position it expected, and a
        // shuffled world's positions disagree with the real one by construction — so the chosen cards are
        // laid down once on a fork of the fight as it actually stands, and it is those positions the seat
        // is given.
        return new Plan(Expect(combat, best.Value.Play.Plays), best.Value.Sum / best.Value.Seen, forks);
    }

    private static List<PlannedPlay> Expect(InteractiveCombat combat, IReadOnlyList<PlannedPlay> plays)
    {
        var walk = combat.Fork();
        var expected = new List<PlannedPlay>();
        foreach (var play in plays)
        {
            if (walk.IsOver || !walk.IsHeroTurn || walk.Hand.All(c => c.Id != play.Card))
                break;
            expected.Add(play with { Expected = Position(walk) });
            var steps = walk.Steps.Count;
            walk.PlayCard(play.Card, play.Target);
            if (RunBot.Refused(walk, steps))
            {
                expected.RemoveAt(expected.Count - 1);
                break;
            }
        }
        return expected;
    }

    // The same fight with its draw pile in a different order — the one thing about this position the player
    // is not entitled to know.
    //
    // ⚠ NO DICE ARE THROWN FOR THIS EITHER. The order comes from the position's own fingerprint and the
    // sample's number, through the engine's own shuffle, so two seats walking the same run still make the
    // same decisions — and a stable hash is used rather than string.GetHashCode, which is randomised per
    // process and would make a recorded run unreproducible in the next one.
    private static InteractiveCombat Shuffled(InteractiveCombat combat, int sample)
    {
        var fork = combat.Fork();
        var zones = fork.State.GetCardZones(fork.HeroId);
        var pile = zones.GetCardsInZone(CardZone.DrawPile);
        if (pile.Count > 1)
            zones.ReorderDrawPile(
                CombatRandom.CreateShuffledIndexes(pile.Count, Fingerprint(Position(combat)), sample));
        return fork;
    }

    private static HashSet<CardInstanceId> Held(InteractiveCombat combat) =>
        [.. combat.Hand.Select(c => c.Id)];

    private static int Fingerprint(string position)
    {
        unchecked
        {
            var hash = (int)2166136261;
            foreach (var c in position)
                hash = (hash ^ c) * 16777619;
            return hash;
        }
    }

    // ── WHAT THE WORLDS ARE ALLOWED TO COMPARE ───────────────────────────────────────────────────────────
    // ⚠⚠ AN OPENING IS KEPT ONLY AS FAR AS THE HAND THE PLAYER IS HOLDING. Every world starts from the same
    // hand, so "play this card, then that one" names the same decision in all of them; the moment a line
    // plays a card it DREW, it is talking about its own world and nobody else's. So a line is cut at its
    // first drawn card and what survives is the part that is comparable.
    //
    // ⚠ AND CUTTING THERE IS WHAT MAKES THIS AFFORDABLE. Keeping only the first play would be safer still,
    // and it was the first version: it forces a fresh decision after every single card, which measured at
    // more than three and a half times the cost of the unfair search — twenty-four times the ordinary
    // runner — for four runs that had not finished. A turn's worth of known cards is decided at once, and
    // the fight is asked again at the next bell.
    private void Keep(
        Dictionary<string, Opening>? openings, IReadOnlyList<PlannedPlay> plays,
        IReadOnlySet<CardInstanceId> hand, double worth)
    {
        if (openings is null)
            return;

        var known = new List<PlannedPlay>();
        foreach (var play in plays)
        {
            if (!hand.Contains(play.Card))
                break;
            known.Add(play);
        }

        var key = known.Count == 0
            ? "stop"
            : string.Join("|", known.Select(p => $"{p.Card}@{p.Target?.value ?? "—"}"));
        if (!openings.TryGetValue(key, out var held) || worth > held.Worth)
            openings[key] = new Opening(known, worth);
    }

    private Plan PlanOneTurn(
        InteractiveCombat combat, IReadOnlySet<CardInstanceId> refused, IReadOnlySet<string> barren,
        Dictionary<string, Opening>? openings = null, int? ceiling = null)
    {
        var allowed = ceiling ?? ForksPerTurn;
        var heroBefore = combat.HeroHealth;
        var enemyBefore = Standing(combat);
        var hand = Held(combat);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var forks = 0;

        // Doing nothing at all, played out to the same depth as every plan: the turn ends, the enemies answer.
        var idle = combat.Fork();
        idle.EndTurn();
        var best = Worth(idle, heroBefore, enemyBefore);
        List<PlannedPlay>? plan = null;
        Keep(openings, [], hand, best);

        void Explore(InteractiveCombat node, List<PlannedPlay> path)
        {
            if (path.Count >= PlansAhead || forks >= allowed || node.IsOver || !node.IsHeroTurn)
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
                    if (forks >= allowed)
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
                    Keep(openings, here, hand, worth);
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
        InteractiveCombat combat, IReadOnlySet<CardInstanceId> refused, IReadOnlySet<string> barren,
        Dictionary<string, Opening>? openings = null, int? ceiling = null)
    {
        var heroBefore = combat.HeroHealth;
        var enemyBefore = Standing(combat);
        var hand = Held(combat);
        var forks = 0;
        var budget = ceiling ?? (ForksPerTurn * Horizon);

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
                    {
                        leaves.Add((worth, first));
                        Keep(openings, first, hand, worth);
                    }
                    else
                    {
                        next.Add((after, first, worth));
                    }
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
