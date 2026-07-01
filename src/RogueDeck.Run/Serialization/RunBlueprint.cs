using RogueDeck.Core.Combat;

namespace RogueDeck.Run;

// The serializable, authored part of a run: the starting deck (card ids), the authored events by id, and the
// map (data nodes referencing events/encounters by id). The combat content library, encounters and relics are
// provided by code and referenced by id — so a RunBlueprint captures what a designer authors as pure data,
// round-trippable via RunJson. Serialized directly (record; nested EventScript/RunMap use their converters).
public sealed record RunBlueprint(
    IReadOnlyList<CardDefinitionId> Deck,
    IReadOnlyDictionary<string, EventScript> Events,
    IReadOnlyList<EncounterDefinition> Encounters,
    RunMap Map);
