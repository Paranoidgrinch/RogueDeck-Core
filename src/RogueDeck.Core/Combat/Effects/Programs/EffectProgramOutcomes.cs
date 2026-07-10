namespace RogueDeck.Core.Combat;

// ── Typed outcomes ────────────────────────────────────────────────────────────

public sealed record DamageOutcome(
    int RequestedAmount,
    int BlockedAmount,
    int HealthLost,
    int PreviousHealth,
    int NewHealth,
    // Damage that landed on HP beyond what the target had — the "overkill" past a lethal hit
    // (post-block, post-modifier amount − HealthLost). Zero unless the hit reduced the target to 0 HP.
    int Overkill = 0);

public sealed record HealOutcome(
    int RequestedAmount,
    int HealedAmount,
    int PreviousHealth,
    int NewHealth);

public sealed record ModifyMaxHealthOutcome(
    int RequestedDelta,
    int AppliedDelta,
    int PreviousMax,
    int NewMax,
    int PreviousCurrent,
    int NewCurrent);

public sealed record SetHealthOutcome(
    int RequestedValue,
    int NewValue,
    int PreviousValue,
    int Delta);

public sealed record PoolChangeOutcome(
    int RequestedDelta,
    int AppliedDelta,
    int PreviousValue,
    int NewValue);

public sealed record GainBlockOutcome(
    int RequestedAmount,
    int ModifiedAmount,
    int PreviousBlock,
    int NewBlock);

public sealed record ClearDefensivePoolOutcome(
    int ClearedAmount,
    bool WasChanged);

public sealed record GainResourceOutcome(
    int RequestedAmount,
    int GainedAmount,
    int PreviousCurrent,
    int NewCurrent,
    bool ReachedMaximum);

public sealed record RefillResourceOutcome(
    int PreviousCurrent,
    int NewCurrent,
    int DefaultMax);

public sealed record LoseResourceOutcome(
    int RequestedAmount,
    int LostAmount,
    int PreviousCurrent,
    int NewCurrent,
    bool ReachedZero);

public sealed record ApplyStatusOutcome(
    bool Applied,
    bool Merged,
    bool Blocked,
    int ResultingStacks,
    int ResultingDurationTurns,
    int ResultingCharges);

public sealed record RemoveStatusOutcome(
    int RemovedCount,
    IReadOnlyList<StatusInstanceId> RemovedInstanceIds);

public sealed record RemoveStatusesByPolarityOutcome(
    int RemovedCount,
    IReadOnlyList<StatusInstanceId> RemovedInstanceIds,
    StatusPolarity Polarity);

public sealed record DrawCardsOutcome(
    int RequestedCount,
    int DrawnCount,
    IReadOnlyList<CardInstanceId> DrawnCardInstanceIds);

public sealed record MoveAllCardsFromZoneOutcome(
    int MovedCount,
    IReadOnlyList<CardInstanceId> MovedCardInstanceIds,
    CardZone FromZone,
    CardZone ToZone);

public sealed record CreateCardInstanceOutcome(
    int CreatedCount,
    IReadOnlyList<CardInstanceId> CreatedCardInstanceIds,
    CardZone ToZone);

public sealed record SetCombatantLifecycleStateOutcome(
    CombatantLifecycleState PreviousState,
    CombatantLifecycleState NewState,
    bool WasChanged);

public sealed record SummonCombatantOutcome(
    CombatantId SummonedCombatantId,
    TeamId TeamId,
    int MaxHealth);

public sealed record ChangeCombatantTeamOutcome(
    TeamId PreviousTeam,
    TeamId NewTeam,
    bool WasChanged);

public sealed record ModifyResourceOutcome(
    int RequestedDelta,
    int AppliedDelta,
    int PreviousValue,
    int CurrentValue,
    bool ReachedMinimum,
    bool ReachedMaximum,
    bool WasChanged);

public sealed record MoveCardToZoneOutcome(
    CardInstanceId? CardInstanceId,
    CardZone? PreviousZone,
    CardZone? CurrentZone,
    bool WasMoved);

public sealed record TransformCardOutcome(
    CardInstanceId? CardInstanceId,
    CardDefinitionId? PreviousDefinition,
    CardDefinitionId? CurrentDefinition,
    bool WasTransformed);

