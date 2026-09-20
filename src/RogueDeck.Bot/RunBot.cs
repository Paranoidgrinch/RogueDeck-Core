using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Bot;

// ── THE RUNNER, IN THE SEAT THE UI SITS IN ───────────────────────────────────────────────────────────────
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
//
// ⚠⚠ WHAT IS DECIDED HERE IS NOTHING. Every decision and every counter lives in BotMind; this file is the
// REPLAY SEAT — the loop that polls a parked InteractiveRunSession and hands the mind's answers back through
// the very methods a mouse click calls. `PlayDirect` is the other seat, and the two must agree; see BotSeat.
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

        var session = play.Session;
        var mind = new BotMind(play, options, log);
        if (session is not null)
            mind.Opening(session.Run);

        try
        {
            for (var step = 0; step < options.Budget && session is not null && !session.IsComplete; step++)
            {
                mind.Step = step;
                mind.Narrate(session.Run);

                if (session.Error is not null || play.Error is not null)
                {
                    mind.Reason = "an error was raised";
                    break;
                }
                if (step % 20 == 19 && breathe is not null)
                    await breathe().ConfigureAwait(true);

                mind.Observe(session.Run, play.CombatDriver?.Current);

                if (play.CombatDriver is { Current: not null } driver)
                {
                    mind.FightStarts(session.Run, driver.Current);

                    if (driver.PendingOptionChoice is { } offered)
                        driver.SupplyOptionChoice(
                            [.. mind.OptionPicks(offered, driver.PendingOptionChoiceCount)]);
                    else if (driver.PendingCardChoice is { } cards)
                        driver.SupplyCardChoice(
                            [.. mind.CardChoicePicks(cards, driver.PendingCardChoiceCount).Select(i => cards[i].Id)]);
                    else if (driver.Current!.IsHeroTurn)
                    {
                        var combat = driver.Current;
                        if (mind.ChoosePlay(combat) is { } chosen)
                        {
                            driver.PlayCard(chosen.Card.Id, chosen.Target);
                            mind.AfterPlay(session.Run, driver.Current, chosen);
                        }
                        else
                        {
                            mind.EndingTurn(combat);
                            driver.EndTurn();
                            mind.AfterEndTurn(session.Run);
                        }
                    }
                    else
                        mind.EnemyTurnWall(session.Run);
                }
                else if (session.IsAwaitingNodeChoice)
                    session.PickNode(mind.Fork(session.PendingNodeChoices).Id.Value);
                else if (session.IsAwaitingEntities && session.PendingEntities is { } entities)
                    session.PickEntities(
                        [.. mind.EntityPicks(
                            entities.Displays,
                            [.. Enumerable.Range(0, entities.Displays.Count).Select(entities.ArtAt)],
                            entities.Count, entities.AllowSkip, entities.Purpose, entities.Intent)]);
                else if (session.IsAwaitingChoice && session.PendingSituation is { } situation)
                    session.Pick(mind.Choose(situation, session.PendingChoices).Id);
                else if (session.IsAwaitingInterlude)
                    session.Continue();
                else
                {
                    mind.Reason = $"nothing at {Where(session.Run)} was awaiting an answer";
                    break;
                }

                if (mind.Stopped)
                    break;
                if (step == options.Budget - 1)
                    mind.Reason = $"the step budget ran out at {Where(session.Run)}";
            }
        }
        catch (BotStopException)
        {
            // A GUARD THAT UNWINDS, in the seat that has a loop to break out of. Most of the runner's
            // ceilings set `Stopped` and let the loop below notice after the answer is given; one that must
            // stop the walk BEFORE its next answer (--stop-after-act) cannot, because by then the answer has
            // moved the run and the two seats would end in different states. It has already written down
            // why, and an unwound walk is INCOMPLETE — exactly as it is under the direct seat.
        }
        catch (Exception ex)
        {
            mind.Crashed(session?.Run, ex);
        }

        return mind.Finish(session?.Run, session?.Error ?? play.Error, session?.IsComplete ?? false);
    }

    // ── THE OTHER SEAT ───────────────────────────────────────────────────────────────────────────────────
    // The same mind, walking the run ONCE. No park, no replay, no re-execution: BotSeat is the collaborator
    // RunRunner asks, and it answers where it stands. The replay model exists so a single-threaded UI can
    // park at a prompt; a bot never parks, so it need not pay for the ability to.
    //
    // ⚠ IT IS NOT A DIFFERENT RUNNER. Both seats build their run through the same RunPlayback, out of the
    // same blueprint, content and registry, and both hand every decision to the same BotMind. The only
    // difference is who calls whom — which is exactly what `golden.sh --console --direct` exists to prove.
    public static BotResult PlayDirect(
        RunPlayback play, RunBlueprint blueprint, string? characterId, string? mapGenerator,
        BotOptions options, IBotLog log)
    {
        ArgumentNullException.ThrowIfNull(play);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(log);

        var seat = new BotSeat(play, options, log);
        string? error = null;
        var complete = false;
        try
        {
            play.StartDirect(blueprint, options.Seed, characterId, mapGenerator, seat, seat.BeforeTheFirstQuestion);
            complete = true;
        }
        catch (BotStopException)
        {
            // The runner called the walk off and has already written down why. A run stopped by a guard is
            // INCOMPLETE, exactly as it is under the replay seat, where the guard broke the loop and left
            // the session standing — and an incomplete run is never clean.
        }
        catch (Exception ex)
        {
            // The same words the replay session puts on an escaped exception, and the same escape hatch: a
            // walk under ROGUEDECK_TRACE prints the whole thing to stderr on its way into the string. The
            // run counts as COMPLETE because it is over and will answer nothing further — which is what the
            // session says too — and `error=` is what makes it unclean.
            if (Environment.GetEnvironmentVariable("ROGUEDECK_TRACE") is not null)
                Console.Error.WriteLine(ex);
            error = $"{ex.GetType().Name}: {ex.Message}";
            complete = true;
        }
        return seat.Finish(error, complete);
    }

    // Where the run stands, in the two names that identify a room: its map id and what is being fought there.
    public static string Where(InteractiveRunSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Where(session.Run);
    }

    public static string Where(RunState run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var here = run.CurrentNodeId?.Value ?? "nowhere";
        return $"act {run.ActNumber} {here} ({Content(run)})";
    }

    // What is AUTHORED in the room the run is standing in — the encounter, the door, the shop, by the id
    // the content gives it. ⚠ The map coordinate (`r12c0`) is not this: it is a different room in the next
    // seed, so a tally kept under it cannot be added up across a sweep. The content id can.
    public static string Content(RunState run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var here = run.CurrentNodeId?.Value ?? "nowhere";
        var node = run.Map.Nodes.FirstOrDefault(n => n.Id.Value == here);
        return node?.Payload switch
        {
            EncounterRef fight => fight.Id.Value,
            EventRef door => door.Id.Value,
            ShopRef shop => shop.Id.Value,
            { } payload => payload.GetType().Name,
            _ => "—",
        };
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
    // reason; nothing new at all means the driver dropped it.
    //
    // ⚠⚠ A CARD THAT ASKS SOMETHING HAS NOT BEEN REFUSED. Under the replay seat a play that opens a prompt
    // PARKS, and the park is written into the step as a problem — so this read it as a refusal and struck the
    // card off the turn, even though the very next answer resolved the play. Under the direct seat nothing
    // parks, the card simply resolves, and it stays offered. That single disagreement was enough to make the
    // two seats play different games from the same seed: a card that comes back to hand was played again by
    // one of them and not by the other. A park is the engine working; only what the RULES refuse counts here.
    public static bool Refused(InteractiveCombat? combat, int stepsBefore)
    {
        if (combat is null)
            return false;
        var steps = combat.Steps;
        if (steps.Count <= stepsBefore)
            return true;
        return steps.Skip(stepsBefore).Any(step => step.HasProblems
            && !string.Join(" | ", step.Problems).Contains("ReplayParked", StringComparison.Ordinal));
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
}
