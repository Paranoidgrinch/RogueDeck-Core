namespace RogueDeck.Core.Combat;

// Raised when a combatant's grid position changes (by a movement effect). Unused until P2 introduces movement;
// declared now so P0's substrate is complete. A null From means the combatant was just placed.
public sealed record CombatantMovedCombatEvent(
    CombatantId CombatantId,
    CombatPosition? From,
    CombatPosition To
) : ICombatEvent;

public sealed record TurnStartedCombatEvent(
    CombatantId CombatantId,
    int Round,
    int Turn
) : ICombatEvent;

public sealed record TurnEndedCombatEvent(
    CombatantId CombatantId,
    int Round,
    int Turn
) : ICombatEvent;

public sealed record RoundStartedCombatEvent(
    int Round
) : ICombatEvent;

public sealed record RoundEndedCombatEvent(
    int Round,
    CombatantId? LastActiveCombatantId
) : ICombatEvent;

public sealed record ResourceGainedCombatEvent(
    CombatantId CombatantId,
    ResourceId ResourceId,
    int PreviousCurrent,
    int NewCurrent,
    int GainedAmount,
    int? Max
) : ICombatEvent;
public sealed record ResourceRefilledCombatEvent(
    CombatantId CombatantId,
    ResourceId ResourceId,
    int PreviousCurrent,
    int NewCurrent,
    int? Max
) : ICombatEvent;
public sealed record ResourceModifiedCombatEvent(
    CombatantId CombatantId,
    ResourceId ResourceId,
    int PreviousCurrent,
    int NewCurrent,
    int AppliedDelta,
    int? Max
) : ICombatEvent;
// Explicit, non-cost resource loss (e.g. an enemy drains mana). Distinct from a generic
// ResourceModified (which is a clamped adjustment that may be ±) and from CardCostPaid
// (cost payment). LostAmount is always > 0 when this event fires.
public sealed record ResourceLostCombatEvent(
    CombatantId CombatantId,
    ResourceId ResourceId,
    int PreviousCurrent,
    int NewCurrent,
    int LostAmount,
    int? Max
) : ICombatEvent;
public sealed record CardInstanceCreatedCombatEvent(
    CombatantId CombatantId,
    CardDefinitionId CardDefinitionId,
    IReadOnlyCollection<CardInstanceId> CardInstanceIds,
    CardZone ToZone
) : ICombatEvent;
public sealed record CardCostPaidCombatEvent(
    CombatantId SourceCombatantId,
    CardDefinitionId CardDefinitionId,
    CardInstanceId? CardInstanceId,
    IReadOnlyCollection<CalculatedResourceCost> Costs
) : ICombatEvent;
public sealed record CardPlayedCombatEvent(
    CardDefinitionId CardDefinitionId,
    CombatantId SourceCombatantId,
    CombatantId? TargetCombatantId,
    CardInstanceId? CardInstanceId = null
) : ICombatEvent;

public sealed record CardsDrawnCombatEvent(
    CombatantId CombatantId,
    IReadOnlyCollection<CardInstanceId> CardInstanceIds
) : ICombatEvent;

public sealed record DiscardPileShuffledIntoDrawPileCombatEvent(
    CombatantId CombatantId,
    IReadOnlyCollection<CardInstanceId> CardInstanceIds
) : ICombatEvent;

public sealed record HandDiscardedCombatEvent(
    CombatantId CombatantId,
    IReadOnlyCollection<CardInstanceId> CardInstanceIds
) : ICombatEvent;

public sealed record CardsMovedBetweenZonesCombatEvent(
    CombatantId CombatantId,
    IReadOnlyCollection<CardInstanceId> CardInstanceIds,
    CardZone FromZone,
    CardZone ToZone
) : ICombatEvent;
public sealed record CardMovedToZoneCombatEvent(
    CombatantId CombatantId,
    CardInstanceId CardInstanceId,
    CardZone FromZone,
    CardZone ToZone
) : ICombatEvent;

public sealed record CardTransformedCombatEvent(
    CombatantId CombatantId,
    CardInstanceId CardInstanceId,
    CardDefinitionId FromDefinition,
    CardDefinitionId ToDefinition
) : ICombatEvent;

public sealed record DamageDealtCombatEvent(
    CombatantId TargetCombatantId,
    int HealthDamage,
    int BlockedDamage,
    int RequestedAmount,
    DamageKind Kind = DamageKind.Direct,
    CombatantId? SourceCombatantId = null,
    CardDefinitionId? SourceCardId = null
) : ICombatEvent;