public sealed record SetCombatResultOutcome(
    CombatResult PreviousResult,
    CombatResult CurrentResult,
    bool WasChanged);

public sealed record PlayCardOutcome(
    CardInstanceId CardInstanceId,
    bool WasPlayed);

public sealed record InstallTemporaryRuleOutcome(
    TriggeredEffectDefinitionId RuleId,
    bool WasInstalled);

public sealed record RemoveTemporaryRuleOutcome(
    TriggeredEffectDefinitionId RuleId,
    bool WasRemoved);

// ── Outcome slots (filled by handlers, read by post-settle continuations) ─────
//
// A handler resolving a request with an outcome slot must complete that slot exactly once on
// every legal path (including no-ops). The shared base enforces the once-only invariant: setting
// Value a second time throws, and IsCompleted lets callers/tests assert completion.
public abstract class OutcomeSlot<TOutcome>
    where TOutcome : class
{
    private TOutcome? _value;

    public bool IsCompleted { get; private set; }

    public TOutcome? Value
    {
        get => _value;
        internal set
        {
            if (IsCompleted)
                throw new InvalidOperationException(
                    $"Outcome slot '{GetType().Name}' was already completed and cannot be completed again.");
            _value = value;
            IsCompleted = true;
        }
    }
}

public sealed class DamageOutcomeSlot : OutcomeSlot<DamageOutcome>;

public sealed class HealOutcomeSlot : OutcomeSlot<HealOutcome>;

public sealed class ModifyMaxHealthOutcomeSlot : OutcomeSlot<ModifyMaxHealthOutcome>;

public sealed class SetHealthOutcomeSlot : OutcomeSlot<SetHealthOutcome>;

public sealed class PoolChangeOutcomeSlot : OutcomeSlot<PoolChangeOutcome>;

public sealed class GainBlockOutcomeSlot : OutcomeSlot<GainBlockOutcome>;

public sealed class ClearDefensivePoolOutcomeSlot : OutcomeSlot<ClearDefensivePoolOutcome>;

public sealed class GainResourceOutcomeSlot : OutcomeSlot<GainResourceOutcome>;

public sealed class LoseResourceOutcomeSlot : OutcomeSlot<LoseResourceOutcome>;

public sealed class RefillResourceOutcomeSlot : OutcomeSlot<RefillResourceOutcome>;

public sealed class ApplyStatusOutcomeSlot : OutcomeSlot<ApplyStatusOutcome>;

public sealed class RemoveStatusOutcomeSlot : OutcomeSlot<RemoveStatusOutcome>;

public sealed class RemoveStatusesByPolarityOutcomeSlot : OutcomeSlot<RemoveStatusesByPolarityOutcome>;

public sealed record ModifyStatusStacksOutcome(
    int OldStacks,
    int NewStacks,
    int ActualDelta,
    bool WasChanged,
    bool WasRemoved);

public sealed record ModifyStatusDurationOutcome(
    int OldDuration,
    int NewDuration,
    int ActualDelta,
    bool WasChanged,
    bool WasRemoved);

public sealed record ModifyStatusChargesOutcome(
    int OldCharges,
    int NewCharges,
    int ActualDelta,
    bool WasChanged,
    bool WasRemoved);

public sealed class ModifyStatusStacksOutcomeSlot : OutcomeSlot<ModifyStatusStacksOutcome>;

public sealed class ModifyStatusDurationOutcomeSlot : OutcomeSlot<ModifyStatusDurationOutcome>;

public sealed class ModifyStatusChargesOutcomeSlot : OutcomeSlot<ModifyStatusChargesOutcome>;

public sealed class DrawCardsOutcomeSlot : OutcomeSlot<DrawCardsOutcome>;

public sealed class MoveAllCardsFromZoneOutcomeSlot : OutcomeSlot<MoveAllCardsFromZoneOutcome>;

public sealed class CreateCardInstanceOutcomeSlot : OutcomeSlot<CreateCardInstanceOutcome>;

public sealed class SetCombatantLifecycleStateOutcomeSlot : OutcomeSlot<SetCombatantLifecycleStateOutcome>;
public sealed class SummonCombatantOutcomeSlot : OutcomeSlot<SummonCombatantOutcome>;
public sealed class ChangeCombatantTeamOutcomeSlot : OutcomeSlot<ChangeCombatantTeamOutcome>;

