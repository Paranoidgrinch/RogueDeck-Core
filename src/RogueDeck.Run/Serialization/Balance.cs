namespace RogueDeck.Run;

// Numeric strength/threat values that steer rule-based map generation (see MapGenerationSpec / RuleBasedMapGenerator).
// Unlike the PresentationManifest, the GENERATOR reads these — they are not engine-ignored: each authored entity
// carries a balance weight so the generator can pick encounters whose NET difficulty (loadout strength + encounter
// threat) stays inside a target band per map depth, so a run is never trivial and never impossible.
//
// Convention (author-facing, not enforced by the type): enemy / encounter threats are NEGATIVE (a mob −10..−49, an
// elite ≈ −50, a boss ≈ −100); loadout pieces (cards, relics, consumables, a character's base) are POSITIVE. An id
// absent from a section takes the matching Defaults value (0 unless the author sets a default). Ids are the same
// definition ids used everywhere else — the enemy id inside encounters, and the card / relic / consumable /
// character ids. A plain record of int dictionaries, so it round-trips through RunJson for free and old documents
// (without a Balance section) deserialize with the empty default.
public sealed record BalanceManifest
{
    // Per-enemy threat (negative), keyed by the enemy definition id used inside encounters. An encounter's threat
    // is the SUM of its enemies' threats unless overridden in Encounters.
    public IReadOnlyDictionary<string, int> Enemies { get; init; } = Empty;

    // Per-encounter threat OVERRIDE (negative). When present it replaces the summed enemy threats for that
    // encounter id — for hand-tuned set-pieces whose danger isn't the plain sum of its roster.
    public IReadOnlyDictionary<string, int> Encounters { get; init; } = Empty;

    // Loadout strengths (positive): the value a deck card / a held relic / a held consumable / a character's base
    // contributes to the run's power. Summed by BalanceCalculator.LoadoutStrength.
    public IReadOnlyDictionary<string, int> Cards { get; init; } = Empty;
    public IReadOnlyDictionary<string, int> Relics { get; init; } = Empty;
    public IReadOnlyDictionary<string, int> Consumables { get; init; } = Empty;
    public IReadOnlyDictionary<string, int> Characters { get; init; } = Empty;

    // Fallback weights for ids not listed above, so an author need not value every single card/relic. All 0 by
    // default (an unvalued entity contributes nothing).
    public BalanceDefaults Defaults { get; init; } = new();

    private static readonly IReadOnlyDictionary<string, int> Empty = new Dictionary<string, int>();
}

// Fallback balance weights applied to ids the manifest doesn't list explicitly.
public sealed record BalanceDefaults
{
    public int Enemy { get; init; }
    public int Card { get; init; }
    public int Relic { get; init; }
    public int Consumable { get; init; }
}
