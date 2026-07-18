using RogueDeck.Core.Combat;

namespace RogueDeck.Run;

// Pure reader over a BalanceManifest: turns the authored strength/threat values into the two numbers the map
// generator balances — an encounter's THREAT (negative by convention) and a loadout's STRENGTH (positive). No RNG,
// no mutation. The generator sums a loadout once at run start and compares (loadout + encounter threat) against a
// target band per depth (see MapGenerationSpec.BalanceTargets); the Studio uses the same numbers for a readout.
public sealed class BalanceCalculator
{
    private readonly BalanceManifest _balance;
    private readonly IReadOnlyDictionary<EncounterId, EncounterDefinition> _encounters;

    public BalanceCalculator(BalanceManifest balance, IReadOnlyList<EncounterDefinition> encounters)
    {
        _balance = balance ?? new BalanceManifest();
        ArgumentNullException.ThrowIfNull(encounters);
        var map = new Dictionary<EncounterId, EncounterDefinition>();
        foreach (var encounter in encounters)
            map[encounter.Id] = encounter;
        _encounters = map;
    }

    // ── Threat (negative) ────────────────────────────────────────────────────────────────────────────────
    // Threat of one enemy definition id; falls back to Defaults.Enemy for an unvalued enemy.
    public int EnemyThreat(string enemyId) =>
        _balance.Enemies.TryGetValue(enemyId, out var value) ? value : _balance.Defaults.Enemy;

    // Threat of a whole encounter: the per-encounter override if the manifest sets one, else the sum of its
    // enemies' threats. An unknown encounter id (no definition) contributes 0.
    public int EncounterThreat(EncounterId id)
    {
        if (_balance.Encounters.TryGetValue(id.Value, out var overrideValue))
            return overrideValue;
        if (!_encounters.TryGetValue(id, out var definition))
            return 0;
        var total = 0;
        foreach (var enemy in definition.Enemies)
            total += EnemyThreat(enemy.Id);
        return total;
    }

    // ── Strength (positive) ──────────────────────────────────────────────────────────────────────────────
    public int CardStrength(string cardId) =>
        _balance.Cards.TryGetValue(cardId, out var value) ? value : _balance.Defaults.Card;

    public int RelicStrength(string relicId) =>
        _balance.Relics.TryGetValue(relicId, out var value) ? value : _balance.Defaults.Relic;

    public int ConsumableStrength(string consumableId) =>
        _balance.Consumables.TryGetValue(consumableId, out var value) ? value : _balance.Defaults.Consumable;

    public int CharacterBase(string characterId) =>
        _balance.Characters.TryGetValue(characterId, out var value) ? value : 0;

    // Strength of an authored starting loadout: the character's base + each deck card + each starting relic + each
    // starting consumable. The deck is the character's own RunStart.Deck, or `fallbackDeck` (the blueprint's shared
    // Deck) when the character declares none — mirroring RunSetup.CreateInitialRun. characterId (optional) selects
    // the character base from the manifest's Characters section.
    public int LoadoutStrength(
        RunStart start, IReadOnlyList<CardDefinitionId>? fallbackDeck = null, string? characterId = null)
    {
        ArgumentNullException.ThrowIfNull(start);
        var total = characterId is not null ? CharacterBase(characterId) : 0;

        var deck = start.Deck.Count > 0 ? start.Deck : fallbackDeck ?? Array.Empty<CardDefinitionId>();
        foreach (var card in deck)
            total += CardStrength(card.ToString());
        foreach (var relic in start.StartingRelics)
            total += RelicStrength(relic);
        foreach (var consumable in start.StartingConsumables)
            total += ConsumableStrength(consumable);

        return total;
    }

    // Strength of the run's LIVE loadout (its current deck, relics, consumables) — for a Studio readout that updates
    // as the run acquires content. Reads the active member's inventory; party members are a later refinement.
    public int LoadoutStrength(RunState run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var total = 0;
        foreach (var card in run.Deck)
            total += CardStrength(card.DefinitionId.ToString());
        foreach (var relic in run.Relics)
            total += RelicStrength(relic.Id.Value);
        foreach (var consumable in run.Consumables)
            total += ConsumableStrength(consumable.DefinitionId.Value);
        return total;
    }
}
