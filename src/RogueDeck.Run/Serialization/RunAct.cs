namespace RogueDeck.Run;

// One act of a run: a stretch of map with its own rules, walked from its entry to its last node before the
// next act begins. What makes an act more than "more map" is that the rules can differ — the design's final
// act has no shops, no events, no treasure and no relic rewards at all, and that is simply a different
// MapGeneration, not a special case in the engine.
//
// `MapGeneration` generates this act's map per run (each act draws from its own seed, so two acts sharing one
// spec are still different maps); `Map` is an authored one. Exactly one of the two should be set — an act with
// neither falls back to the blueprint's own map, which is what makes a one-act blueprint expressible as an
// empty act list.
public sealed record RunAct(
    string Id,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    MapGenerationSpec? MapGeneration = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    RunMap? Map = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? NameKey = null);

// One act as the live run holds it: its id and the map that was built for it. The whole plan is laid out when
// the run starts, so the acts are as seed-deterministic as the first one and a resumed run rebuilds all of
// them identically.
public sealed record RunActPlan(string Id, RunMap Map);
