namespace RogueDeck.Run;

// Presentation metadata for one authored entity — the Godot-facing "how it looks" half of the blueprint. The
// ENGINE never reads any of this (gameplay stays byte-identical with or without it); a playable frontend maps
// Art to an asset in its own project, shows FlavorText, and can key arbitrary per-game hints off Tags/Extra
// (e.g. a card frame style, a VFX id, a sound cue). Freeform strings on purpose: what an art id or a tag MEANS
// is the consuming game's contract, not the engine's.
public sealed record EntityPresentation
{
    // The asset the frontend shows for this entity (an id or relative path in the game's own asset scheme).
    public string? Art { get; init; }

    // Flavor line shown alongside the entity (rules text comes from the entity's own definition).
    public string? FlavorText { get; init; }

    // The entity's SMALL form (a status icon, a map marker, a shop thumbnail) when it differs from Art.
    public string? Icon { get; init; }

    // Common named hints most card games want — freeform strings, each game defines the vocabulary. Named here
    // (instead of living in Extra) so authors across games spell them the same way and frontends can rely on the
    // slot. Unused ones stay null and cost nothing.
    public string? Rarity { get; init; }   // "common", "rare", …
    public string? Frame { get; init; }    // card-frame / border style
    public string? Color { get; init; }    // accent color ("#8b0000" or a palette key)
    public string? Sound { get; init; }    // audio cue when played / used / entered
    public string? Vfx { get; init; }      // visual-effect id on resolve

    // Freeform labels a frontend can key visual treatments off ("rare", "fire", "boss").
    public IReadOnlyList<string> Tags { get; init; } = [];

    // Arbitrary per-game key→value hints for anything the named fields don't cover ("frame": "gold").
    public IReadOnlyDictionary<string, string> Extra { get; init; } = new Dictionary<string, string>();
}

// The blueprint's presentation manifest: per content kind, entity id → its presentation. A section entry whose
// id matches nothing is an authoring mistake (RunDocumentValidator flags it); an entity WITHOUT an entry is
// fine — the frontend falls back to its default look. Enemies are keyed by the enemy definition id used inside
// encounters (the same id = the same look everywhere).
public sealed record PresentationManifest
{
    public IReadOnlyDictionary<string, EntityPresentation> Cards { get; init; } = Empty;
    public IReadOnlyDictionary<string, EntityPresentation> Relics { get; init; } = Empty;
    public IReadOnlyDictionary<string, EntityPresentation> Consumables { get; init; } = Empty;
    public IReadOnlyDictionary<string, EntityPresentation> Statuses { get; init; } = Empty;
    public IReadOnlyDictionary<string, EntityPresentation> Enemies { get; init; } = Empty;
    public IReadOnlyDictionary<string, EntityPresentation> Encounters { get; init; } = Empty;
    public IReadOnlyDictionary<string, EntityPresentation> Characters { get; init; } = Empty;
    public IReadOnlyDictionary<string, EntityPresentation> Events { get; init; } = Empty;
    public IReadOnlyDictionary<string, EntityPresentation> Shops { get; init; } = Empty;

    // Game-wide presentation (title art, theme hints) — the one entry that is not per-entity.
    public EntityPresentation? Game { get; init; }

    private static readonly IReadOnlyDictionary<string, EntityPresentation> Empty =
        new Dictionary<string, EntityPresentation>();
}
