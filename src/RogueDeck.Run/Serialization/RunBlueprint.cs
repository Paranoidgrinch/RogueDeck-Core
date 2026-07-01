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
    RunMap Map);
