using RogueDeck.Run;
using Xunit.Abstractions;

namespace RogueDeck.Run.Tests;

// THE FLAVOUR ASSIGNMENT, SWEPT (map rework S5).
//
// StrategicStrandProfileTests states each rule on a handful of acts. This file runs the same invariants over
// every BnB act length, every sensible profile count and every rule extreme, because the assignment is a walk
// over a generated shape and the interesting cases are the ones nobody thought to write down: an act that never
// forks, one that merges in its second row, an act with one authored lane, an act with six.
//
// Two invariants carry the whole step: every room resolves to a profile, and a profile begins only where a route
// was born or where a second route arrived. Everything else is tuning.
public class StrategicStrandProfileFuzzTests(ITestOutputHelper output)
{
    private static readonly IReadOnlyList<MapLaneProfile> Lanes =
    [
        Lane("gauntlet", MapNodeKind.Combat),
        Lane("wilds", MapNodeKind.Event),
        Lane("errands", MapNodeKind.Shop),
        Lane("hoard", MapNodeKind.Treasure),
    ];

    private static MapLaneProfile Lane(string name, MapNodeKind kind) =>
        new(name, new Dictionary<MapNodeKind, int> { [kind] = 7 });

    [Theory]
    [InlineData(4)]
    [InlineData(12)]
    [InlineData(23)]    // Act I
    [InlineData(24)]    // Act II
    [InlineData(25)]    // Act III
    [InlineData(35)]    // Act IV
    public void Two_thousand_acts_of_this_length_are_flavoured_without_a_gap(int rows)
    {
        const int seeds = 2000;
        var spans = 0;
        var branched = 0;
        var inherited = 0;
        var actsWithAHandover = 0;
        var flavoursUsed = new int[Lanes.Count];

        for (var seed = 1; seed <= seeds; seed++)
        {
            var topology = StrategicTopologyGenerator.Generate(seed, rows);
            var profiles = StrategicStrandProfileAssigner.Assign(seed, topology, Lanes);

            Check(seed, topology, profiles);

            spans += profiles.Spans.Count;
            branched += profiles.Branched;
            inherited += profiles.Inherited;
            if (profiles.Inherited > 0)
                actsWithAHandover++;
            foreach (var slot in topology.Slots)
                flavoursUsed[profiles.IndexOf(slot.Id)]++;
        }

        output.WriteLine($"rows {rows}: {spans / (double)seeds:0.00} spans/act · {branched / (double)seeds:0.00} "
            + $"branch flavours · {inherited / (double)seeds:0.00} handovers · {actsWithAHandover} of {seeds} acts "
            + $"had one · rooms per flavour {string.Join("/", flavoursUsed)}");

        // Every authored flavour has to actually get used across a few thousand acts, or it is authoring nobody
        // sees: a bucket that is never drawn from is a defect, not a taste.
        Assert.All(flavoursUsed, count => Assert.True(count > 0, "an authored flavour was never used"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(6)]
    public void A_thousand_acts_are_flavoured_with_this_many_authored_profiles(int count)
    {
        var authored = Enumerable.Range(0, count)
            .Select(index => Lane($"lane{index}", MapNodeKind.Combat))
            .ToList();

        for (var seed = 1; seed <= 1000; seed++)
        {
            var topology = StrategicTopologyGenerator.Generate(seed, rows: 23);
            var profiles = StrategicStrandProfileAssigner.Assign(seed, topology, authored);

            Check(seed, topology, profiles);
            Assert.All(topology.Slots, slot => Assert.InRange(profiles.IndexOf(slot.Id), 0, count - 1));
        }
    }

    [Theory]
    [InlineData(0, 0, 0, 0, 0)]     // every weight off: the documented fallback, any flavour but the parent's
    [InlineData(1, 0, 0, 1, 0)]     // a fork's sides always agree; a convergence always keeps the older
    [InlineData(0, 1, 0, 0, 1)]     // a fork always steps next door; a convergence always takes the younger
    [InlineData(0, 0, 1, 1, 1)]     // a fork always jumps far; a convergence is a coin toss
    [InlineData(9, 9, 9, 9, 9)]     // everything equally likely
    public void A_thousand_acts_are_flavoured_under_these_rules(
        int same, int adjacent, int distant, int older, int younger)
    {
        var rules = new StrategicStrandProfileRules
        {
            SameProfileWeight = same,
            AdjacentProfileWeight = adjacent,
            DistantProfileWeight = distant,
            OlderProfileWeight = older,
            YoungerProfileWeight = younger,
        };

        for (var seed = 1; seed <= 1000; seed++)
        {
            var topology = StrategicTopologyGenerator.Generate(seed, rows: 23);
            var profiles = StrategicStrandProfileAssigner.Assign(seed, topology, Lanes, rules);

            Check(seed, topology, profiles);
        }
    }

    [Fact]
    public void Two_thousand_acts_are_each_flavoured_identically_the_second_time()
    {
        for (var seed = 1; seed <= 2000; seed++)
        {
            var topology = StrategicTopologyGenerator.Generate(seed, rows: 24);

            Assert.Equal(
                StrategicStrandProfileAssigner.Assign(seed, topology, Lanes).Render(),
                StrategicStrandProfileAssigner.Assign(seed, topology, Lanes).Render());
        }
    }

    [Fact]
    public void One_act_read_out_loud()
    {
        var topology = StrategicTopologyGenerator.Generate(seed: 20260911, rows: 23);
        output.WriteLine(topology.Render());
        output.WriteLine(StrategicStrandProfileAssigner.Assign(20260911, topology, Lanes).Render());
    }

    // The two invariants, on one act.
    private static void Check(int seed, StrategicTopology topology, StrategicStrandProfiles profiles)
    {
        foreach (var slot in topology.Slots)
            Assert.True(profiles.TryIndexOf(slot.Strand, slot.Row, out _),
                $"seed {seed}: room {slot.Id.Value} on strand {slot.Strand} has no flavour");

        var born = topology.Strands.ToDictionary(strand => strand.Id, strand => strand.BornRow);
        foreach (var span in profiles.Spans)
        {
            if (span.FromRow == born[span.Strand])
                continue;

            var room = topology.Rows[span.FromRow].Slots.Single(slot => slot.Strand == span.Strand);
            var arriving = topology.PredecessorsOf(room.Id).Select(id => topology.Slot(id).Strand).Distinct().Count();
            Assert.True(arriving > 1,
                $"seed {seed}: strand {span.Strand} changed flavour in row {span.FromRow} for no reason");
        }
    }
}
