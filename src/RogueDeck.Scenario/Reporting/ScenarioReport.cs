using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Scenario.Reporting;

// The observed outcome of one scenario step: the turn context it ran in, the actor, the (optional) intent,
// the slice of trace events the step produced, and any problems the runner detected.
public sealed record ScenarioStepReport(
    int Index,
    ScenarioStep Step,
    int Round,
    int Turn,
    CombatantId? Actor,
    ActionIntent? Intent,
    IReadOnlyList<CombatTraceEvent> Trace,
    IReadOnlyList<string> Problems)
{
    public bool HasProblems => Problems.Count > 0;
}

// The full result of running a scenario: every step report plus the final combat result and state.
public sealed class ScenarioReport
{
    public IReadOnlyList<ScenarioStepReport> Steps { get; }
    public CombatResult Result { get; }
    public CombatState FinalState { get; }

    public ScenarioReport(
        IReadOnlyList<ScenarioStepReport> steps,
        CombatResult result,
        CombatState finalState)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(finalState);

        Steps = steps;
        Result = result;
        FinalState = finalState;
    }

    public bool HasProblems => Steps.Any(step => step.HasProblems);
    public IEnumerable<ScenarioStepReport> ProblemSteps => Steps.Where(step => step.HasProblems);
}
