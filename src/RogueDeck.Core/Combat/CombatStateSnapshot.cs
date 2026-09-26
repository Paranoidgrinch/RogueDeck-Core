using System.Collections.Immutable;

namespace RogueDeck.Core.Combat;

// Read-only pool capture used inside snapshots.
public readonly record struct PoolSnapshot(int Current, int? Max, bool CanExceedMax);

// ⚠⚠ NOTHING THAT IS WRITTEN TO DISK MAY BE A TUPLE. Every one of the five types below used to be an
// inline `(Key, Value)` — correct in memory, correct through every in-process snapshot test, and SILENTLY
// EMPTY through `System.Text.Json`, which serializes PROPERTIES and finds only the fields `Item1`/`Item2`
// on a ValueTuple. A saved fight therefore came back as `Resources: [{}]`, `CardZones: [{}, {}]`,
// `Counters: [{}]` — the hero's energy, both combatants' draw piles, hands and discards, every counter,
// gone — and the restore then asked the fight for a combatant named "" and threw. That throw was the only
// reason any of it was ever noticed. A positional record serializes because its members are properties,
// and it still deconstructs, so every `foreach (var (key, value) in …)` downstream reads unchanged.
public readonly record struct CounterSnapshot(CounterId Key, int Value);
public readonly record struct ResourcePoolSnapshot(ResourceId Key, PoolSnapshot Pool);
public readonly record struct DefensivePoolSnapshot(DefensivePoolId Key, PoolSnapshot Pool);
public readonly record struct CombatantCardZonesEntry(CombatantId CombatantId, CombatantCardZonesSnapshot Zones);
public readonly record struct CountedKeySnapshot(string Key, int Count);

// Immutable capture of one status instance at a point in time. The Source* / Applied* / Visibility fields are not
// part of the determinism hash (the hasher ignores them), but ARE captured so a save can restore a status faithfully
// (e.g. a poison that remembers who applied it). Default so existing constructions / the hash are unaffected.
public sealed record StatusInstanceSnapshot(
    StatusInstanceId Id,
    StatusDefinitionId DefinitionId,
    CombatantId OwnerCombatantId,
    int Stacks,
    int DurationTurns,
    int Charges,
    StatusPolarity Polarity,
    ImmutableArray<TagId> Tags,                          // sorted by value
    ImmutableArray<CounterSnapshot> Counters,            // sorted by key.value
    CombatantId? SourceCombatantId = null,
    CardDefinitionId? SourceCardId = null,
    int AppliedRound = 1,
    int AppliedTurn = 1,
    StatusVisibility Visibility = StatusVisibility.Visible,
    // Turns this instance still waits before it takes effect (see StatusInstance.PendingTurns). Part of the
    // determinism hash: a pending status behaves differently from one in force.
    int PendingTurns = 0
);

// Immutable capture of one card instance, including its per-instance marks (tags/counters/source).
public sealed record CardInstanceSnapshot(
    CardInstanceId Id,
    CardDefinitionId DefinitionId,
    CardZone Zone,
    ImmutableArray<TagId> Marks = default,                       // sorted by value; default = empty
    ImmutableArray<CounterSnapshot> MarkCounters = default,            // sorted by key.value; default = empty
    CombatantId? MarkSourceCombatantId = null,
    // WHO A QUEUED CARD WAS AIMED AT. Queueing pays the cost and locks the target NOW; the card waits in the
    // queue until its resolution window. Without this a restored queue would resolve at whoever the rules
    // pick instead of whoever the player chose. Null for every card that is not waiting.
    CombatantId? QueuedTargetId = null
);

// Immutable capture of a combatant's card zones, in pile order.
public sealed record CombatantCardZonesSnapshot(
    ImmutableArray<CardInstanceSnapshot> DrawPile,
    ImmutableArray<CardInstanceSnapshot> Hand,
    ImmutableArray<CardInstanceSnapshot> DiscardPile,
    ImmutableArray<CardInstanceSnapshot> ExhaustPile,
    ImmutableArray<CardInstanceSnapshot> BanishedPile,
    // ⚠⚠ THE QUEUE, AND IT IS NOT AN AFTERTHOUGHT. Cards that have been PLAYED — paid for, targeted — and
    // whose effect has not happened yet. It was missing here until 2026-09-18, which meant a fight captured
    // with anything waiting came back WITHOUT IT: the cards were gone, the cost had been paid for nothing,
    // and nothing said so. That is every save taken mid-fight, and it was every turn boundary of the replay
    // model as well. Defaulted so a save written before this reads as an empty queue, which is what those
    // saves effectively had.
    ImmutableArray<CardInstanceSnapshot> QueuePile = default
);

