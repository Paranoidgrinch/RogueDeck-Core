using System.Diagnostics;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Bot;

// ── THE RUNNER'S BRAIN, WITH NO HOST IN IT ───────────────────────────────────────────────────────────────
// A player made of dice (or of a bred policy). It walks the REAL run — every answer goes through the same
// session and drivers the mouse drives — but it answers by itself: a fork, a door, a card at an enemy, a pick
// from every offer. It does not play WELL; it plays BROADLY and fast, so a batch of runs touches content a
// careful player would never reach in a hundred sittings.
//
// ⚠⚠ THIS USED TO EXIST THREE TIMES (S8): once in Godot's `--sim`, once in Godot's `--smoke-marathon`, once
// in bnb-content's playtest walker. Three copies of one answer loop, with three sets of guards that had
// already drifted apart — one of them learnt Act IV's measure rule months after the others. One behaviour,
// one log format, one set of guards, and every host is a caller.
//
// ⚠ IT TAKES A RunPlayback RATHER THAN MAKING ONE. That is what lets a console runner play N runs in one
// process while each run keeps its own playback, its own RNG and its own try/catch — a crash must cost one
// run, not the batch — and it is also what lets Godot hand over the playback its screens are already
// watching.
public static class RunBot
{
    // A turn that plays this many cards is not a turn, and a fight that needs this many turns is not a fight.
    // Both were learnt from walks that never ended: a card may put a fresh copy of itself in your hand.
    public const int PlaysInATurnNobodyMakes = 50;
    public const int TurnsAFightShouldNotNeed = 100;

