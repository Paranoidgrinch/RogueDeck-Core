using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;
using RogueDeck.ShredEngine;

namespace RogueDeck.Run;

// The serializable, authored part of a run: the starting deck (card ids), the authored events by id, the
// encounters, the map (data nodes referencing events/encounters by id), and — so the combat content is data too
// — the card and enemy-action definitions the run uses (their effect programs serialize via CombatJson, merged
// into RunJson's options). Relics are still provided by code and referenced by id. A RunBlueprint thus captures
// what a designer authors as pure data, round-trippable via RunJson.
public sealed record RunBlueprint(
    IReadOnlyList<CardDefinitionId> Deck,
    IReadOnlyDictionary<string, EventScript> Events,
    IReadOnlyList<EncounterDefinition> Encounters,
    IReadOnlyList<CardData> Cards,
    IReadOnlyList<EnemyActionData> EnemyActions,
    RunMap Map)
{
    // The document's schema version (see RunBlueprintSchema). New blueprints are stamped with the current
    // version; loading goes through RunJson.BlueprintFromJson, which upgrades older documents and rejects newer
    // ones — so a Godot game (or the Studio) built against version N reads any document ≤ N safely.
    public int SchemaVersion { get; init; } = RunBlueprintSchema.CurrentVersion;

    // Custom status definitions the run's cards / actions reference (e.g. a card that applies an authored status).
    // Registered into every combat's content so the status resolves; without it the engine can't find the id and
    // the card is unplayable. An init property (not a positional field) so existing constructions stay unchanged.
    public IReadOnlyList<StatusData> Statuses { get; init; } = [];

    // Relics the run defines as data (a relic = run-level triggered programs). Registered into the content so an
    // event that grants one by id resolves it. An init property, like Statuses, to keep constructions unchanged.
    public IReadOnlyList<RelicData> Relics { get; init; } = [];

    // The run programs the CONTENT authors, by id: lasting consequences an event (or any other effect) installs
    // with fx.installProgramById. A relic carries its programs itself, but a consequence handed out by an event
    // belongs to no relic — and an effect that embedded the body would be neither serializable nor saveable — so
    // the body lives here once and is named where it is installed. Registered into the content catalog, which is
    // also what lets a saved run re-link an installed program on restore.
    //
    // Null (the default) ⇒ no authored programs, and null stays out of the wire format so every document written
    // before this section round-trips byte-identically.
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, ITriggeredRunEffectDefinition>? Programs { get; init; }

    // Custom combat resources (energy-like) the hero carries into every fight — an id + max + optional per-turn
    // refill. Injected into each combat's hero by the run→combat bridge. An init property, like Statuses/Relics.
    public IReadOnlyList<CombatResourceData> CombatResources { get; init; } = [];

    // Consumable KINDS the run defines as data (id + name + use effects). Registered into the content so a reward /
    // event / starting inventory can grant one by id. An init property, like Relics.
    public IReadOnlyList<ConsumableData> Consumables { get; init; } = [];

    // Shops the run defines as data, keyed by id. A map's shop node references one by id (ShopRef), the shop
    // counterpart of the Events dictionary. An init property, like the rest.
    public IReadOnlyDictionary<string, ShopDefinition> Shops { get; init; } = new Dictionary<string, ShopDefinition>();

    // The run's starting state — hero identity + how the RunState is seeded (health, resources). Previously the
    // sandbox hard-coded HP 30/40 and an empty inventory; carrying it here makes the run's opening data too. An init
    // property with a default that reproduces the old hard-coded start, so existing blueprints are unaffected.
    public RunStart Start { get; init; } = new();

    // A roster of selectable starting characters (character selection). Each is a full RunStart (own name / HP /
    // deck / relics / consumables / party). Empty (the default) ⇒ the single Start above is used, exactly as before.
    // When non-empty, the run is seeded from the chosen character (CreateInitialRun's characterId); the actual
    // CHARACTERS are content — the engine only models the roster + the pick (mechanism, not content). A later meta
    // layer can gate which of these are unlocked.
    public IReadOnlyList<RunCharacter> Characters { get; init; } = [];

    // The content's run-end meta-progression rules (unlocks / meta-currency / wins). The host supplies these to the
    // RunRunner alongside the persistent MetaState; they fold a finished run into the profile. Empty (the default) ⇒
    // no meta progression. The rules are content — the engine only evaluates them. Round-trips via RunJson.
    public IReadOnlyList<MetaRule> MetaRules { get; init; } = [];

    // ── Shred Engine sections (card composition; see src/RogueDeck.Run/ShredEngine/) ──────────────────
    // The card parts the run can grant, the authored recipes (shred combinations → curated cards), the
    // per-game composition rules, and the workbench stations map nodes reference by id. All empty by
    // default — a game without composition carries nothing.
    public IReadOnlyList<ShredData> Shreds { get; init; } = [];
    public IReadOnlyList<RecipeData> Recipes { get; init; } = [];
    public ShredRules ShredRules { get; init; } = new();
    public IReadOnlyDictionary<string, WorkbenchDefinition> Workbenches { get; init; }
        = new Dictionary<string, WorkbenchDefinition>();

    // The presentation manifest (Godot bridge, variant B): per entity id, how it LOOKS — art ids, flavor text,
    // visual tags. The engine ignores it entirely; a playable frontend (Godot) reads it to map ids onto its own
    // assets. Lives in the blueprint so one exported document carries the whole game, gameplay AND look.
    public PresentationManifest Presentation { get; init; } = new();

    // Numeric strength/threat values (enemies negative, loadout positive) that steer rule-based map generation
    // (RuleBasedMapGenerator / MapGenerationSpec). Unlike Presentation the generator READS these to keep each
    // encounter's net difficulty in a target band. Empty (the default) ⇒ no balancing input; a hand-authored or
    // unweighted map is unaffected. Round-trips via RunJson; old documents deserialize with the empty default.
    public BalanceManifest Balance { get; init; } = new();

    // The rules a run's map is generated from, per run, at run start (RuleBasedMapGenerator). Null (the default) ⇒
    // no procedural generation: the authored Map above is used as-is, exactly as before. When set, CreateInitialRun
    // builds the map from these rules + the run seed + the starting loadout strength (see Balance), guaranteeing the
    // per-path minimums and balancing each fight. Round-trips via RunJson.
    public MapGenerationSpec? MapGeneration { get; init; }

    // The run's ACTS, walked in order. A run is one RunState from the first act to the last — the deck, the
    // relics and the purse have to cross every boundary, and a save is a save of the whole run — so an act is a
    // SEGMENT of one walk rather than a separate game: its own map, its own generation rules, its own name.
    //
    // Null (the default) ⇒ exactly one act, built from the Map / MapGeneration above. Every blueprint written
    // before acts existed is that, and null is kept out of the wire format so those documents round-trip
    // byte-identically.
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<RunAct>? Acts { get; init; }

    // The effective starting config for a run: the chosen roster character, else the first roster character (a
    // deterministic default / an unknown id falls back here), else the single Start when there is no roster.
    public RunStart ResolveStart(string? characterId = null)
    {
        if (Characters.Count == 0)
            return Start;
        if (characterId is not null
            && Characters.FirstOrDefault(c => c.Id == characterId) is { } match)
            return match.Start;
        return Characters[0].Start;
    }
}

