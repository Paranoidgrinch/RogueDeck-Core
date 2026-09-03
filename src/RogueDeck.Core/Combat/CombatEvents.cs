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

// A combatant actually gained Block (a gain modified down to zero raises nothing). The counterpart of
// HealedCombatEvent for the defensive pool — what "whenever someone gains Block" hooks listen to.
public sealed record BlockGainedCombatEvent(
    CombatantId TargetCombatantId,
    int GainedAmount,
    int RequestedAmount,
    CombatantId? SourceCombatantId = null,
    CardDefinitionId? SourceCardId = null
) : ICombatEvent;

// An amplification enlarged an application on its bearer and paid for it. The counterpart of the blocked
// event: it names what grew, by how much, and which status was spent growing it — so a rule can answer the
// AMPLIFIED application specifically (Act IV's register writes one glyph for a blessing made larger and
// another for a curse made larger, and the two are only distinguishable here).
public sealed record StatusApplicationAmplifiedCombatEvent(
    CombatantId TargetCombatantId,
    StatusDefinitionId AmplifiedStatusDefinitionId,
    StatusPolarity AmplifiedStatusPolarity,
    int AddedStacks,
    int ResultingStacks,
    StatusInstanceId AmplifyingStatusInstanceId,
    StatusDefinitionId AmplifyingStatusDefinitionId,
    // Who applied the status that was enlarged, when the application named a source.
    CombatantId? SourceCombatantId = null
) : ICombatEvent;

public sealed record StatusApplicationBlockedCombatEvent(
    CombatantId TargetCombatantId,
    StatusDefinitionId BlockedStatusDefinitionId,
    StatusInstanceId BlockingStatusInstanceId,
    StatusDefinitionId BlockingStatusDefinitionId,
    // Who tried to apply it, when the application named a source. A rule that answers a refusal usually wants
    // to answer the one who was refused — "the first time each turn your Censure prevents a negative Status
    // applied by an enemy, apply 2 Censure to THAT enemy".
    CombatantId? SourceCombatantId = null
) : ICombatEvent;
public sealed record StatusAppliedCombatEvent(
    CombatantId TargetCombatantId,
    StatusInstanceId StatusInstanceId,
    StatusDefinitionId StatusDefinitionId,
    int Stacks,
    int DurationTurns,
    int Charges,
    CombatantId? SourceCombatantId = null,
    CardDefinitionId? SourceCardId = null,
    // Whether this application was a COPY of another one. A copy may be answered like any other application;
    // what it may not do is feed the rule that made it.
    bool Replicated = false
) : ICombatEvent;

// A postponed status has finished waiting and is now in force. It is deliberately NOT a second StatusApplied:
// the application already happened, this is the notice taking effect.
public sealed record StatusActivatedCombatEvent(
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
    CardDefinitionId? SourceCardId = null,
    // As StatusAppliedCombatEvent: whether the application that merged here was a copy.
    bool Replicated = false,
    // How many stacks THIS application added, as opposed to `Stacks`, which is what the instance now holds.
    // A rule that answers an application by its size — "gain favour equal to the stacks gained" — cannot use
    // the total: a second blessing of one stack on top of three would read as four. On a fresh application
    // the two are the same number, which is why only the merge carries the extra field.
    int AppliedStacks = 0
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
