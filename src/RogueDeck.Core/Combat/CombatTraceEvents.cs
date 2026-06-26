namespace RogueDeck.Core.Combat;

// Pluggable, optional trace listener. Set CombatState.TraceListener before combat starts.
// Events are fired synchronously, inline — no buffering.
// This listener carries the high-level combat trace (queue, event dispatch, turn, command).
// Deep Effect Program traces (ProgramStarted, NodeEntered, ScopeOpened/Closed, LimitExceeded,
// ProgramCompleted/Cancelled/Faulted — each with ExecutionId, NodePath, ScopeId, ChainId) are
// already implemented separately as EffectProgramTraceEvent (see EffectProgramTrace.cs), emitted
// through the execution context's trace sink. Only optional RNG/outcome-summary trace events
// remain as future diagnostics; they are not closure blockers.
public interface ICombatTraceListener
{
    void OnTrace(CombatTraceEvent evt);
}

// -----------------------------------------------------------------------
// Base
// -----------------------------------------------------------------------

public abstract record CombatTraceEvent(int Round, int Turn);

// -----------------------------------------------------------------------
// Command layer
// -----------------------------------------------------------------------

public sealed record CommandAppliedTraceEvent(
    int Round, int Turn,
    string CommandType
) : CombatTraceEvent(Round, Turn);

// -----------------------------------------------------------------------
// Effect queue
// -----------------------------------------------------------------------

public sealed record EffectEnqueuedTraceEvent(
    int Round, int Turn,
    string RequestType,
    long ChainId
) : CombatTraceEvent(Round, Turn);

public sealed record EffectResolvedTraceEvent(
    int Round, int Turn,
    string RequestType,
    long ChainId
) : CombatTraceEvent(Round, Turn);

// -----------------------------------------------------------------------
// Event queue
// -----------------------------------------------------------------------

public sealed record CombatEventEnqueuedTraceEvent(
    int Round, int Turn,
    string EventType,
    long ChainId
) : CombatTraceEvent(Round, Turn);

public sealed record CombatEventDispatchedTraceEvent(
    int Round, int Turn,
    string EventType,
    int HandlerCount
) : CombatTraceEvent(Round, Turn);

// -----------------------------------------------------------------------
// Turn lifecycle
// -----------------------------------------------------------------------

public sealed record TurnStartedTraceEvent(
    int Round, int Turn,
    CombatantId CombatantId
) : CombatTraceEvent(Round, Turn);

public sealed record TurnEndedTraceEvent(
    int Round, int Turn,
    CombatantId CombatantId
) : CombatTraceEvent(Round, Turn);

// -----------------------------------------------------------------------
// Combat result
// -----------------------------------------------------------------------

// Observable surface for a combat-result transition. CombatResultChanged is a deliberate
// NON-triggerable v1 event (no generic trigger adapter): both queue loops stop the instant the
// result becomes terminal, so reacting to it would require changing combat-end semantics. It is
// surfaced via the trace listener and the combat log instead — see docs/combat-trigger-event-matrix.md.
public sealed record CombatResultChangedTraceEvent(
    int Round, int Turn,
    CombatResult PreviousResult,
    CombatResult NewResult
) : CombatTraceEvent(Round, Turn);

// -----------------------------------------------------------------------
// Effect resolution derivations — the "how the engine produced this result" layer.
//
// Where the coarse CombatLog records the outcome ("dealt 13 damage"), these trace events
// record the full derivation: the base value, every modifier/pipeline step that shaped it,
// and the absorption/clamp that produced the final number. Emitted only when a TraceListener
// is attached, so there is no cost on the hot path otherwise.
// -----------------------------------------------------------------------

// One damage-modifier pipeline step. Only steps that actually changed the running amount are
// recorded; an absent modifier simply did not contribute.
public sealed record DamageModifierStepTrace(
    DamageModifierStage Stage,
    string ModifierId,
    int Before,
    int After
);

