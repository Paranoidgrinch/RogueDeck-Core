using System.Text;
using RogueDeck.Sandbox.Fuzzing;

namespace RogueDeck.Sandbox.Tests;

public class FuzzTests
{
    // Generate and run a batch of random scenarios; the engine/harness must never throw, must stay
    // deterministic, and must never let an effect throw mid-resolution. Any finding fails the test with a
    // reproducible seed + the model JSON (paste into the sandbox's Import box to inspect it).
    [Fact]
    public void RandomScenarios_RunWithoutEngineFaults_AndAreDeterministic()
    {
        var findings = new Fuzzer().Run(startSeed: 0, count: 300);

        if (findings.Count > 0)
        {
            var report = new StringBuilder($"Fuzzer found {findings.Count} issue(s):\n");
            foreach (var finding in findings.Take(5))
            {
                report.AppendLine($"  seed {finding.Seed} [{finding.Kind}]: {finding.Detail}");
                report.AppendLine($"  model: {finding.ModelJson}");
            }
            Assert.Fail(report.ToString());
        }
    }
}