// Immutable capture of a single combatant at a point in time.
public sealed record CombatantSnapshot(
    CombatantId Id,
    CombatantDefinitionId DefinitionId,
    TeamId TeamId,
    CombatantLifecycleState LifecycleState,
    int HealthCurrent,
    int HealthMax,
    ImmutableArray<ResourcePoolSnapshot> Resources,                          // sorted by key.value
    ImmutableArray<DefensivePoolSnapshot> DefensivePools,                    // sorted by key.value
    ImmutableArray<StatusInstanceSnapshot> Statuses,
    ImmutableArray<TagId> Tags,                          // sorted by value
    ImmutableArray<CounterSnapshot> Counters                                 // sorted by key.value
);

// Complete immutable snapshot of all gameplay-relevant combat state.
// Excludes transient execution queues and the combat log (output, not input state).
// Identity- and lifetime-relevant capture of a runtime temporary triggered program.
// The program body is by-reference content (like a registered trigger) and is not
// value-serialized; what varies at runtime — which rule is installed and its countdown
// — is captured so the state hash reflects installed delayed effects and temporary rules.
public sealed record TemporaryTriggeredProgramSnapshot(
    string Id,
    string EventType,
    int? RemainingActivations,
    int? ExpiresAfterRound,
    int? ExpiresAfterTurn,
    bool ExpiresWhenOwnerRemoved,
    string? OwnerCombatantId,
    int InstalledRound,
    int InstalledTurn,
    bool IsExpired,
    // Whether the live rule carried ad-hoc expiry effects. These are IEffectRequest bodies the snapshot does
    // NOT capture, so a registry-relinked restore refuses a rule that has them (not hashed — a guard signal only).
    bool HasExpiryEffects = false);

// Combatants and CardZones are ordered by TurnOrder for deterministic hashing.
public sealed record CombatStateSnapshot(
    CombatId Id,
    int RandomSeed,
    int RandomStep,
    CombatResult Result,
    int CurrentRound,
    int CurrentTurn,
    CombatTurnPhase TurnPhase,
    CombatantId? ActiveCombatantId,
    ImmutableArray<CombatantId> TurnOrder,
    ImmutableArray<CombatantSnapshot> Combatants,
    ImmutableArray<StatusInstanceSnapshot> GlobalStatuses,
    ImmutableArray<CombatantCardZonesEntry> CardZones,
    int NextStatusInstanceNumber,
    int NextCardInstanceNumber,
    int NextSummonedCombatantNumber,
    long NextEffectChainNumber,
    long NextProgramExecutionId,
    ImmutableArray<TemporaryTriggeredProgramSnapshot> TemporaryRules,

    // WHAT EACH TURN REMEMBERS — cards played this turn and last, what the turn opened with, damage dealt,
    // resources gained and spent. Empty (the default, and every snapshot taken before this field existed) is
    // a fight with no history, which is what a restore used to produce for ALL of them: every "more than last
    // turn" and "you opened with an Attack again" rule read zero on the far side of a save.
    ImmutableArray<CombatantCardPlayTurnStatsSnapshot> CardPlayTurnStats = default,

    // How often each trigger has paid out this fight (CombatState.TriggerActivity) — a host's reading, never a
    // rule's. Carried so that a fight rebuilt from its own snapshot (a checkpoint at a turn boundary, a resumed
    // save) keeps counting instead of starting again, which a host comparing two drawings would read as nothing
    // having fired. Written only when there is something to write; absent is an empty count.
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    ImmutableArray<TriggerActivitySnapshot> TriggerActivity = default
);

// One trigger's count. A named record, not a tuple — see the warning below.
public sealed record TriggerActivitySnapshot(string TriggerId, int Times);

// ⚠⚠ A VALUE TUPLE DOES NOT SURVIVE JSON. This was `(CombatantId, CardPlayTurnStatsSnapshot)`, which is
// correct in memory and correct through every in-process snapshot test — and `System.Text.Json` writes a
// ValueTuple as `{}`, because Item1 and Item2 are FIELDS and it serializes properties. So a saved fight came
// back with one empty entry per combatant, every id `default`, and the restore asked the fight for a
// combatant called "" and threw. A player who saved mid-fight could not continue their run. A named record
// serializes because its members are properties; that is the whole difference, and it is why nothing that is
// written to disk may be a tuple.
public sealed record CombatantCardPlayTurnStatsSnapshot(
    CombatantId CombatantId,
    CardPlayTurnStatsSnapshot Stats);