    // `breathe` is called every twenty answers. In Godot it yields a frame — the screen rebuilds into fresh
    // nodes per answer and frees the old ones deferred, so a walk that never yields fills the message queue
    // and takes the process down in the second act. Out of Godot there is nothing to yield to and it is null.
    public static async Task<BotResult> Play(
        RunPlayback play, BotOptions options, IBotLog log, Func<Task>? breathe = null)
    {
        ArgumentNullException.ThrowIfNull(play);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(log);

        var rng = new Random(options.Seed);
        var clock = Stopwatch.StartNew();
        var session = play.Session;
        var policy = options.Policy;

        var rooms = new List<string>();
        var acts = 1;
        string? lastRoom = null;
        var loggedNarration = 0;
        var problems = 0;
        var fights = 0;
        var reason = "the run finished";
        var crash = "";
        var hpAtActBoss = new Dictionary<int, int>();
        var damageAtActBoss = new Dictionary<int, int>();
        var hpBeforeRoom = 0;
        var damageTaken = 0;
        var healed = 0;
        var hpLastSeen = session?.Run.Health.Current ?? 0;

        log.Line($"sim: policy={policy?.Name ?? "random"} seed={options.Seed} maps={options.Maps} "
            + $"character={options.Character ?? "—"} "
            + $"hp={session?.Run.Health.Current}/{session?.Run.Health.Max} deck={session?.Run.Deck.Count} "
            + $"relics={string.Join(",", session?.Run.Relics.Select(r => r.Id.Value) ?? [])}");

        // The guards. Never re-offer a play the engine refused; never repeat a play that moved nothing on the
        // table (a card may put a copy of itself back in hand for ever); give both the turn and the fight a
        // ceiling.
        var inFight = false;
        var turn = 0;
        var playsThisTurn = 0;
        var refused = new HashSet<CardInstanceId>();
        var barren = new HashSet<string>(StringComparer.Ordinal);
        string? lastPlayed = null;
        var tableBeforeThePlay = "";
        void NewTurn()
        {
            playsThisTurn = 0;
            lastPlayed = null;
            refused.Clear();
            barren.Clear();
        }

        try
        {
            for (var step = 0; step < options.Budget && session is not null && !session.IsComplete; step++)
            {
                // Fold in the game's own narration since the last answer: what the engine SAID happened.
                var narration = session.Run.Log;
                for (; loggedNarration < narration.Count; loggedNarration++)
                    log.Line($"    | {narration[loggedNarration].Message}");

                if (session.Error is not null || play.Error is not null)
                {
                    reason = "an error was raised";
                    break;
                }
                if (step % 20 == 19 && breathe is not null)
                    await breathe().ConfigureAwait(true);

                var hpNow = session.Run.Health.Current;
                if (hpNow < hpLastSeen)
                    damageTaken += hpLastSeen - hpNow;
                else if (hpNow > hpLastSeen)
                    healed += hpNow - hpLastSeen;
                hpLastSeen = hpNow;

                if (session.Run.CurrentNodeId?.Value is { } here && here != lastRoom)
                {
                    lastRoom = here;
                    var node = session.Run.Map.Nodes.FirstOrDefault(n => n.Id.Value == here);
                    var role = node is null ? "?" : MapRole.Of(node);
                    rooms.Add($"{session.Run.ActNumber}:{role}");
                    acts = Math.Max(acts, session.Run.ActNumber);
                    var spent = hpBeforeRoom == 0 ? 0 : hpBeforeRoom - session.Run.Health.Current;
                    hpBeforeRoom = session.Run.Health.Current;
                    if (node is not null && node.HasTag(MapNodeTags.Boss))
                    {
                        hpAtActBoss[session.Run.ActNumber] = session.Run.Health.Current;
                        damageAtActBoss[session.Run.ActNumber] = damageTaken;
                    }
                    log.Line($"[{clock.Elapsed.TotalSeconds,6:0.0}s {step,5}] ROOM {Where(session)} {role} "
                        + $"cost={spent} hp={session.Run.Health.Current}/{session.Run.Health.Max} "
                        + $"gold={session.Run.GetResource(StandardRunIds.Gold)} "
                        + $"deck={session.Run.Deck.Count} relics={session.Run.Relics.Count}");
                }

                if (play.CombatDriver?.Current is null && inFight)
                {
                    inFight = false;
                    log.Line($"  fight ends: hp={session.Run.Health.Current}/{session.Run.Health.Max} "
                        + $"after {turn} turns");
                    turn = 0;
                    NewTurn();
                }

                if (play.CombatDriver is { Current: not null } driver)
                {
                    if (!inFight)
                    {
                        inFight = true;
                        fights++;
                        var enemies = driver.Current!.State.Combatants
                            .Where(c => c.Id != driver.Current.HeroId)
                            .Select(c => $"{c.Id.value}({c.Health.Current})");
                        log.Line($"  FIGHT {Where(session)} vs {string.Join(" ", enemies)}");
                    }

                    if (driver.PendingOptionChoice is { } options2)
                    {
                        var picks = Pick(rng, options2.Count, driver.PendingOptionChoiceCount);
                        log.Line($"    option {string.Join(",", picks)} of {options2.Count}");
                        driver.SupplyOptionChoice(picks);
                    }
                    else if (driver.PendingCardChoice is { } cards)
                    {
                        var picks = Pick(rng, cards.Count, driver.PendingCardChoiceCount);
                        log.Line($"    card-choice {string.Join(",", picks.Select(i => cards[i].DefinitionId.value))}");
                        driver.SupplyCardChoice([.. picks.Select(i => cards[i].Id)]);
                    }
                    else if (driver.Current!.IsHeroTurn)
                    {
                        var combat = driver.Current;
                        if (lastPlayed is { } finished)
                        {
                            if (TableState(combat) == tableBeforeThePlay)
                                barren.Add(finished);
                            lastPlayed = null;
                        }

                        var hero = combat.State.GetCombatant(combat.HeroId);
                        var playable = combat.Hand
                            .Where(c => !refused.Contains(c.Id) && !barren.Contains(c.DefinitionId.value)
                                && CanPay(play, hero, c.DefinitionId.value))
                            .ToList();
                        var living = combat.State.Combatants
                            .Where(c => c.Id != combat.HeroId && c.IsAlive && c.TeamId == StandardCombatIds.EnemyTeam)
                            .ToList();
                        // A random player ends the turn early sometimes — the same hand played to the last
                        // point every time never shows what a held card does on the enemy's turn.
                        CardInstance? card;
                        if (policy is null)
                            card = playable.Count > 0 && rng.NextDouble() > 0.12
                                ? playable[rng.Next(playable.Count)]
                                : null;
                        else
                        {
                            // The best card in hand, and a turn that ends when the best is not worth it.
                            var best = playable
                                .Select(c => (card: c, score: Score(play, options, policy, c.DefinitionId.value)))
                                .OrderByDescending(x => x.score)
                                .FirstOrDefault();
                            card = best.card is not null && best.score >= policy.EndTurnBelow ? best.card : null;
                        }
                        if (card is not null)
                        {
                            var target = living.Count == 0 ? (CombatantId?)null
                                : policy is null ? living[rng.Next(living.Count)].Id
                                : rng.NextDouble() < policy.TargetLowestHp
                                    ? living.OrderBy(e => e.Health.Current).First().Id
                                    : living.OrderByDescending(e => e.Health.Current).First().Id;
                            var stepsBefore = combat.Steps.Count;
                            tableBeforeThePlay = TableState(combat);
                            lastPlayed = card.DefinitionId.value;
                            log.Line($"    play {card.DefinitionId.value} -> {target?.value ?? "—"} "
                                + $"(hp {hero.Health.Current}, hand {combat.Hand.Count})");
                            driver.PlayCard(card.Id, target);
                            foreach (var bad in (driver.Current?.Steps ?? []).Skip(stepsBefore)
                                .Where(s => s.HasProblems))
                            {
                                // Two of these are the engine working, not failing: a card the rules REFUSE
                                // (a random player will try a curse) and a card that PARKS to ask its own
                                // question (the replay model reports the park as a throw, and the prompt the
                                // bot answers next arrives right behind it). Everything else is a finding.
                                var text = string.Join(" | ", bad.Problems);
                                var expected = text.Contains("was not played", StringComparison.Ordinal)
                                    || text.Contains("ReplayParked", StringComparison.Ordinal);
                                if (expected)
                                {
                                    log.Line($"    (refused/asked: {card.DefinitionId.value})");
                                    continue;
                                }
                                problems++;
                                log.Line($"    !! PROBLEM playing {card.DefinitionId.value} at {Where(session)}: "
                                    + text);
                            }
                            if (Refused(driver.Current, stepsBefore))
                                refused.Add(card.Id);
                            if (++playsThisTurn >= PlaysInATurnNobodyMakes)
                            {
                                reason = $"a turn at {Where(session)} played {playsThisTurn} cards without "
                                    + $"ending — last '{card.DefinitionId.value}'";
                                break;
                            }
                        }
                        else
                        {
                            log.Line($"    end turn {turn + 1} (hp {hero.Health.Current}, hand {combat.Hand.Count})");
                            driver.EndTurn();
                            NewTurn();
                            if (++turn >= TurnsAFightShouldNotNeed)
                            {
                                reason = $"the fight at {Where(session)} did not end in {turn} turns";
                                break;
                            }
                        }
                    }
                    else
                    {
                        reason = $"the fight at {Where(session)} parked on the enemy's turn";
                        break;
                    }
                }
                else if (session.IsAwaitingNodeChoice)
                {
                    var forks = session.PendingNodeChoices;
                    var pick = policy is null
                        ? forks[rng.Next(forks.Count)]
                        : forks.OrderByDescending(n => PathWeight(policy, n)).First();
                    log.Line($"  fork -> {pick.Id.Value} {MapRole.Of(pick)} "
                        + $"(of {string.Join(" ", forks.Select(MapRole.Of))})");
                    session.PickNode(pick.Id.Value);
                }
                else if (session.IsAwaitingEntities && session.PendingEntities is { } entities)
                {
                    // A skippable offer is skipped now and then, on purpose: a deck that takes every card
                    // and a deck that refuses one are different games.
                    var take = entities.AllowSkip && (policy is null ? rng.NextDouble() < 0.2 : policy.RewardSkip > 0.5)
                        ? []
                        : Pick(rng, entities.Displays.Count, entities.Count);
                    log.Line($"  pick [{entities.Purpose}] -> "
                        + (take.Count == 0 ? "skipped" : string.Join(", ", take.Select(i => entities.Displays[i])))
                        + $" (of {entities.Displays.Count})");
                    session.PickEntities(take);
                }
                else if (session.IsAwaitingChoice && session.PendingSituation is { } situation)
                {
                    var choices = session.PendingChoices;
                    var choice = choices[PickChoice(policy, rng, choices)];
                    log.Line($"  choice [{situation.Id}] -> {choice.Id} "
                        + $"(of {string.Join(" ", choices.Select(c => c.Id))})");
                    session.Pick(choice.Id);
                }
                else if (session.IsAwaitingInterlude)
                    session.Continue();
                else
                {
                    reason = $"nothing at {Where(session)} was awaiting an answer";
                    break;
                }

                if (step == options.Budget - 1)
                    reason = $"the step budget ran out at {Where(session)}";
            }
        }
        catch (Exception ex)
        {
            crash = ex.ToString();
            reason = $"an exception escaped at {(session is null ? "—" : Where(session))}";
            log.Line($"!! CRASH {crash}");
        }

        return new BotResult
        {
            Seed = options.Seed,
            Maps = options.Maps,
            Policy = policy?.Name ?? "random",
            Result = session?.Run.Result.ToString() ?? "",
            Acts = acts,
            Fights = fights,
            Health = session?.Run.Health.Current ?? 0,
            MaxHealth = session?.Run.Health.Max ?? 0,
            Problems = problems,
            Error = session?.Error ?? play.Error ?? "none",
            Seconds = clock.Elapsed.TotalSeconds,
            Reason = reason,
            Crash = crash,
            Rooms = rooms,
            DamageTaken = damageTaken,
            Healed = healed,
            DamageAtActBoss = damageAtActBoss,
            HealthAtActBoss = hpAtActBoss,
            Complete = session?.IsComplete ?? false,
        };
    }

