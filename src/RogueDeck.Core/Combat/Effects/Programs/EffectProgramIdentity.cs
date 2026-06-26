namespace RogueDeck.Core.Combat;

// ── Execution state ───────────────────────────────────────────────────────────

public enum EffectProgramExecutionState
{
    Created,
    Running,
    WaitingForNativeOperation,
    WaitingForReactionBoundary,
    Completed,
    Cancelled,
    Faulted,
}

// ── Execution frame ───────────────────────────────────────────────────────────

/// <summary>
/// Non-generic view of an Effect Program execution frame. Lets <see cref="CombatState"/>
/// track in-flight frames of any context type so they can be cancelled when combat ends.
/// </summary>
public interface IEffectProgramExecutionFrame
{
    EffectProgramId ProgramId { get; }
    EffectProgramExecutionId ExecutionId { get; }
    EffectProgramExecutionState State { get; }
    bool IsTerminal { get; }
    Exception? FaultException { get; }

    /// <summary>
    /// Move a still-running frame to <see cref="EffectProgramExecutionState.Cancelled"/>
    /// because combat reached a terminal result. Idempotent: a no-op on an already-terminal
    /// frame, guaranteeing exactly one terminal state per frame.
    /// </summary>
    void CancelDueToCombatEnd();

    /// <summary>
    /// Fault a still-running frame because a native request it enqueued threw during queue
    /// resolution (outside the node-dispatch try/catch). No-op if already terminal, preserving
    /// the exactly-one-terminal-state guarantee. Emits the ProgramFaulted trace and runs the
    /// frame's terminal cleanup (e.g. card-play fault placement).
    /// </summary>
    void FaultDueToNativeHandler(Exception exception);
}

