using System.Globalization;
using System.Text;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Bot;

// ── A RUN PLAYED BY HAND, ONE COMMAND LINE AT A TIME ─────────────────────────────────────────────────────────
// For a player who is not a mouse: a person at a terminal, or Claude. A run is its seed and the answers given so
// far — the same thing a recording is — so every call rebuilds it from the seed, replays the saved answers, applies
// the new ones and prints where the run now stands. Nothing is kept in memory between calls, and nothing needs to
// be: the engine is deterministic.
//
// ⚠ IT SHOWS WHAT THE SCREEN SHOWS, NOT WHAT THE ENGINE KNOWS. A door is its role (a mimic says "treasure"), an
// enemy is its intent, the draw pile is a count. A player who reads the encounter id off the map is not playing the
// game this is here to judge.
//
// The answers:
//   p <card> [enemy]   play the card at that hand position (at that enemy)      e        end the turn
//   o <i,j>            a card asked a question: pick those options            c <i,j>  pick those cards
//   n <i>              walk through that door                                  k [i,j]  take those offers (none = skip)
//   x <i>              take that choice in an event, a shop or a rest site
public static class TextPlay
{
    public sealed record Outcome(string Text, IReadOnlyList<string> Accepted, string? Refused);

    public static Outcome Play(
        RunBlueprint blueprint, int seed, string? character, string generator,
        IReadOnlyList<string> saved, IReadOnlyList<string> fresh)
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(fresh);

        using var play = new RunPlayback(() => { }, new InMemoryMetaStore());
        play.Start(blueprint, seed, interactive: true, character, generator);
        Settle(play);

        foreach (var (answer, index) in saved.Select((a, i) => (a, i)))
            if (Apply(play, blueprint, answer) is { } broken)
                return new Outcome(
                    $"the saved run does not replay: answer {index + 1} '{answer}' — {broken}", saved, broken);

        var accepted = saved.ToList();
        var logFrom = play.Session?.Run.Log.Count ?? 0;
        var before = Table(play);
        string? refused = null;
        foreach (var answer in fresh)
        {
            if (Apply(play, blueprint, answer) is { } why)
            {
                refused = $"'{answer}' was not taken: {why}";
                break;
            }
            accepted.Add(answer);
        }