    // Where the run stands, in the two names that identify a room: its map id and what is being fought there.
    public static string Where(InteractiveRunSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var here = session.Run.CurrentNodeId?.Value ?? "nowhere";
        var node = session.Run.Map.Nodes.FirstOrDefault(n => n.Id.Value == here);
        var content = node?.Payload switch
        {
            EncounterRef fight => fight.Id.Value,
            EventRef door => door.Id.Value,
            ShopRef shop => shop.Id.Value,
            { } payload => payload.GetType().Name,
            _ => "—",
        };
        return $"act {session.Run.ActNumber} {here} ({content})";
    }

    // Everything about the table a play could visibly move. The EXHAUST PILE is deliberately not in it: a card
    // that burns itself and puts a fresh copy back in hand grows that pile on every play, which would make
    // exactly the loop this reading exists to find look busy for ever. Statuses count their STACKS as well as
    // their number, because paying a debt down usually moves the stack and not the count.
    public static string TableState(InteractiveCombat combat)
    {
        ArgumentNullException.ThrowIfNull(combat);
        var hero = combat.State.GetCombatant(combat.HeroId);
        var energy = hero.Resources.TryGetValue(StandardCombatIds.EnergyResource, out var pool) ? pool.Current : 0;
        var enemies = combat.State.Combatants.Where(c => c.Id != combat.HeroId).ToList();
        var zones = combat.State.GetCardZones(combat.HeroId);
        int Count(CardZone zone) => zones.GetCardsInZone(zone).Count;
        static int Stacks(IEnumerable<StatusInstance> statuses) => statuses.Sum(status => status.Stacks);
        return $"{energy}/{hero.Health.Current}/{hero.Statuses.Count}/{Stacks(hero.Statuses)}/"
            + $"{Count(CardZone.Hand)}/{Count(CardZone.DiscardPile)}/{Count(CardZone.DrawPile)}/"
            + $"{enemies.Sum(e => e.Health.Current)}/{enemies.Sum(e => e.Statuses.Count)}/"
            + $"{enemies.Sum(e => Stacks(e.Statuses))}";
    }