// One selectable starting character (character selection): a stable id (for the pick + the meta unlock gate) plus
// its full RunStart, and an optional UnlockFlag. When set, the character is only offered once the meta profile has
// that flag (MetaProgression.AvailableCharacters); null ⇒ always available. Which flag unlocks it — and how it is
// earned — is content. A plain record so it round-trips through RunJson like the rest of the blueprint.
public sealed record RunCharacter(string Id, RunStart Start, string? UnlockFlag = null);

// The authored opening state of a run: the hero's display name, starting/maximum health, starting resources
// (resource id → amount, e.g. gold), and the relics the hero begins with (ids resolved from the run's content when
// the run starts). Kept flat + init-only so it round-trips through RunJson like the rest of the blueprint. The
// default reproduces the sandbox's historical hard-coded start (HP 30/40, empty resources, no relics). Starting
// consumables are still a later slice (they need a consumable-definition registry to resolve an id to use-effects).
public sealed record RunStart
{
    public string HeroName { get; init; } = "Hero";
    public int MaxHealth { get; init; } = 40;
    public int StartingHealth { get; init; } = 30;
    public IReadOnlyDictionary<string, int> Resources { get; init; } = new Dictionary<string, int>();

    // The hero's starting deck (card ids) for THIS character. Empty (the default) ⇒ fall back to the blueprint's
    // shared Deck, so existing single-character blueprints (deck on RunBlueprint.Deck) are unchanged. A character
    // roster gives each character its own deck here; that is what makes character selection meaningful.
    public IReadOnlyList<CardDefinitionId> Deck { get; init; } = [];

    // Relic ids the hero starts with. Granted at run start from the content catalog (unknown ids are skipped); a
    // relic listed here should be defined in the blueprint's Relics (or be a built-in sample).
    public IReadOnlyList<string> StartingRelics { get; init; } = [];

    // Consumable definition ids the hero starts with (one instance each). Granted at run start from the content
    // catalog (unknown ids skipped); each id should be defined in the blueprint's Consumables.
    public IReadOnlyList<string> StartingConsumables { get; init; } = [];

    // The persistent player-controlled board units the run begins with (P5c). Seeded into RunState.Units at run
    // start and projected into each fight's player team. Empty (the default) ⇒ today's single-hero run.
    public IReadOnlyList<RunUnitData> StartingUnits { get; init; } = [];

    // Additional party members the run begins with, BESIDES the hero (member 0, seeded from the fields above).
    // Each is a full player character with its own HP / deck / resources (party deckbuilding B1c). Empty (the
    // default) ⇒ a single-hero run, exactly as before. Its relics/consumables are a later slice (need content).
    public IReadOnlyList<RunMemberData> StartingParty { get; init; } = [];
}

// The authored description of an additional party member (party deckbuilding B1c): its combat identity + display
// name, starting max HP, its own starting deck (card ids), and its own starting resources (currency included).
// Seeded into RunState.Party by RunSetup. A plain record so it round-trips through RunJson like the rest of RunStart.
public sealed record RunMemberData
{
    public string DefinitionId { get; init; } = "hero";
    public string DisplayNameKey { get; init; } = "Member";
    public int MaxHealth { get; init; } = 30;
    public IReadOnlyList<string> Deck { get; init; } = [];
    public IReadOnlyDictionary<string, int> Resources { get; init; } = new Dictionary<string, int>();

    // Relic / consumable definition ids this member starts with (party deckbuilding B3b). Granted per member by
    // the runner once content is attached (unknown ids skipped), exactly like the hero's RunStart.Starting* lists.
    public IReadOnlyList<string> StartingRelics { get; init; } = [];
    public IReadOnlyList<string> StartingConsumables { get; init; } = [];
}