public sealed class ModifyResourceOutcomeSlot : OutcomeSlot<ModifyResourceOutcome>;

public sealed class MoveCardToZoneOutcomeSlot : OutcomeSlot<MoveCardToZoneOutcome>;

public sealed class TransformCardOutcomeSlot : OutcomeSlot<TransformCardOutcome>;

public sealed class SetCombatResultOutcomeSlot : OutcomeSlot<SetCombatResultOutcome>;

public sealed class PlayCardOutcomeSlot : OutcomeSlot<PlayCardOutcome>;

public sealed class InstallTemporaryRuleOutcomeSlot : OutcomeSlot<InstallTemporaryRuleOutcome>;

public sealed class RemoveTemporaryRuleOutcomeSlot : OutcomeSlot<RemoveTemporaryRuleOutcome>;

// ── Ordered multi-target outcome collection ───────────────────────────────────

public sealed record TargetOutcome<T>(CombatantId TargetId, T Outcome, int DeterministicIndex);

public sealed class OrderedTargetOutcomes<T>
{
    public IReadOnlyList<TargetOutcome<T>> Results { get; }

    public OrderedTargetOutcomes(IEnumerable<TargetOutcome<T>> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        Results = [.. results];
    }

    public static OrderedTargetOutcomes<T> Empty { get; } = new([]);

    // Convenience for tests and single-target expressions.
    // Throws when Results.Count != 1.
    public T Single()
    {
        if (Results.Count != 1)
            throw new InvalidOperationException(
                $"Expected exactly one target outcome, but found {Results.Count}.");
        return Results[0].Outcome;
    }
}

// ── Outcome cardinality wrappers (§10.12) ─────────────────────────────────────
//
// These wrappers declare the expected cardinality of an operation's result:
//   SingleOutcome<T>   — exactly one target was processed; throws on empty/multi.
//   OptionalOutcome<T> — zero or one target was processed.
//
// Used with EffectResultKey to assert cardinality at preflight and read time.
// The preflight validator uses these to catch mismatches before combat starts.

public sealed record SingleOutcome<T>(T Outcome)
{
    // Convenience: unwrap from an OrderedTargetOutcomes that must have exactly one result.
    public static SingleOutcome<T> FromOrdered(OrderedTargetOutcomes<T> ordered)
    {
        ArgumentNullException.ThrowIfNull(ordered);

        if (ordered.Results.Count != 1)
            throw new InvalidOperationException(
                $"Expected exactly one target outcome for SingleOutcome<{typeof(T).Name}>, " +
                $"but found {ordered.Results.Count}.");

        return new SingleOutcome<T>(ordered.Results[0].Outcome);
    }
}

public sealed record OptionalOutcome<T>(T? Outcome, bool HasValue)
{
    public static OptionalOutcome<T> Empty { get; } = new(default, HasValue: false);

    public static OptionalOutcome<T> Of(T outcome) => new(outcome, HasValue: true);

    // Convenience: unwrap from an OrderedTargetOutcomes that has zero or one result.
    public static OptionalOutcome<T> FromOrdered(OrderedTargetOutcomes<T> ordered)
    {
        ArgumentNullException.ThrowIfNull(ordered);

        if (ordered.Results.Count > 1)
            throw new InvalidOperationException(
                $"Expected at most one target outcome for OptionalOutcome<{typeof(T).Name}>, " +
                $"but found {ordered.Results.Count}.");

        return ordered.Results.Count == 0
            ? Empty
            : new OptionalOutcome<T>(ordered.Results[0].Outcome, HasValue: true);
    }
}

// ── Result key ────────────────────────────────────────────────────────────────

public sealed record EffectResultKey<TOutcome>(string Name)
{
    public string Name { get; init; } =
        string.IsNullOrWhiteSpace(Name)
            ? throw new ArgumentException("Result key name must not be empty.", nameof(Name))
            : Name;
}

// ── Non-generic execution context interface ───────────────────────────────────
//
// Exposes all per-invocation state that node executors need without requiring
// them to know TContext. Implemented by EffectExecutionContext<TContext>.

public interface IEffectExecutionContextCore
{
    TriggeredEffectActionBuildContext BuildContext { get; }

