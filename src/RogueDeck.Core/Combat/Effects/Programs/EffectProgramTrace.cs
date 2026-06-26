namespace RogueDeck.Core.Combat;

// ── Trace event kind ─────────────────────────────────────────────────────────

public enum EffectProgramTraceEventKind
{
    ProgramStarted,
    NodeEntered,
    ScopeOpened,
    ScopeClosed,
    LimitExceeded,
    ProgramCompleted,
    ProgramCancelled,
    ProgramFaulted,
}

// ── Trace event (value type — no heap allocation on the hot path) ─────────────

public readonly struct EffectProgramTraceEvent
{
    public EffectProgramTraceEventKind Kind { get; init; }
    public EffectProgramId ProgramId { get; init; }
    public string? NodeTypeName { get; init; }
    public int ScopeDepth { get; init; }
    public string? Detail { get; init; }

    // ── Deterministic identities ─────────────────────────────────────────────
    // Pin every trace event to the invocation, lexical node, scope, and effect chain it
    // belongs to, so a trace fully explains "what ran where" and can be compared across runs.

    /// <summary>The per-invocation execution this event belongs to.</summary>
    public EffectProgramExecutionId ExecutionId { get; init; }

    /// <summary>The structural path from the program root to the node, or null when not node-scoped.</summary>
    public string? NodePath { get; init; }

    /// <summary>The lexical result scope active when the event was emitted, or null when not applicable.</summary>
    public long? ScopeId { get; init; }

    /// <summary>The effect chain the execution is running in, or null when no chain is bound.</summary>
    public long? ChainId { get; init; }
}

// ── Sink interface ────────────────────────────────────────────────────────────

public interface IEffectProgramTraceSink
{
    void Record(in EffectProgramTraceEvent traceEvent);
}

// ── Null sink (no-op default) ─────────────────────────────────────────────────

public sealed class NullEffectProgramTraceSink : IEffectProgramTraceSink
{
    public static readonly NullEffectProgramTraceSink Instance = new();
    private NullEffectProgramTraceSink() { }
    public void Record(in EffectProgramTraceEvent traceEvent) { }
}

// ── Recording sink (for tests) ────────────────────────────────────────────────

public sealed class RecordingEffectProgramTraceSink : IEffectProgramTraceSink
{
    private readonly List<EffectProgramTraceEvent> _events = [];

    public IReadOnlyList<EffectProgramTraceEvent> Events => _events;

    public void Record(in EffectProgramTraceEvent traceEvent) => _events.Add(traceEvent);

    public IReadOnlyList<EffectProgramTraceEvent> EventsOfKind(EffectProgramTraceEventKind kind) =>
        _events.Where(e => e.Kind == kind).ToList();
}
