using System.Diagnostics;
using RogueDeck.Run;
using Xunit.Abstractions;

namespace RogueDeck.Run.Tests;

// THE S4 ACCEPTANCE CRITERION, RUN AS A TEST (map rework S4).
//
// The plan asks for 10 000 seeds at every supported act length, each one checked for: no cycle, every room
// reachable, every room reaching the boss, width inside its bounds, no crossing edge, no branch absorbed before
// its minimum lifetime, and the same shape from the same seed. StrategicTopologyValidator is all of those
// questions; this is the sample it is asked over.
//
// It is here rather than in a throwaway probe because the generator's guarantees are made BY CONSTRUCTION, and
// the whole hazard of that is a later change that quietly breaks one of them on one seed in fifty thousand. The
// walk is cheap (no retries, no path enumeration), so the whole sweep costs a second or two — affordable as a
// permanent gate, which a 10 000-seed promise in a document is not.
public class StrategicTopologyFuzzTests(ITestOutputHelper output)
{
    private const int Seeds = 10_000;

    [Theory]
    [InlineData(4)]    // the shortest act whose opening branches can live the default minimum before the boss
    [InlineData(5)]
    [InlineData(12)]
    [InlineData(23)]   // BnB Act I
    [InlineData(24)]   // Act II
    [InlineData(25)]   // Act III
    [InlineData(35)]   // Act IV
    public void Ten_thousand_acts_of_this_length_are_all_sound(int rows)
    {
        var rules = new StrategicTopologyRules();
        var failures = new List<string>();
        var forks = 0;
        var merges = 0;
        var crossings = 0;
        var strands = 0;
        var widest = 0;
        var narrowest = int.MaxValue;
        var shortestBranch = int.MaxValue;
        var watch = Stopwatch.StartNew();

        for (var seed = 1; seed <= Seeds; seed++)
        {
            var topology = StrategicTopologyGenerator.Generate(seed, rows, 1, 2, 4, rules);
            var problems = StrategicTopologyValidator.Validate(topology, rows, 1, 2, 4, rules);
            if (problems.Count > 0 && failures.Count < 5)
                failures.Add($"seed {seed}: {string.Join(" / ", problems)}");

            forks += topology.Forks;
            merges += topology.Merges;
            crossings += topology.Crossings;
            strands += topology.Strands.Count;
            widest = Math.Max(widest, topology.Widths.Max());
            narrowest = Math.Min(narrowest, topology.Widths.SkipLast(1).Min());
            foreach (var strand in topology.Strands.Where(strand => strand.LastRow < rows - 1))
                shortestBranch = Math.Min(shortestBranch, strand.LifeRows);
        }

        // The numbers the later steps will be tuned against — S13 turns this into a real report. Printed rather
        // than asserted, apart from the two that are promises: nothing crosses, and no branch is a detour.
        output.WriteLine($"{rows} rows × {Seeds} seeds in {watch.ElapsedMilliseconds} ms");
        output.WriteLine($"  forks {forks / (double)Seeds:F2}/act · merges {merges / (double)Seeds:F2}/act "
            + $"· strands {strands / (double)Seeds:F2}/act");
        output.WriteLine($"  widths {narrowest}..{widest} · crossings {crossings} "
            + $"· shortest absorbed branch {(shortestBranch == int.MaxValue ? "—" : shortestBranch)} rows");

        Assert.Empty(failures);
        Assert.Equal(0, crossings);
        if (shortestBranch != int.MaxValue)
            Assert.True(shortestBranch >= rules.MinBranchLifeRows,
                $"a branch lived {shortestBranch} rows, short of {rules.MinBranchLifeRows}");
    }

    [Fact]
    public void Ten_thousand_acts_are_each_identical_the_second_time()
    {
        // Determinism over the same sample, because it is the promise a seed IS: a bug report's seed has to
        // reproduce the map it was about, and every later stage draws from this shape.
        for (var seed = 1; seed <= Seeds; seed++)
            Assert.Equal(
                StrategicTopologyGenerator.Generate(seed, 23).Render(),
                StrategicTopologyGenerator.Generate(seed, 23).Render());
    }

    [Theory]
    [InlineData(2)]   // one room row, then the boss
    [InlineData(3)]
    public void A_thousand_acts_shorter_than_any_branch_rule_are_sound_without_one(int rows)
    {
        // The acts too short for the default three-row branch minimum: legal only with the rule switched off,
        // and then they have to be sound like any other act. The refusal itself is pinned below.
        var rules = new StrategicTopologyRules { MinBranchLifeRows = 0 };
        foreach (var seed in Enumerable.Range(1, 1_000))
        {
            var topology = StrategicTopologyGenerator.Generate(seed, rows, 1, 2, 4, rules);
            var problems = StrategicTopologyValidator.Validate(topology, rows, 1, 2, 4, rules);
            Assert.True(problems.Count == 0, $"seed {seed} at {rows} rows: {string.Join(" / ", problems)}");
        }
    }

    [Fact]
    public void An_act_too_short_for_its_own_branch_rule_is_refused()
    {
        // The two promises are incompatible here, so the generator names the conflict instead of producing a
        // shape the validator would then fail. A gauntlet act has no branches and stays legal.
        var rules = new StrategicTopologyRules();
        var refused = Assert.Throws<ArgumentException>(
            () => StrategicTopologyGenerator.Generate(1, 3, 1, 2, 4, rules));
        Assert.Contains("3 rows a branch must live", refused.Message);

        Assert.Empty(StrategicTopologyValidator.Validate(
            StrategicTopologyGenerator.Generate(1, 3, 3, 2, 4, rules), 3, 3, 2, 4, rules));
        Assert.Empty(StrategicTopologyValidator.Validate(
            StrategicTopologyGenerator.Generate(1, 3, 1, 1, 4, rules), 3, 1, 1, 4, rules));
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(1, 4)]
    [InlineData(2, 2)]
    [InlineData(2, 6)]
    [InlineData(3, 4)]
    public void A_thousand_acts_are_sound_at_this_width_too(int minWidth, int maxWidth)
    {
        // The act lengths above all run at BnB's 2..4. The bounds are configuration, so they get their own sweep:
        // a funnel-capable act (min 1), a corridor with no room to branch (2..2), and one wider than BnB's.
        var rules = new StrategicTopologyRules();
        foreach (var seed in Enumerable.Range(1, 1_000))
        {
            var topology = StrategicTopologyGenerator.Generate(seed, 23, 1, minWidth, maxWidth, rules);
            var problems = StrategicTopologyValidator.Validate(topology, 23, 1, minWidth, maxWidth, rules);
            Assert.True(problems.Count == 0, $"seed {seed} at {minWidth}..{maxWidth}: {string.Join(" / ", problems)}");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(9)]
    public void A_thousand_acts_are_sound_at_this_minimum_branch_lifetime(int minBranchLifeRows)
    {
        // The lifetime rule feeds back into which splits are legal at all — a long minimum forbids late forks —
        // so the tuning range gets swept rather than trusted. Nine rows is deliberately absurd for a 23-row act:
        // the shape has to stay sound when almost nothing is allowed to fork.
        var rules = new StrategicTopologyRules { MinBranchLifeRows = minBranchLifeRows };
        foreach (var seed in Enumerable.Range(1, 1_000))
        {
            var topology = StrategicTopologyGenerator.Generate(seed, 23, 1, 2, 4, rules);
            var problems = StrategicTopologyValidator.Validate(topology, 23, 1, 2, 4, rules);
            Assert.True(problems.Count == 0, $"seed {seed} at life {minBranchLifeRows}: {string.Join(" / ", problems)}");
        }
    }
}