    // Did the play go through? The fight records every attempt as a step, and a refused one carries the
    // reason; nothing new at all means the driver dropped it (a prompt opened, say).
    public static bool Refused(InteractiveCombat? combat, int stepsBefore)
    {
        if (combat is null)
            return false;
        var steps = combat.Steps;
        return steps.Count <= stepsBefore || steps.Skip(stepsBefore).Any(step => step.HasProblems);
    }

    public static IReadOnlyList<ResourceCost> FullCosts(RunPlayback play, string definitionId)
    {
        ArgumentNullException.ThrowIfNull(play);
        if (play.CardFullCosts.TryGetValue(definitionId, out var costs))
            return costs;
        return play.ComposedCostsFor(definitionId)
            ?? [new ResourceCost(StandardCombatIds.EnergyResource, play.CardCosts.GetValueOrDefault(definitionId))];
    }

    public static bool CanPay(RunPlayback play, CombatantState payer, string definitionId)
    {
        ArgumentNullException.ThrowIfNull(payer);
        return FullCosts(play, definitionId).All(cost =>
            payer.Resources.TryGetValue(cost.ResourceId, out var pool) && pool.Current >= cost.Amount);
    }

    // `count` distinct indices out of `available`, in random order (never more than there are).
    public static List<int> Pick(Random rng, int available, int count)
    {
        ArgumentNullException.ThrowIfNull(rng);
        var pool = Enumerable.Range(0, available).ToList();
        var picked = new List<int>();
        for (var i = 0; i < count && pool.Count > 0; i++)
        {
            var at = rng.Next(pool.Count);
            picked.Add(pool[at]);
            pool.RemoveAt(at);
        }
        return picked;
    }

    // Which door a runner takes. A shop is answered as a shop — how eagerly it spends is a weight of its
    // own — and every other situation by one knob: the first option, the last, or somewhere in between.
    private static int PickChoice(BotPolicy? policy, Random rng, IReadOnlyList<EventChoice> choices)
    {
        if (policy is null)
            return rng.Next(choices.Count);
        var buys = Enumerable.Range(0, choices.Count)
            .Where(i => choices[i].Id.StartsWith("buy-", StringComparison.Ordinal)).ToList();
        var leave = choices.ToList().FindIndex(c => c.Id == "leave");
        if (leave >= 0)
            return buys.Count > 0 && rng.NextDouble() < policy.ShopBuy ? buys[rng.Next(buys.Count)] : leave;
        return Math.Clamp((int)Math.Round(policy.EventLate * (choices.Count - 1)), 0, choices.Count - 1);
    }

    private static double Score(RunPlayback play, BotOptions options, BotPolicy policy, string cardId)
    {
        var f = options.Features?.For(cardId) ?? new double[CardFeatures.Count];
        var cost = FullCosts(play, cardId).Sum(c => c.Amount);
        return policy.WDamage * f[0] + policy.WBlock * f[1] + policy.WStatus * f[2]
            + policy.WDraw * f[3] + policy.WResource * f[4] + policy.WCost * cost;
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
