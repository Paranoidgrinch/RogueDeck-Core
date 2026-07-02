using System.Text.Json;
using System.Text.RegularExpressions;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Composition;

// Projects the Combat tab's authored SandboxModel into run data (cards + enemy actions + one encounter roster +
// starting deck) and merges it into an existing RunBlueprint. Extracted from the Run sandbox component so the
// projection is unit-testable and reusable: the same logic that the "⇐ Import from Combat tab" button runs is
// exercised by the end-to-end tests. Reuses ScenarioComposer so imported content behaves identically to how it
// plays in the Combat tab.
public static class CombatImport
{
    // Combat effects that are Func-backed escape nodes have no serializable data form (e.g. temporary rules);
    // such cards/actions can't live in a RunBlueprint. Project keeps only what the run JSON can round-trip and
    // reports the rest in Skipped, so a valid document is always produced instead of the whole import failing.
    public sealed record Result(
        RunBlueprint Blueprint,
        string HeroId,
        int CardCount,
        int ActionCount,
        int EnemyCount,
        int DeckCount,
        string EncounterId,
        IReadOnlyList<string> Skipped)
    {
        // The user-facing summary the sandbox shows after an import.
        public string Summary =>
            $"Imported hero '{HeroId}', {CardCount} card(s), {ActionCount} action(s); " +
            $"encounter '{EncounterId}' ({EnemyCount} enemy/-ies), deck {DeckCount} card(s). " +
            (Skipped.Count > 0 ? $"Skipped (effects the run JSON can't represent): {string.Join(", ", Skipped)}. " : "") +
            $"Add a combat node → '{EncounterId}' to the map to play it.";
    }

    // Suggests an encounter id that doesn't collide with any already in the blueprint, so repeated imports create
    // distinct fights instead of overwriting one. If `desired` is free it's returned as-is; otherwise a trailing
    // "-<number>" is dropped to find the stem (so combat-fight-2 → combat-fight) and the next free "-N" is used.
    public static string SuggestEncounterId(RunBlueprint blueprint, string desired = "combat-fight")
    {
        ArgumentNullException.ThrowIfNull(blueprint);
        var existing = blueprint.Encounters.Select(e => e.Id.Value).ToHashSet(StringComparer.Ordinal);
        if (!existing.Contains(desired))
            return desired;
        var stem = Regex.Replace(desired, "-\\d+$", "");
        for (var n = 2; ; n++)
        {
            var candidate = $"{stem}-{n}";
            if (!existing.Contains(candidate))
                return candidate;
        }
    }

    // Merges the composed combat into `current`. The whole fight (its enemy roster + hero combat resources /
    // statuses) becomes one encounter named `encounterId`; the fight's cards/actions merge by id (imported wins);
    // the hero's expanded deck becomes the run's starting deck. Custom status definitions do not carry over — the
    // run blueprint has no status library — so encounters using them should stick to the built-in statuses.
    public static Result Project(
        RunBlueprint current,
        SandboxModel model,
        JsonSerializerOptions options,
        string encounterId = "combat-fight")
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(options);

        var composed = ScenarioComposer.ComposeBlueprint(model);
        var hero = composed.Hero!; // ComposeBlueprint always sets the hero (it throws otherwise).

        bool CanSerialize<T>(T value)
        {
            try { RunJson.ToJson(value, options); return true; }
            catch (NotSupportedException) { return false; }
        }

        // Merge cards and enemy actions by id (imported definitions win over same-id existing ones).
        var cards = current.Cards.ToDictionary(c => c.Id, c => c);
        var skipped = new List<string>();
        var importedCards = 0;
        foreach (var card in composed.Cards)
        {
            var data = CardData.From(card);
            if (CanSerialize(data)) { cards[data.Id] = data; importedCards++; }
            else skipped.Add($"card '{data.Id}'");
        }
        var actions = current.EnemyActions.ToDictionary(a => a.Id, a => a);
        var importedActions = 0;
        foreach (var action in composed.EnemyActions)
        {
            var data = EnemyActionData.From(action);
            if (CanSerialize(data)) { actions[data.Id] = data; importedActions++; }
            else skipped.Add($"action '{data.Id}'");
        }

        // The whole fight becomes one encounter: the enemy roster + the hero's combat resources / statuses.
        // Enemies only keep the actions that survived (a dropped action would dangle).
        var enemies = composed.Enemies.Select(e => new EncounterEnemy(
            e.Id, e.MaxHealth,
            e.Actions.Where(a => actions.ContainsKey(a.value)).ToList(),
            e.StartingStatuses.Count > 0 ? e.StartingStatuses.ToList() : null)).ToList();
        var id = new EncounterId(encounterId);
        var encounter = new EncounterDefinition(id, enemies, hero.Resources.ToList(),
            hero.StartingStatuses.Count > 0 ? hero.StartingStatuses.ToList() : null);
        var encounters = current.Encounters.Where(e => e.Id != id).Append(encounter).ToList();

        // The hero's deck (expanded by copies, only cards that survived) becomes the run's starting deck.
        var deck = hero.Deck
            .SelectMany(entry => Enumerable.Repeat(entry.Card, Math.Max(1, entry.Count)))
            .Where(c => cards.ContainsKey(c.value))
            .ToList();

        var merged = current with
        {
            Cards = cards.Values.ToList(),
            EnemyActions = actions.Values.ToList(),
            Encounters = encounters,
            Deck = deck.Count > 0 ? deck : current.Deck,
        };

        return new Result(merged, hero.Id, importedCards, importedActions, enemies.Count,
            deck.Count, encounterId, skipped);
    }
}