public sealed record DamageReceivedCombatEvent(
    CombatantId ReceiverCombatantId,
    int HealthDamage,
    int BlockedDamage,
    int RequestedAmount,
    DamageKind Kind = DamageKind.Direct,
    CombatantId? SourceCombatantId = null,
    CardDefinitionId? SourceCardId = null
) : ICombatEvent;

public sealed record HealedCombatEvent(
    CombatantId TargetCombatantId,
    int HealedAmount,
    int RequestedAmount,
    CombatantId? SourceCombatantId = null,
    CardDefinitionId? SourceCardId = null
) : ICombatEvent;

public sealed record StatusApplicationBlockedCombatEvent(
    CombatantId TargetCombatantId,
    StatusDefinitionId BlockedStatusDefinitionId,
    StatusInstanceId BlockingStatusInstanceId,
    StatusDefinitionId BlockingStatusDefinitionId
) : ICombatEvent;
public sealed record StatusAppliedCombatEvent(
    CombatantId TargetCombatantId,
    StatusInstanceId StatusInstanceId,
    StatusDefinitionId StatusDefinitionId,
    int Stacks,
    int DurationTurns,
    int Charges,
    CombatantId? SourceCombatantId = null,
    CardDefinitionId? SourceCardId = null
) : ICombatEvent;

public sealed record StatusMergedCombatEvent(
    CombatantId TargetCombatantId,
    StatusInstanceId StatusInstanceId,
    StatusDefinitionId StatusDefinitionId,
    int Stacks,
    int DurationTurns,
    int Charges,
    CombatantId? SourceCombatantId = null,
    CardDefinitionId? SourceCardId = null
) : ICombatEvent;

public sealed record StatusesRemovedByPolarityCombatEvent(
    CombatantId TargetCombatantId,
    IReadOnlyCollection<StatusInstanceId> StatusInstanceIds,
    StatusPolarity Polarity
) : ICombatEvent;
public sealed record StatusRemovedCombatEvent(
    CombatantId TargetCombatantId,
    IReadOnlyCollection<StatusInstanceId> StatusInstanceIds,
    StatusDefinitionId StatusDefinitionId
) : ICombatEvent;
public sealed record StatusChargesReducedCombatEvent(
    CombatantId TargetCombatantId,
    StatusInstanceId StatusInstanceId,
    StatusDefinitionId StatusDefinitionId,
    int OldCharges,
    int NewCharges
) : ICombatEvent;
public sealed record StatusExpiredCombatEvent(
    CombatantId TargetCombatantId,
    StatusInstanceId StatusInstanceId,
    StatusDefinitionId StatusDefinitionId
) : ICombatEvent;

public sealed record StatusStacksChangedCombatEvent(
    CombatantId TargetCombatantId,
    StatusInstanceId StatusInstanceId,
    StatusDefinitionId StatusDefinitionId,
    int OldStacks,
    int NewStacks
) : ICombatEvent;

public sealed record StatusDurationChangedCombatEvent(
    CombatantId TargetCombatantId,
    StatusInstanceId StatusInstanceId,
    StatusDefinitionId StatusDefinitionId,
    int OldDuration,
    int NewDuration
) : ICombatEvent;

public sealed record StatusChargesChangedCombatEvent(
    CombatantId TargetCombatantId,
    StatusInstanceId StatusInstanceId,
    StatusDefinitionId StatusDefinitionId,
    int OldCharges,
    int NewCharges
) : ICombatEvent;

public sealed record CombatantLifecycleChangedCombatEvent(
    CombatantId CombatantId,
    CombatantLifecycleState OldState,
    CombatantLifecycleState NewState
) : ICombatEvent;

public sealed record EnemyActionExecutedCombatEvent(
    EnemyActionDefinitionId ActionId,
    CombatantId ActorCombatantId,
    CombatantId? TargetCombatantId
) : ICombatEvent;

// Non-triggerable v1 event (no generic trigger adapter): represents a combat-result transition.
// It is surfaced via the combat log and CombatResultChangedTraceEvent, NOT dispatched through the
// trigger system, because both queue loops halt once the result is terminal. Mechanics that need to
// react to combat ending should react to the underlying event that ended it. See
// docs/combat-trigger-event-matrix.md.
public sealed record CombatResultChangedCombatEvent(
    CombatResult PreviousResult,
    CombatResult NewResult
) : ICombatEvent;

// Triggerable: fires after a temporary triggered program (temporary rule / delayed effect)
// successfully activates. Re-entry and depth limits guard against meta-trigger recursion.
public sealed record TemporaryRuleActivatedCombatEvent(
    TriggeredEffectDefinitionId RuleId,
    Type SourceEventType,
    CombatantId? ActiveCombatantId
) : ICombatEvent;