/// <summary>
/// Represents one invocation of an Effect Program. Every call to
/// EffectProgramExecutor.Execute creates a fresh frame with a unique
/// ExecutionId, independent of the program definition.
///
/// The frame is the authoritative owner of per-invocation runtime state:
/// execution identity, lifecycle state, and fault information. Result storage,
/// source context, and target-selection context remain on EffectExecutionContext
/// so that callers can read outcomes after the program completes.
///
/// The lifecycle state machine guarantees exactly one terminal state
/// (Completed | Cancelled | Faulted): once terminal, the only idempotent transition is
/// <see cref="CancelDueToCombatEnd"/> (no-op); the strict Mark* transitions throw.
/// Each terminal transition emits its trace event through the execution context's sink.
/// </summary>
public sealed class EffectProgramExecutionFrame<TContext> : IEffectProgramExecutionFrame
    where TContext : class
{
    public EffectProgramId ProgramId { get; }
    public EffectProgramExecutionId ExecutionId { get; }
    public EffectExecutionContext<TContext> ExecutionContext { get; }

    public EffectProgramExecutionState State { get; private set; } = EffectProgramExecutionState.Created;
    public Exception? FaultException { get; private set; }

    private CombatState? _cleanupCombat;
    private Action<EffectProgramExecutionState, CombatState>? _onTerminal;
    private bool _terminalCleanupRan;

    public bool IsTerminal =>
        State is EffectProgramExecutionState.Completed
              or EffectProgramExecutionState.Cancelled
              or EffectProgramExecutionState.Faulted;

    internal EffectProgramExecutionFrame(
        EffectProgramId programId,
        EffectProgramExecutionId executionId,
        EffectExecutionContext<TContext> executionContext)
    {
        ArgumentNullException.ThrowIfNull(executionContext);
        ProgramId = programId;
        ExecutionId = executionId;
        ExecutionContext = executionContext;
    }

    /// <summary>
    /// Register a cleanup callback invoked exactly once when the frame reaches any terminal
    /// state, receiving that state so the caller can act differently for Completed / Cancelled
    /// / Faulted (e.g. card-play zone placement after a faulted program).
    /// </summary>
    internal void ConfigureTerminalCleanup(
        CombatState combat,
        Action<EffectProgramExecutionState, CombatState>? onTerminal)
    {
        _cleanupCombat = combat;
        _onTerminal = onTerminal;
    }

    private void RunTerminalCleanup()
    {
        if (_terminalCleanupRan)
            return;
        _terminalCleanupRan = true;
        if (_onTerminal is not null && _cleanupCombat is not null)
            _onTerminal(State, _cleanupCombat);
    }

    internal void MarkRunning()
    {
        if (State != EffectProgramExecutionState.Created)
            throw new InvalidOperationException(
                $"Cannot transition to Running from state '{State}'.");
        State = EffectProgramExecutionState.Running;
    }

    internal void MarkCompleted()
    {
        if (IsTerminal)
            throw new InvalidOperationException(
                $"Cannot transition to Completed from terminal state '{State}'.");
        State = EffectProgramExecutionState.Completed;
        EmitTerminalTrace(EffectProgramTraceEventKind.ProgramCompleted, detail: null);
        RunTerminalCleanup();
    }

    internal void MarkCancelled()
    {
        if (State is EffectProgramExecutionState.Cancelled)
            return;
        if (State is EffectProgramExecutionState.Completed
                  or EffectProgramExecutionState.Faulted)
            throw new InvalidOperationException(
                $"Cannot transition to Cancelled from terminal state '{State}'.");
        State = EffectProgramExecutionState.Cancelled;
        EmitTerminalTrace(EffectProgramTraceEventKind.ProgramCancelled, detail: null);
        RunTerminalCleanup();
    }

    internal void MarkFaulted(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        if (State == EffectProgramExecutionState.Completed)
            throw new InvalidOperationException(
                "Cannot transition to Faulted from Completed state.");
        FaultException = ex;
        State = EffectProgramExecutionState.Faulted;
        EmitTerminalTrace(EffectProgramTraceEventKind.ProgramFaulted, detail: ex.GetType().Name);
        RunTerminalCleanup();
    }

    public void FaultDueToNativeHandler(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (IsTerminal)
            return;
        MarkFaulted(exception);
    }

    public void CancelDueToCombatEnd()
    {
        if (IsTerminal)
            return;
        State = EffectProgramExecutionState.Cancelled;
        EmitTerminalTrace(EffectProgramTraceEventKind.ProgramCancelled, detail: "combat ended");
        RunTerminalCleanup();
    }

    private void EmitTerminalTrace(EffectProgramTraceEventKind kind, string? detail) =>
        ExecutionContext.TraceSink.Record(new EffectProgramTraceEvent
        {
            Kind = kind,
            ProgramId = ProgramId,
            Detail = detail,
            ExecutionId = ExecutionId,
            ChainId = ExecutionContext.EffectChain?.Id.Value,
        });
}

// ── Program definition ID ─────────────────────────────────────────────────────

/// <summary>
/// Stable ID for an Effect Program definition.
/// Used in diagnostics, traces, and exceptions to identify which program
/// caused a problem, independent of runtime object identity.
/// </summary>
public readonly record struct EffectProgramId(string Value)
{
    public string Value { get; init; } =
        string.IsNullOrWhiteSpace(Value)
            ? throw new ArgumentException("Effect program ID must not be empty or whitespace.", nameof(Value))
            : Value;

    public override string ToString() => Value;
}

// ── Per-invocation execution ID ───────────────────────────────────────────────

/// <summary>
/// Unique deterministic ID for one Effect Program invocation within a combat.
/// Allocated by CombatState via a monotonically increasing counter.
/// </summary>
public readonly record struct EffectProgramExecutionId(long Value)
{
    public override string ToString() => Value.ToString();
}

// ── Node path ─────────────────────────────────────────────────────────────────

/// <summary>
/// Stable structural path from the program root to a specific node.
///
/// Examples:
///   root
///   root.causal[0]
///   root.causal[1].conditional.then
///   root.causal[2].forEach.body.causal[0]
///
/// Paths are built from node-supplied child path segments via
/// IEffectNode.GetChildPathSegment(childIndex). They are independent of
/// runtime object hash codes and deterministic for the same program definition.
/// </summary>
public readonly record struct EffectProgramNodePath(string Value)
{
    public static readonly EffectProgramNodePath Root = new("root");

    public string Value { get; init; } = Value ?? "root";

    public EffectProgramNodePath Child(string segment) =>
        new($"{Value}.{segment}");

    public override string ToString() => Value;
}