    // Iteration target is lexical: the innermost open iteration scope (ForEach / aggregate).
    // There is no public setter — scopes are opened and closed in balanced pairs so the outer
    // target is restored automatically when an inner scope closes.
    CombatantId? IterationTarget { get; }
    // The 0-based index of the innermost open iteration scope, or null when not iterating.
    int? IterationIndex { get; }
    void PushIterationTarget(CombatantId target);
    void PushIterationTarget(CombatantId target, int index);
    void PopIterationTarget();
    // Push/pop only a loop index (no iteration target) — for count-less loops such as repeat-until,
    // where the body wants the 0-based pass number but there is no per-iteration target.
    void PushLoopIndex(int index);
    void PopLoopIndex();

    CombatEffectChainContext? EffectChain { get; set; }
    int ProgramStepCount { get; set; }
    int MaxProgramSteps { get; set; }
    EffectProgramId ProgramId { get; set; }

    // Result store — scoped. Each OpenScope/CloseScope call brackets a lexical
    // region. Store writes to the innermost scope. Get/TryGet walk outward.
    void OpenScope();
    void CloseScope();
    int ActiveScopeCount { get; }
    int MaxActiveScopes { get; set; }

    void Store<TOutcome>(EffectResultKey<TOutcome> key, TOutcome outcome);
    TOutcome Get<TOutcome>(EffectResultKey<TOutcome> key);
    bool TryGet<TOutcome>(EffectResultKey<TOutcome> key, out TOutcome? outcome);

    IEffectProgramTraceSink TraceSink { get; set; }

    CombatantTargetSelectionContext GetTargetSelectionContext();
    TriggeredEffectActionBuildContext GetBuildContextWithIterationTarget();
}

// ── Per-execution mutable context ─────────────────────────────────────────────

