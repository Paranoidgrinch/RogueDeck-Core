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

    // The run's starting state — hero identity + how the RunState is seeded (health, resources). Previously the
    // sandbox hard-coded HP 30/40 and an empty inventory; carrying it here makes the run's opening data too. An init
    // property with a default that reproduces the old hard-coded start, so existing blueprints are unaffected.
    public RunStart Start { get; init; } = new();
}

// The authored opening state of a run: the hero's display name, starting/maximum health, and starting resources
// (resource id → amount, e.g. gold). Kept flat + init-only so it round-trips through RunJson like the rest of the
// blueprint. Starting relics/consumables are a later slice (they need content resolution); the default reproduces
// the sandbox's historical hard-coded start (HP 30/40, empty resources).
public sealed record RunStart
{
    public string HeroName { get; init; } = "Hero";
    public int MaxHealth { get; init; } = 40;
    public int StartingHealth { get; init; } = 30;
    public IReadOnlyDictionary<string, int> Resources { get; init; } = new Dictionary<string, int>();
}
