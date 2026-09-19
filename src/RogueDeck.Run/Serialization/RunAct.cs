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
//
// `StrategicMapGeneration` is the SHAPE half of the second generator (map rework S11): rows, widths, lane
// profiles, act-wide budgets, path pressure, fork quality. It sits BESIDE `MapGeneration` rather than replacing
// it, because the two generators both ship and the player picks (plan §4b) — and because the content half of
// `MapGeneration` (the encounter pools, the node refs, the balance targets) is what either generator realizes
// its rooms from. An act with only a strategic spec cannot be built by the strategic generator: it would have
// rooms and nothing to put in them.
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
    string? NameKey = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    StrategicActSpec? StrategicMapGeneration = null,

    // ── WHAT HAPPENS THE MOMENT THIS ACT BEGINS ──────────────────────────────────────────────────────────
    // Resolved once, right after the act is announced, before its first room. The act the run OPENS on is
    // included: a rule about "each act" should need no special case for the first one, which is the same
    // reason RunRunner announces that act at all.
    //
    // It lives on the act rather than on a relic because it is a rule of the PLACE, not a thing the player
    // carries — and because each act may want its own: a game can restore the body at the gates of act two
    // and let its final gauntlet begin on whatever is left, and that difference is the act's to state.
    //
    // ⚠ IT IS NOT A REWARD FOR FINISHING THE LAST ACT, and the difference shows on a run that dies: an
    // opening never fires for an act the run did not live to enter.
    //
    // Null (the default) ⇒ an act that simply begins, which is every blueprint written before this, and null
    // stays out of the wire format so those documents round-trip byte-identically.
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<IRunEffectRequest>? Opening = null);

// One act as the live run holds it: its id and the map that was built for it. The whole plan is laid out when
// the run starts, so the acts are as seed-deterministic as the first one and a resumed run rebuilds all of
// them identically.
public sealed record RunActPlan(string Id, RunMap Map, IReadOnlyList<IRunEffectRequest>? Opening = null);