public sealed class EffectExecutionContext<TContext> : IEffectExecutionContextCore
    where TContext : class
{
    // Scope stack: index 0 is the root (program-level) scope; new scopes are
    // pushed onto the end. Store writes to the innermost scope. Get/TryGet
    // search from innermost to root so child scopes shadow parent values but
    // do not leak after the scope closes.
    // Key is (name, outcomeType) so two keys with the same name but different
    // TOutcome never collide (type-safe storage).
    private readonly List<Dictionary<(string Name, Type OutcomeType), object>> _scopes = [new()];

    // Deterministic scope identity: the root scope is 0; each OpenScope takes the next id from
    // a monotonic per-execution counter. _scopeIds mirrors _scopes so CurrentScopeId is the
    // innermost open scope.
    private readonly List<long> _scopeIds = [0];
    private long _nextScopeId;

    public TContext SourceContext { get; }
    public TriggeredEffectActionBuildContext BuildContext { get; }

    // Lexical iteration-target stack: the innermost open ForEach/aggregate scope is the top.
    // Inner scopes shadow outer ones; closing a scope (pop) restores the parent target.
    private readonly Stack<CombatantId> _iterationTargets = new();
    // Parallel to _iterationTargets: the 0-based index of each open iteration scope. Pushed/popped
    // in lockstep so IterationIndex always matches IterationTarget for the innermost loop.
    private readonly Stack<int> _iterationIndices = new();
    public CombatantId? IterationTarget =>
        _iterationTargets.Count > 0 ? _iterationTargets.Peek() : null;
    public int? IterationIndex =>
        _iterationIndices.Count > 0 ? _iterationIndices.Peek() : null;
    public void PushIterationTarget(CombatantId target) => PushIterationTarget(target, 0);
    public void PushIterationTarget(CombatantId target, int index)
    {
        _iterationTargets.Push(target);
        _iterationIndices.Push(index);
    }
    public void PopIterationTarget()
    {
        _iterationTargets.Pop();
        _iterationIndices.Pop();
    }
    public void PushLoopIndex(int index) => _iterationIndices.Push(index);
    public void PopLoopIndex() => _iterationIndices.Pop();

    // Back-reference to the owning frame, set by EffectProgramExecutor.Execute. Lets the
    // node dispatcher reject stale continuations and fault the frame on a runtime exception.
    internal EffectProgramExecutionFrame<TContext>? Frame { get; set; }

    public CombatEffectChainContext? EffectChain { get; set; }

    public int ProgramStepCount { get; set; }

    public int MaxProgramSteps { get; set; } = EffectProgram<TContext>.DefaultMaxProgramSteps;

    public EffectProgramId ProgramId { get; set; }

    public int ActiveScopeCount => _scopes.Count;

    public int MaxActiveScopes { get; set; } = 32;

    // Trace identities: the innermost open scope, the owning invocation, and the bound chain.
    internal long CurrentScopeId => _scopeIds[^1];
    internal EffectProgramExecutionId CurrentExecutionId => Frame?.ExecutionId ?? default;
    internal long? CurrentChainId => EffectChain?.Id.Value;

    public IEffectProgramTraceSink TraceSink { get; set; } = NullEffectProgramTraceSink.Instance;

    public EffectExecutionContext(
        TContext context,
        TriggeredEffectActionBuildContext buildContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(buildContext);

        SourceContext = context;
        BuildContext = buildContext;
    }

    public void OpenScope()
    {
        if (_scopes.Count >= MaxActiveScopes)
        {
            TraceSink.Record(new EffectProgramTraceEvent
            {
                Kind = EffectProgramTraceEventKind.LimitExceeded,
                ProgramId = ProgramId,
                ScopeDepth = _scopes.Count,
                Detail = $"MaxActiveScopes={MaxActiveScopes}",
                ExecutionId = CurrentExecutionId,
                ScopeId = CurrentScopeId,
                ChainId = CurrentChainId,
            });
            throw new InvalidOperationException(
                $"Effect program '{ProgramId}' exceeded the active scope limit of {MaxActiveScopes}.");
        }
        _scopes.Add(new Dictionary<(string, Type), object>());
        _scopeIds.Add(++_nextScopeId);
        TraceSink.Record(new EffectProgramTraceEvent
        {
            Kind = EffectProgramTraceEventKind.ScopeOpened,
            ProgramId = ProgramId,
            ScopeDepth = _scopes.Count,
            ExecutionId = CurrentExecutionId,
            ScopeId = CurrentScopeId,
            ChainId = CurrentChainId,
        });
    }

    public void CloseScope()
    {
        if (_scopes.Count <= 1)
            throw new InvalidOperationException(
                "Cannot close the program root scope.");
        var closedScopeId = _scopeIds[^1];
        _scopes.RemoveAt(_scopes.Count - 1);
        _scopeIds.RemoveAt(_scopeIds.Count - 1);
        TraceSink.Record(new EffectProgramTraceEvent
        {
            Kind = EffectProgramTraceEventKind.ScopeClosed,
            ProgramId = ProgramId,
            ScopeDepth = _scopes.Count,
            ExecutionId = CurrentExecutionId,
            ScopeId = closedScopeId,
            ChainId = CurrentChainId,
        });
    }

    public void Store<TOutcome>(EffectResultKey<TOutcome> key, TOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull((object?)outcome);

        _scopes[_scopes.Count - 1][(key.Name, typeof(TOutcome))] = outcome;
    }

    public TOutcome Get<TOutcome>(EffectResultKey<TOutcome> key)
    {
        ArgumentNullException.ThrowIfNull(key);

        var storageKey = (key.Name, typeof(TOutcome));
        for (var i = _scopes.Count - 1; i >= 0; i--)
        {
            if (_scopes[i].TryGetValue(storageKey, out var raw))
                return (TOutcome)raw;
        }

        throw new InvalidOperationException(
            $"No result stored for key '{key.Name}' ({typeof(TOutcome).Name}).");
    }

    public bool TryGet<TOutcome>(EffectResultKey<TOutcome> key, out TOutcome? outcome)
    {
        ArgumentNullException.ThrowIfNull(key);

        var storageKey = (key.Name, typeof(TOutcome));
        for (var i = _scopes.Count - 1; i >= 0; i--)
        {
            if (_scopes[i].TryGetValue(storageKey, out var raw))
            {
                outcome = (TOutcome)raw;
                return true;
            }
        }

        outcome = default;
        return false;
    }

    public CombatantTargetSelectionContext GetTargetSelectionContext()
    {
        var ctx = BuildContext.TargetSelectionContext;
        return IterationTarget is { } t ? ctx with { IterationTarget = t } : ctx;
    }

    public TriggeredEffectActionBuildContext GetBuildContextWithIterationTarget()
    {
        if (IterationTarget is not { } t)
            return BuildContext;
        var updatedCtx = BuildContext.TargetSelectionContext with { IterationTarget = t };
        return BuildContext with { TargetSelectionContext = updatedCtx };
    }
}
