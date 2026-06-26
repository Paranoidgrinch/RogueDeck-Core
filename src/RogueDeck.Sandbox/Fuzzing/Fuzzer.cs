using RogueDeck.Core.Combat;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Sandbox.Fuzzing;

// One reproducible fuzzer finding: a generated scenario that exposed a real failure. ModelJson can be
// pasted into the sandbox's Import box to reproduce it.
public sealed record FuzzFinding(int Seed, string Kind, string Detail, string ModelJson);

// Generates random scenarios and runs them, collecting failures that indicate engine/harness bugs:
//   - the composer or runner threw an exception (the harness should never throw — it surfaces problems);
//   - a step recorded a "Step threw …" problem (the engine threw while resolving an effect);
//   - the same scenario produced two different final hashes (a determinism violation).
// Expected harness problems (card not in hand, unaffordable play, combat already ended, …) are NOT failures.
public sealed class Fuzzer
{
    public IReadOnlyList<FuzzFinding> Run(int startSeed, int count)
    {
        var findings = new List<FuzzFinding>();

        for (var seed = startSeed; seed < startSeed + count; seed++)
        {
            SandboxModel model;
            try
            {
                model = new RandomScenarioGenerator(seed).Generate();
            }
            catch (Exception ex)
            {
                findings.Add(new FuzzFinding(seed, "generator-threw", $"{ex.GetType().Name}: {ex.Message}", ""));
                continue;
            }

            var json = SafeJson(model);

            try
            {
                var report = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));

                var engineThrew = report.Steps
                    .SelectMany(step => step.Problems)
                    .FirstOrDefault(problem =>
                        problem.Contains("Step threw", StringComparison.Ordinal) && !IsEngineGuard(problem));
                if (engineThrew is not null)
                    findings.Add(new FuzzFinding(seed, "engine-exception", engineThrew, json));

                // Determinism: the same model + seed must produce the same final state.
                var report2 = new ScenarioRunner().Run(new ScenarioComposer().Compose(model));
                var hash1 = CombatStateHasher.ComputeHash(report.FinalState.CreateSnapshot());
                var hash2 = CombatStateHasher.ComputeHash(report2.FinalState.CreateSnapshot());
                if (hash1 != hash2)
                    findings.Add(new FuzzFinding(seed, "nondeterministic", $"{hash1} != {hash2}", json));
            }
            catch (Exception ex)
            {
                var detail = $"{ex.GetType().Name}: {ex.Message}";
                if (!IsEngineGuard(detail)) // genuine escaped fault (a guard escaping resolution is benign)
                    findings.Add(new FuzzFinding(seed, "compose/run-threw", detail, json));
            }
        }

        return findings;
    }

    // The engine's intentional safety valves (queue-cycle cap, ForEach target cap, etc.) halt pathological
    // scenarios gracefully — they are by-design limits, not faults, so they are not fuzzer findings.
    private static bool IsEngineGuard(string problem) =>
        problem.Contains("reaching the limit of", StringComparison.Ordinal) ||
        problem.Contains("exceeds the configured maximum", StringComparison.Ordinal) ||
        problem.Contains("maximum trigger depth", StringComparison.Ordinal) ||
        problem.Contains("maximum repeat count", StringComparison.Ordinal);

    private static string SafeJson(SandboxModel model)
    {
        try { return SandboxModelJson.Export(model); }
        catch { return "(could not serialise model)"; }
    }
}
