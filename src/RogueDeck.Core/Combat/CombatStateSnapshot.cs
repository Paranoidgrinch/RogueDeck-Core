using System.Collections.Immutable;

namespace RogueDeck.Core.Combat;

// Read-only pool capture used inside snapshots.
public readonly record struct PoolSnapshot(int Current, int? Max, bool CanExceedMax);

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
    ImmutableArray<(CounterId Key, int Value)> Counters, // sorted by key.value
    CombatantId? SourceCombatantId = null,
    CardDefinitionId? SourceCardId = null,
    int AppliedRound = 1,
    int AppliedTurn = 1,
    StatusVisibility Visibility = StatusVisibility.Visible
);

// Immutable capture of one card instance.
public sealed record CardInstanceSnapshot(
    CardInstanceId Id,
    CardDefinitionId DefinitionId,
    CardZone Zone
);

// Immutable capture of a combatant's card zones, in pile order.
public sealed record CombatantCardZonesSnapshot(
    ImmutableArray<CardInstanceSnapshot> DrawPile,
    ImmutableArray<CardInstanceSnapshot> Hand,
    ImmutableArray<CardInstanceSnapshot> DiscardPile,
    ImmutableArray<CardInstanceSnapshot> ExhaustPile,
    ImmutableArray<CardInstanceSnapshot> BanishedPile
);

// Immutable capture of a single combatant at a point in time.
public sealed record CombatantSnapshot(
    CombatantId Id,
    CombatantDefinitionId DefinitionId,
    TeamId TeamId,
    CombatantLifecycleState LifecycleState,
    int HealthCurrent,
    int HealthMax,
    ImmutableArray<(ResourceId Key, PoolSnapshot Pool)> Resources,           // sorted by key.value
    ImmutableArray<(DefensivePoolId Key, PoolSnapshot Pool)> DefensivePools, // sorted by key.value
    ImmutableArray<StatusInstanceSnapshot> Statuses,
    ImmutableArray<TagId> Tags,                          // sorted by value
    ImmutableArray<(CounterId Key, int Value)> Counters  // sorted by key.value
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
    ImmutableArray<(CombatantId CombatantId, CombatantCardZonesSnapshot Zones)> CardZones,
    int NextStatusInstanceNumber,
    int NextCardInstanceNumber,
    int NextSummonedCombatantNumber,
    long NextEffectChainNumber,
    long NextProgramExecutionId,
    ImmutableArray<TemporaryTriggeredProgramSnapshot> TemporaryRules
);