public sealed record DamageResolvedTraceEvent(
    int Round, int Turn,
    CombatantId TargetCombatantId,
    CombatantId? SourceCombatantId,
    CardDefinitionId? SourceCardId,
    DamageKind Kind,
    int BaseAmount,
    IReadOnlyList<DamageModifierStepTrace> ModifierSteps,
    int AmountAfterModifiers,
    DefensivePoolId? BlockPoolId,
    int BlockBefore,
    int BlockAfter,
    int BlockedAmount,
    int HealthBefore,
    int HealthAfter,
    int HealthLost,
    // True when this was block-ignoring ("true") damage: the block pool was bypassed entirely, so the
    // Block* fields above stay zero/null even if the target actually held block.
    bool IgnoresBlock = false
) : CombatTraceEvent(Round, Turn);

// Why a candidate trigger did or did not run during one event-dispatch pass. The generic
// trigger handler evaluates every registered + temporary trigger for the dispatched event in
// priority→id order; this records the outcome of that evaluation for each candidate so a
// diagnostic log can explain a trigger that silently never fired (filter rejected it, re-entry
// suppressed it, its context was unavailable) or that hit the recursion guard.
public enum TriggerEvaluationOutcome
{
    Fired,
    SkippedReentrySuppressed,
    SkippedDepthLimited,
    SkippedContextUnavailable,
    SkippedFilterRejected
}

public sealed record TriggerEvaluatedTraceEvent(
    int Round, int Turn,
    string EventType,
    string TriggerId,
    int Priority,
    bool IsTemporary,
    TriggerEvaluationOutcome Outcome
) : CombatTraceEvent(Round, Turn);

// How an ApplyStatusEffectRequest resolved: a fresh instance was applied, it merged into an
// existing instance (with the resulting stacks/duration/charges), or an interceptor suppressed it
// (Blocked) / swapped it for another request (Replaced, naming the replacement request type). The
// intercepting modifier id is recorded for the two interceptor outcomes so a diagnostic log can
// name what stopped or rewrote the application.
public enum StatusApplicationOutcome
{
    Applied,
    Merged,
    BlockedByInterceptor,
    ReplacedByInterceptor
}

public sealed record StatusApplicationResolvedTraceEvent(
    int Round, int Turn,
    CombatantId TargetCombatantId,
    StatusDefinitionId StatusDefinitionId,
    StatusApplicationOutcome Outcome,
    int RequestedStacks,
    int RequestedDurationTurns,
    int RequestedCharges,
    int ResultingStacks,
    int ResultingDurationTurns,
    int ResultingCharges,
    string? InterceptingModifierId,
    string? ReplacementRequestType
) : CombatTraceEvent(Round, Turn);

// How a HealEffectRequest resolved: the requested amount, the amount actually restored after the
// max-health clamp, and the health before/after.
public sealed record HealResolvedTraceEvent(
    int Round, int Turn,
    CombatantId TargetCombatantId,
    CombatantId? SourceCombatantId,
    CardDefinitionId? SourceCardId,
    int RequestedAmount,
    int HealedAmount,
    int HealthBefore,
    int HealthAfter
) : CombatTraceEvent(Round, Turn);

// How a random-target selection resolved: the candidate pool size, the requested pick count, the
// chosen target ids (in random order), and the RandomStep that seeded the deterministic shuffle.
public sealed record RandomTargetsSelectedTraceEvent(
    int Round, int Turn,
    int CandidatePoolSize,
    int RequestedCount,
    IReadOnlyList<CombatantId> SelectedTargetIds,
    int RandomStepUsed
) : CombatTraceEvent(Round, Turn);

// How a max-HP change resolved: the requested signed delta, the applied delta after flooring max at 1,
// the max HP before/after, and the current HP before/after (current is clamped down if max drops below it).
public sealed record MaxHealthChangeResolvedTraceEvent(
    int Round, int Turn,
    CombatantId TargetCombatantId,
    CombatantId? SourceCombatantId,
    CardDefinitionId? SourceCardId,
    int RequestedDelta,
    int AppliedDelta,
    int PreviousMax,
    int NewMax,
    int PreviousCurrent,
    int NewCurrent
) : CombatTraceEvent(Round, Turn);

