namespace RogueDeck.Core.Combat;

public enum CombatDiagnosticSeverity
{
    Error,
    Warning,
}

// Machine-readable diagnostic codes for registry build / preflight failures (master plan §13).
// The numeric RDCP code is derived from the declaration order; do not reorder existing members.
public enum CombatDiagnosticCode
{
    MissingNodeExecutor = 1,          // RDCP001
    MissingRequestHandler = 2,        // RDCP002
    UnsafeSideEffectNode = 3,         // RDCP003
    MissingStatusDefinition = 4,      // RDCP004
    MissingCardDefinition = 5,        // RDCP005
    MissingResourceDefinition = 6,    // RDCP006
    MissingEnemyActionDefinition = 7, // RDCP007
    MissingTemporaryRuleDefinition = 8, // RDCP008
    ResultProducerDuplicate = 9,      // RDCP009
    ResultConsumerMissing = 10,       // RDCP010
    ResultTypeMismatch = 11,          // RDCP011
    ResultScopeEscape = 12,           // RDCP012
    ScalarReadFromMultiTarget = 13,   // RDCP013
    ContextCapabilityMissing = 14,    // RDCP014
    TargetDomainMismatch = 15,        // RDCP015
    OperationEligibilityMismatch = 16, // RDCP016
    InvalidSelectorCardinality = 17,  // RDCP017
    MissingProgramDefinition = 18,    // RDCP018
    InvalidOutcomeCardinality = 19,   // RDCP019
    UnsafeRuntimeRegistry = 20,       // RDCP020
}

// A structured preflight diagnostic. Message preserves the legacy human-readable text; the other
// fields make it machine-readable so authors/tooling can locate and fix invalid content without
// reading engine source.
public sealed record CombatDiagnostic(
    CombatDiagnosticCode Code,
    CombatDiagnosticSeverity Severity,
    string OwnerKind,
    string OwnerId,
    string? ProgramId,
    string? NodePath,
    string Message,
    string? SelectorName = null)
{
    public string CodeString => $"RDCP{(int)Code:000}";

    // The deterministic structural location of the offending node within its program
    // (e.g. root.causal[1].conditional.then). The lexical result scope is this same path.
    public override string ToString() => Message;
}

// Thrown by CombatDefinitionRegistryBuilder.Build when preflight fails. Subclasses
// InvalidOperationException for backward compatibility with existing callers, and exposes the
// structured Diagnostics for tooling.
public sealed class CombatDefinitionBuildException : InvalidOperationException
{
    public IReadOnlyList<CombatDiagnostic> Diagnostics { get; }

    public CombatDefinitionBuildException(IReadOnlyList<CombatDiagnostic> diagnostics)
        : base(FormatMessage(diagnostics))
    {
        Diagnostics = diagnostics;
    }

    private static string FormatMessage(IReadOnlyList<CombatDiagnostic> diagnostics) =>
        $"Registry build failed — {diagnostics.Count} program validation error(s):\n" +
        string.Join("\n", diagnostics.Select(d => $"  {d.CodeString} {d.Message}"));
}