        var text = new StringBuilder();
        if (play.Session is { } session)
            foreach (var entry in session.Run.Log.Skip(logFrom))
                text.AppendLine($"  | {entry.Message}");
        var after = Table(play);
        var moved = before.Where(b => after.TryGetValue(b.Key, out var a) && a != b.Value)
            .Select(b => $"{b.Key} HP {b.Value}→{after[b.Key]}")
            .Concat(before.Keys.Where(k => !after.ContainsKey(k) && play.CombatDriver?.Current is not null)
                .Select(k => $"{k} is gone"));
        if (moved.ToList() is { Count: > 0 } changes)
            text.AppendLine($"  » {string.Join(" · ", changes)}");
        if (refused is not null)
            text.AppendLine($"!! {refused}");
        text.Append(Render(play, blueprint));
        return new Outcome(text.ToString(), accepted, refused);
    }

    // Everyone's health in the fight now standing, so a call can say what its answers did.
    private static Dictionary<string, int> Table(RunPlayback play) =>
        play.CombatDriver?.Current is { } combat
            ? combat.State.Combatants.ToDictionary(
                c => c.Id == combat.HeroId ? "you" : c.Id.value, c => c.Health.Current, StringComparer.Ordinal)
            : [];

    // Interludes are a screen's "continue" button and ask nothing.
    private static void Settle(RunPlayback play)
    {
        for (var guard = 0; guard < 100 && play.Session is { IsAwaitingInterlude: true, IsComplete: false } s; guard++)
            s.Continue();
    }

    private static string? Apply(RunPlayback play, RunBlueprint blueprint, string answer)
    {
        var parts = answer.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return "empty answer";
        if (play.Session is not { } session || session.IsComplete)
            return "the run is over";
        var verb = parts[0];
        var numbers = parts.Skip(1)
            .SelectMany(p => p.Split(',', StringSplitOptions.RemoveEmptyEntries))
            .Select(p => int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : -1)
            .ToList();
        if (numbers.Any(n => n < 0))
            return "the numbers could not be read";
        var asking = Asking(play);

        string? Wrong(string wanted) => asking == wanted ? null : $"the game is asking for {asking}, not {wanted}";

        switch (verb)
        {
            case "p":
                {
                    if (Wrong("a play") is { } wrong)
                        return wrong;
                    var driver = play.CombatDriver!;
                    var combat = driver.Current!;
                    if (numbers.Count < 1 || numbers[0] >= combat.Hand.Count)
                        return "no card at that hand position";
                    var card = combat.Hand[numbers[0]];
                    var hero = combat.State.GetCombatant(combat.HeroId);
                    if (!RunBot.CanPay(play, hero, card.DefinitionId.value))
                        return $"not enough energy for {Name(blueprint, card.DefinitionId.value)}";
                    var enemies = Living(combat);
                    CombatantId? target = numbers.Count > 1
                        ? numbers[1] < enemies.Count ? enemies[numbers[1]].Id : null
                        : enemies.Count == 1 ? enemies[0].Id : null;
                    if (numbers.Count > 1 && target is null)
                        return "no enemy at that position";
                    var before = combat.Steps.Count;
                    driver.PlayCard(card.Id, target);
                    if (RunBot.Refused(driver.Current, before))
                        return $"the rules refused {Name(blueprint, card.DefinitionId.value)}";
                    break;
                }
            case "e":
                if (Wrong("a play") is { } notNow)
                    return notNow;
                play.CombatDriver!.EndTurn();
                break;
            case "o":
                {
                    if (Wrong("an option") is { } wrong)
                        return wrong;
                    var driver = play.CombatDriver!;
                    if (numbers.Count != driver.PendingOptionChoiceCount
                        || numbers.Any(n => n >= driver.PendingOptionChoice!.Count))
                        return $"pick exactly {driver.PendingOptionChoiceCount} of the options";
                    driver.SupplyOptionChoice([.. numbers]);
                    break;
                }
            case "c":
                {
                    if (Wrong("a card") is { } wrong)
                        return wrong;
                    var driver = play.CombatDriver!;
                    var cards = driver.PendingCardChoice!;
                    if (numbers.Count != Math.Min(driver.PendingCardChoiceCount, cards.Count)
                        || numbers.Any(n => n >= cards.Count))
                        return $"pick exactly {Math.Min(driver.PendingCardChoiceCount, cards.Count)} of the cards";
                    driver.SupplyCardChoice([.. numbers.Select(n => cards[n].Id)]);
                    break;
                }
            case "n":
                if (Wrong("a door") is { } noDoor)
                    return noDoor;
                if (numbers.Count != 1 || numbers[0] >= session.PendingNodeChoices.Count)
                    return "no door at that position";
                session.PickNode(session.PendingNodeChoices[numbers[0]].Id.Value);
                break;
            case "k":
                {
                    if (Wrong("offers") is { } wrong)
                        return wrong;
                    var offers = session.PendingEntities!;
                    if (numbers.Count == 0 && !offers.AllowSkip)
                        return "this cannot be skipped";
                    if (numbers.Count > 0 && (numbers.Count != Math.Min(offers.Count, offers.Displays.Count)
                        || numbers.Any(n => n >= offers.Displays.Count) || numbers.Distinct().Count() != numbers.Count))
                        return $"take exactly {Math.Min(offers.Count, offers.Displays.Count)} of the offers";
                    session.PickEntities([.. numbers]);
                    break;
                }
            case "x":
                if (Wrong("a choice") is { } noChoice)
                    return noChoice;
                if (numbers.Count != 1 || numbers[0] >= session.PendingChoices.Count)
                    return "no choice at that position";
                session.Pick(session.PendingChoices[numbers[0]].Id);
                break;
            default:
                return $"unknown answer '{verb}'";
        }

        if ((session.Error ?? play.Error) is { } error)
            return $"the engine raised an error: {error}";
        Settle(play);
        return null;
    }

    private static string Asking(RunPlayback play)
    {
        var session = play.Session!;
        if (play.CombatDriver is { Current: not null } driver)
            return driver.PendingOptionChoice is not null ? "an option"
                : driver.PendingCardChoice is not null ? "a card"
                : driver.Current!.IsHeroTurn ? "a play"
                : "nothing (the enemy is moving)";
        if (session.IsAwaitingNodeChoice)
            return "a door";
        if (session.IsAwaitingEntities)
            return "offers";
        if (session.IsAwaitingChoice)
            return "a choice";
        return "nothing";
    }

    private static List<CombatantState> Living(InteractiveCombat combat) =>
        [.. combat.State.Combatants.Where(c => c.Id != combat.HeroId && c.IsAlive
            && c.TeamId == StandardCombatIds.EnemyTeam)];

    // ── WHAT IS ON THE SCREEN ────────────────────────────────────────────────────────────────────────────────

    private static string Render(RunPlayback play, RunBlueprint blueprint)
    {
        var text = new StringBuilder();
        if (play.Session is not { } session)
            return "there is no run\n";
        var run = session.Run;
        var here = run.CurrentNodeId?.Value;
        var node = here is null ? null : run.Map.Nodes.FirstOrDefault(n => n.Id.Value == here);
        text.AppendLine();
        text.AppendLine($"== act {run.ActNumber} · {(node is null ? "—" : MapRole.Of(node))} · HP {run.Health.Current}/"
            + $"{run.Health.Max} · gold {run.GetResource(StandardRunIds.Gold)} · deck {run.Deck.Count} · relics: "
            + (run.Relics.Count == 0 ? "none" : string.Join(", ", run.Relics.Select(r => RelicName(blueprint, r.Id.Value)))));
        if (session.IsComplete)
        {
            text.AppendLine($"THE RUN IS OVER: {run.Result}");
            return text.ToString();
        }

        if (play.CombatDriver is { Current: not null } driver)
            Fight(play, blueprint, driver, text);
        else if (session.IsAwaitingNodeChoice)
            Doors(run, session.PendingNodeChoices, text);
        else if (session.IsAwaitingEntities && session.PendingEntities is { } offers)
        {
            text.AppendLine($"OFFERS — {offers.Purpose}{(offers.Intent == RunChoiceIntent.Remove ? " (you GIVE UP what you pick)" : "")}"
                + $": take {offers.Count}{(offers.AllowSkip ? " or skip (k)" : "")}");
            for (var i = 0; i < offers.Displays.Count; i++)
            {
                var about = offers.ArtAt(i) is { Kind: EntityArt.Card } art ? CardText(blueprint, art.Id)
                    : i < offers.Descriptions.Count ? offers.Descriptions[i] : "";
                text.AppendLine($"  [{i}] {offers.Displays[i]}{(about.Length > 0 ? $" — {about}" : "")}");
            }
            text.AppendLine("> k <i[,j]>");
        }
        else if (session.IsAwaitingChoice && session.PendingSituation is { } situation)
        {
            text.AppendLine($"EVENT — {situation.TextKey}");
            for (var i = 0; i < session.PendingChoices.Count; i++)
            {
                var choice = session.PendingChoices[i];
                var price = (choice.Costs ?? []).SelectMany(c => c.Pay).OfType<ChangeResourceRunEffect>()
                    .Sum(p => Math.Max(0, -p.Delta));
                var grant = RunEntityLabeler.ArtForGrant(choice.Effects) is { Kind: EntityArt.Card } card
                    ? $" — {CardText(blueprint, card.Id)}" : "";
                text.AppendLine($"  [{i}] {choice.TextKey ?? choice.Id}{(price > 0 ? $" ({price} gold)" : "")}{grant}");
            }
            text.AppendLine("> x <i>");
        }
        else
            text.AppendLine("(nothing is being asked)");

        text.AppendLine($"deck: {string.Join(", ", run.Deck.GroupBy(c => c.DefinitionId.value).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Count() > 1 ? $"{g.Key}×{g.Count()}" : g.Key))}");
        return text.ToString();
    }

    private static void Fight(RunPlayback play, RunBlueprint blueprint, InteractiveCombatDriver driver, StringBuilder text)
    {
        var combat = driver.Current!;
        var hero = combat.State.GetCombatant(combat.HeroId);
        var zones = combat.State.GetCardZones(combat.HeroId);
        var seen = new SortedSet<string>(StringComparer.Ordinal);

        text.AppendLine($"FIGHT · round {combat.State.CurrentRound} · YOU HP {hero.Health.Current}/{hero.Health.Max}"
            + $" · energy {Energy(hero)} · block {Block(hero)}"
            + $" · statuses: {Statuses(hero, seen)}");
        text.AppendLine($"  piles: draw {zones.DrawPile.Count} · discard {zones.DiscardPile.Count} · exhaust {zones.ExhaustPile.Count}");
        var enemies = Living(combat);
        for (var i = 0; i < enemies.Count; i++)
        {
            var e = enemies[i];
            text.AppendLine($"  enemy [{i}] {e.Id.value} HP {e.Health.Current}/{e.Health.Max} · block {Block(e)} · "
                + $"statuses: {Statuses(e, seen)} · INTENT: {combat.UpcomingIntentFor(e.Id)?.Label ?? "?"}");
        }

        if (driver.PendingOptionChoice is { } options)
        {
            text.AppendLine($"A CARD ASKS: pick {driver.PendingOptionChoiceCount}");
            for (var i = 0; i < options.Count; i++)
                text.AppendLine($"  [{i}] {options[i]}");
            text.AppendLine("> o <i[,j]>");
        }
        else if (driver.PendingCardChoice is { } cards)
        {
            text.AppendLine($"A CARD ASKS: pick {Math.Min(driver.PendingCardChoiceCount, cards.Count)} card(s)");
            for (var i = 0; i < cards.Count; i++)
                text.AppendLine($"  [{i}] {CardLine(play, blueprint, cards[i].DefinitionId.value)}");
            text.AppendLine("> c <i[,j]>");
        }
        else
        {
            text.AppendLine("  hand:");
            for (var i = 0; i < combat.Hand.Count; i++)
            {
                var id = combat.Hand[i].DefinitionId.value;
                text.AppendLine($"  [{i}]{(RunBot.CanPay(play, hero, id) ? " " : "x")}{CardLine(play, blueprint, id)}");
            }
            text.AppendLine("> p <card> [enemy] · e");
        }

        foreach (var id in seen)
            if (blueprint.Statuses.FirstOrDefault(s => s.Id == id) is { DescriptionKey: { Length: > 0 } about } status)
                text.AppendLine($"  ({status.NameKey ?? id}: {about})");
    }

    private static void Doors(RunState run, IReadOnlyList<Node> doors, StringBuilder text)
    {
        text.AppendLine("DOORS");
        for (var i = 0; i < doors.Count; i++)
        {
            var next = run.Map.SuccessorIds(doors[i].Id)
                .Select(id => run.Map.Nodes.First(n => n.Id == id)).Select(MapRole.Of);
            text.AppendLine($"  [{i}] {MapRole.Of(doors[i])} → then {string.Join(" / ", next)}");
        }

        // The rest of the act as the map shows it: one row per depth, the rooms any of these doors can reach.
        var reachable = new HashSet<NodeId>();
        var frontier = new Queue<NodeId>(doors.Select(d => d.Id));
        while (frontier.Count > 0)
            if (reachable.Add(frontier.Peek()))
                foreach (var next in run.Map.SuccessorIds(frontier.Dequeue()))
                    frontier.Enqueue(next);
            else
                frontier.Dequeue();
        var depth = run.Map.Depths();
        foreach (var row in reachable.GroupBy(id => depth.GetValueOrDefault(id)).OrderBy(g => g.Key))
            text.AppendLine($"  row {row.Key,2}: {string.Join(" ", row.Select(id => run.Map.Nodes.First(n => n.Id == id)).Select(MapRole.Of).OrderBy(r => r, StringComparer.Ordinal))}");
        text.AppendLine("> n <i>");
    }

    private static string CardLine(RunPlayback play, RunBlueprint blueprint, string id)
    {
        var cost = RunBot.FullCosts(play, id).Sum(c => c.Amount);
        return $"{Name(blueprint, id)} ({cost}) — {CardText(blueprint, id)}";
    }

    private static string Name(RunBlueprint blueprint, string id) =>
        blueprint.Cards.FirstOrDefault(c => c.Id == id)?.NameKey ?? id;

    private static string CardText(RunBlueprint blueprint, string id) =>
        blueprint.Cards.FirstOrDefault(c => c.Id == id)?.DescriptionKey ?? "";

    private static string RelicName(RunBlueprint blueprint, string id) =>
        blueprint.Relics.FirstOrDefault(r => r.Id == id)?.DisplayName ?? id;

    private static string Energy(CombatantState c) =>
        c.Resources.TryGetValue(StandardCombatIds.EnergyResource, out var pool) ? $"{pool.Current}/{pool.Max}" : "—";

    private static int Block(CombatantState c) => c.DefensivePools.Values.Sum(p => p.Current);

    private static string Statuses(CombatantState c, SortedSet<string> seen)
    {
        var list = c.AllStatuses.Select(s =>
        {
            seen.Add(s.DefinitionId.value);
            return $"{s.DefinitionId.value} {s.Stacks}";
        }).ToList();
        return list.Count == 0 ? "none" : string.Join(", ", list);
    }
}
