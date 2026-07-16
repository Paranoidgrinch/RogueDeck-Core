using RogueDeck.Run;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.ShredEngine;

// The Shred Engine's combat seam: before a fight compiles, every distinct composition found in the party's
// decks is re-synthesized into a CardBlueprint and added to the fight's (still mutable) ScenarioBlueprint —
// so the deck's derived shred:… definition ids resolve and ValidateReferences passes. A standing projection
// modifier (applied EVERY fight, see CombatNodeResolver), because a composed card persists as nothing but
// its part list; determinism of the synthesizer guarantees each fight sees the identical definition.
public sealed class ShredCombatInjection : IRunCombatModifier
{
    public void Apply(ScenarioBlueprint blueprint, RunState run)
    {
        var compositions = run.Party
            .SelectMany(member => member.Deck)
            .Where(card => card.Composition.Count > 0)
            .GroupBy(card => card.DefinitionId.value, StringComparer.Ordinal)
            .Select(group => group.First());

        foreach (var card in compositions)
        {
            if (blueprint.Cards.Any(existing => existing.Id == card.DefinitionId.value))
                continue; // already injected (or, pathologically, authored) — the registry rejects duplicates

            if (run.Content is null)
                throw new InvalidOperationException(
                    $"Deck card '{card.DefinitionId.value}' is composed from shreds, but the run has no content "
                    + "catalog to resolve them from.");

            var parts = card.Composition.Select(id => run.Content.GetShred(id)).ToList();
            if (!ShredCardSynthesizer.TrySynthesize(parts, out var synthesized, out var error))
                throw new InvalidOperationException(
                    $"Deck card '{card.DefinitionId.value}' cannot be synthesized: {error}");

            blueprint.Cards.Add(synthesized);
        }
    }
}
