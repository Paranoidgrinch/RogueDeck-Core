using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;

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
    // Custom status definitions the run's cards / actions reference (e.g. a card that applies an authored status).
    // Registered into every combat's content so the status resolves; without it the engine can't find the id and
    // the card is unplayable. An init property (not a positional field) so existing constructions stay unchanged.
    public IReadOnlyList<StatusData> Statuses { get; init; } = [];

    // Relics the run defines as data (a relic = run-level triggered programs). Registered into the content so an
    // event that grants one by id resolves it. An init property, like Statuses, to keep constructions unchanged.
    public IReadOnlyList<RelicData> Relics { get; init; } = [];

    // Custom combat resources (energy-like) the hero carries into every fight — an id + max + optional per-turn
    // refill. Injected into each combat's hero by the run→combat bridge. An init property, like Statuses/Relics.
    public IReadOnlyList<CombatResourceData> CombatResources { get; init; } = [];

    // Consumable KINDS the run defines as data (id + name + use effects). Registered into the content so a reward /
    // event / starting inventory can grant one by id. An init property, like Relics.
    public IReadOnlyList<ConsumableData> Consumables { get; init; } = [];

    // The run's starting state — hero identity + how the RunState is seeded (health, resources). Previously the
    // sandbox hard-coded HP 30/40 and an empty inventory; carrying it here makes the run's opening data too. An init
    // property with a default that reproduces the old hard-coded start, so existing blueprints are unaffected.
    public RunStart Start { get; init; } = new();
}

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