// How a raw HP set resolved: the requested value, the value after clamping to [0, Max], the current HP
// before, and the signed delta. No damage/heal pipeline involvement (no modifiers, no down/heal events).
public sealed record HealthSetResolvedTraceEvent(
    int Round, int Turn,
    CombatantId TargetCombatantId,
    CombatantId? SourceCombatantId,
    CardDefinitionId? SourceCardId,
    int RequestedValue,
    int NewValue,
    int PreviousValue,
    int Delta
) : CombatTraceEvent(Round, Turn);

public sealed record CombatantTeamChangedResolvedTraceEvent(
    int Round, int Turn,
    CombatantId TargetCombatantId,
    TeamId PreviousTeam,
    TeamId NewTeam
) : CombatTraceEvent(Round, Turn);

// One block-amount modifier-pipeline step. Like DamageModifierStepTrace, only steps that actually
// changed the running amount are recorded.
public sealed record BlockModifierStepTrace(
    string ModifierId,
    int Before,
    int After
);

// How a GainBlockEffectRequest resolved: the requested amount, every block-amount modifier step
// that shaped it, the amount after modifiers, and the block pool before/after (clamped to int.MaxValue).
public sealed record BlockGainResolvedTraceEvent(
    int Round, int Turn,
    CombatantId TargetCombatantId,
    int RequestedAmount,
    IReadOnlyList<BlockModifierStepTrace> ModifierSteps,
    int AmountAfterModifiers,
    int BlockBefore,
    int BlockAfter
) : CombatTraceEvent(Round, Turn);

// How a resource change resolved. The four native resource operations share this derivation shape:
// the requested amount/delta, the applied delta after clamping (to min / max / zero / int.MaxValue),
// and the resource current before/after, plus whether a bound was reached.
public enum ResourceChangeKind
{
    Gained,
    Lost,
    Modified,
    Refilled
}

public sealed record ResourceChangeResolvedTraceEvent(
    int Round, int Turn,
    CombatantId CombatantId,
    ResourceId ResourceId,
    ResourceChangeKind Kind,
    int RequestedAmount,
    int AppliedDelta,
    int PreviousCurrent,
    int NewCurrent,
    bool ReachedMinimum,
    bool ReachedMaximum
) : CombatTraceEvent(Round, Turn);

// How a defensive-pool change resolved (ModifyDefensivePool / ClearDefensivePool): the requested
// delta, the applied delta after the pool's min/max clamp, and the pool value before/after.
public enum DefensivePoolChangeKind
{
    Modified,
    Cleared
}

public sealed record DefensivePoolChangeResolvedTraceEvent(
    int Round, int Turn,
    CombatantId TargetCombatantId,
    DefensivePoolId PoolId,
    DefensivePoolChangeKind Kind,
    int RequestedDelta,
    int AppliedDelta,
    int PreviousValue,
    int NewValue
) : CombatTraceEvent(Round, Turn);

// What a target selector resolved to during effect-program execution: the selector's runtime type,
// its declared cardinality, and the ordered list of combatant ids it produced. Records the actual
// targets a node acted on (e.g. why an all-enemies node hit two combatants, or why an event-target
// selector produced nothing because the target was downed).
public sealed record SelectorResolvedTraceEvent(
    int Round, int Turn,
    string SelectorType,
    TargetSelectorCardinality Cardinality,
    IReadOnlyList<string> ResolvedTargetIds
) : CombatTraceEvent(Round, Turn);

// One card-cost modifier-pipeline step (only steps that changed the running amount are recorded).
public sealed record CardCostModifierStepTrace(
    string ModifierId,
    int Before,
    int After
);

// How one resource cost of a card was derived: the printed base cost, every cost modifier that
// shaped it, and the final amount paid (0 means the cost was reduced away). One event per cost line.
public sealed record CardCostResolvedTraceEvent(
    int Round, int Turn,
    CardDefinitionId CardId,
    ResourceId ResourceId,
    int BaseAmount,
    IReadOnlyList<CardCostModifierStepTrace> ModifierSteps,
    int FinalAmount
) : CombatTraceEvent(Round, Turn);
