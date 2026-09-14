using RogueDeck.Run;
using Xunit.Abstractions;

namespace RogueDeck.Run.Tests;

// WHAT A THOUSAND ACTS LOOK LIKE (map rework S13).
//
// Every number this arc authored is a claim about a distribution, and up to here each was checked one act at a
// time — enough to catch a rule that never holds, useless against the ones that hold nine times in ten. The
// instrument that measures the other case has to be trusted before its numbers are, so what is tested here is
// the instrument: the extremes it names are real, the seeds it names produce them, the mean does not depend on
// the machine, and the one bit a build may be failed on is the only bit that ever fails.
public class StrategicActStatisticsTests(ITestOutputHelper output)
{
    private static readonly IReadOnlyList<MapLaneProfile> Lanes =
    [
        new("gauntlet", new Dictionary<MapNodeKind, int>
        {
            [MapNodeKind.Combat] = 9, [MapNodeKind.Elite] = 4, [MapNodeKind.MultiCombat] = 2, [MapNodeKind.Event] = 1,
        }),
        new("wilds", new Dictionary<MapNodeKind, int>
        {
            [MapNodeKind.Combat] = 5, [MapNodeKind.Event] = 5, [MapNodeKind.Elite] = 2, [MapNodeKind.Treasure] = 2,
        }),
        new("errands", new Dictionary<MapNodeKind, int>
        {
            [MapNodeKind.Combat] = 3, [MapNodeKind.Event] = 2, [MapNodeKind.Shop] = 5, [MapNodeKind.Rest] = 3,
        }),
    ];

    private static StrategicActSpec Act(int rows = 23) => new()
    {
        Rows = rows,
        LaneProfiles = Lanes,
        Rooms = new StrategicRoomSpec
        {
            KindWeights = new Dictionary<MapNodeKind, int>
            {
                [MapNodeKind.Combat] = 7,
                [MapNodeKind.Event] = 3,
                [MapNodeKind.Elite] = 2,
                [MapNodeKind.Shop] = 1,
                [MapNodeKind.Rest] = 1,
                [MapNodeKind.Treasure] = 1,
            },
            RoomBudgets = new Dictionary<MapNodeKind, RoomBudget>
            {
                [MapNodeKind.Elite] = new() { Min = 3, Target = 5, Max = 8 },
                [MapNodeKind.Shop] = new() { Min = 4, Target = 5, Max = 7 },
                [MapNodeKind.Rest] = new() { Min = 4, Target = 6, Max = 9 },
            },
        },
    };

    [Fact]
    public void Every_act_is_counted_and_every_role_is_counted_on_every_act()
    {
        var report = StrategicActStatistics.Measure(Act(), firstSeed: 1, seeds: 120);

        Assert.True(report.Sound, report.Render());
        Assert.Equal(120, report.Seeds);
        // Everything but the fork measures is counted on every act. The forks are counted on every act that HAS
        // one, and at these topology weights roughly one act in a hundred never splits — which the report says
        // out loud rather than averaging in as a zero (see ActStatistics.Coverage).
        Assert.All(report.Measures, measure => Assert.True(
            measure.Count == 120 || measure.Name.Contains("fork", StringComparison.Ordinal),
            $"{measure.Name} was measured on {measure.Count} of 120 acts"));
        Assert.All(report.Measures.Where(measure => measure.Name.Contains("fork", StringComparison.Ordinal)),
            measure => Assert.InRange(measure.Count, 100, 120));

        // A role the act can place is counted on EVERY act, zero included — a role first seen on the fiftieth
        // act would report a "least" taken over the other seventy and never the zero it had before that.
        Assert.All(report.Rooms.Values, measure => Assert.Equal(120, measure.Count));
        foreach (var kind in new[]
                 {
                     MapNodeKind.Combat, MapNodeKind.Event, MapNodeKind.Elite, MapNodeKind.Shop,
                     MapNodeKind.Rest, MapNodeKind.Treasure, MapNodeKind.Boss,
                 })
            Assert.True(report.Rooms.ContainsKey(kind), $"{kind} is never measured");
    }

    // THE CLAIM THE WHOLE REPORT RESTS ON: a named outlier seed is a map you can open. If the seed does not
    // actually produce the extreme, the report is worse than no report — it sends a reader to the wrong act.
    [Fact]
    public void The_seed_a_report_names_really_does_produce_the_extreme_it_is_named_for()
    {
        var spec = Act();
        var report = StrategicActStatistics.Measure(spec, firstSeed: 1, seeds: 80);

        var rooms = report.Measures.Single(measure => measure.Name == "rooms");
        Assert.Equal(rooms.Least, StrategicMapGenerator.Generate(spec, rooms.LeastSeed).Topology.NodeCount);
        Assert.Equal(rooms.Most, StrategicMapGenerator.Generate(spec, rooms.MostSeed).Topology.NodeCount);

        var elites = report.Rooms[MapNodeKind.Elite];
        Assert.Equal(elites.Least, StrategicMapGenerator.Generate(spec, elites.LeastSeed).Plan.Count(MapNodeKind.Elite));
        Assert.Equal(elites.Most, StrategicMapGenerator.Generate(spec, elites.MostSeed).Plan.Count(MapNodeKind.Elite));

        var thin = report.Measures.Single(measure => measure.Name == "thinnest route");
        Assert.Equal(thin.Least, StrategicMapGenerator.Generate(spec, thin.LeastSeed).Pressure.Thinnest.Pressure);
    }

    // An average is a number two machines have to agree about, so it is kept in tenths and computed with
    // integers — the same argument StrategicRoomRules makes about its percentages.
    [Fact]
    public void The_same_seeds_report_the_same_numbers_twice()
    {
        Assert.Equal(
            StrategicActStatistics.Measure(Act(), firstSeed: 500, seeds: 60).Render(),
            StrategicActStatistics.Measure(Act(), firstSeed: 500, seeds: 60).Render());

        var measure = new ActMeasure("thirds");
        measure.Add(1, 1);
        measure.Add(2, 1);
        measure.Add(3, 2);
        // 4 / 3 = 1.333…, and the report says 1.3 rather than whatever the last double rounded to.
        Assert.Equal(13, measure.MeanTenths);
        Assert.Equal(1, measure.Least);
        Assert.Equal(2, measure.Most);
        Assert.Equal(3, measure.MostSeed);
    }

    // A SPEC THE GENERATOR REFUSES IS REPORTED, NOT THROWN. The report exists to be read about specs that are
    // wrong; one that fell over on the first bad seed would only ever describe specs nobody needed to look at.
    [Fact]
    public void A_spec_that_cannot_be_satisfied_is_reported_with_the_seeds_that_show_it()
    {
        var impossible = Act() with
        {
            // More challenge than a 23-room act of these roles could hold if every room were an elite.
            PathPressure = new PathPressureRules { Minimum = 10_000 },
        };

        var report = StrategicActStatistics.Measure(impossible, firstSeed: 1, seeds: 5);

        Assert.False(report.Sound);
        Assert.Equal(5, report.Refused.Count);
        Assert.Equal([1, 2, 3, 4, 5], report.Refused.Select(refusal => refusal.Seed));
        // Refused before a seed was spent on it, so there is no best attempt to be close about.
        Assert.All(report.Refused, refusal => Assert.Equal(0, refusal.Attempts));
        Assert.All(report.Refused, refusal => Assert.Null(refusal.Best));
        Assert.Contains("NOT SOUND", report.Render(), StringComparison.Ordinal);
        output.WriteLine(report.Render());
    }

    // …and a spec that is merely far too tight — satisfiable on paper, unreachable in practice — is refused per
    // seed, after the retries, with the best attempt's defects saying how close it came. That is the difference
    // between a spec that is wrong and a spec that wants one number moved.
    [Fact]
    public void A_spec_that_is_only_just_out_of_reach_says_how_close_it_came()
    {
        var tight = Act(rows: 8) with
        {
            LaneProfiles =
            [
                new("mixed", new Dictionary<MapNodeKind, int> { [MapNodeKind.Combat] = 1, [MapNodeKind.Event] = 1 }),
            ],
            Rooms = new StrategicRoomSpec
            {
                KindWeights = new Dictionary<MapNodeKind, int>
                {
                    [MapNodeKind.Combat] = 1,
                    [MapNodeKind.Event] = 1,
                },
                RoomBudgets = new Dictionary<MapNodeKind, RoomBudget>
                {
                    [MapNodeKind.Event] = new() { Min = 3, Target = 3, Max = 3 },
                },
            },
            // Seven rooms before the boss, and three of them must be doors: the richest route a spec could
            // permit is seven fights, so this is not IMPOSSIBLE — it is merely unreachable once the act's own
            // budget has taken three of the rooms away.
            PathPressure = new PathPressureRules { Minimum = 70 },
        };

        var report = StrategicActStatistics.Measure(tight, firstSeed: 1, seeds: 5);

        Assert.False(report.Sound);
        Assert.NotEmpty(report.Refused);
        Assert.All(report.Refused, refusal => Assert.True(refusal.Attempts > 0,
            "a spec that is only unreachable should be tried before it is refused"));
        Assert.All(report.Refused, refusal =>
            Assert.True(refusal.Best is { PressureMissing: > 0 },
                $"seed {refusal.Seed} was refused without saying how much challenge it was short of"));
        output.WriteLine(report.Render());
    }

    // The quality numbers may never fail anything, and the way to say that in a test is to show an act whose
    // quality is dreadful and whose report is still sound.
    [Fact]
    public void An_act_with_nothing_to_say_for_itself_is_still_sound()
    {
        var dull = new StrategicActSpec
        {
            Rows = 6,
            MinWidth = 1,
            MaxWidth = 1,
            LaneProfiles = [new("one", new Dictionary<MapNodeKind, int> { [MapNodeKind.Combat] = 1 })],
            Rooms = new StrategicRoomSpec
            {
                KindWeights = new Dictionary<MapNodeKind, int> { [MapNodeKind.Combat] = 1 },
            },
        };

        var report = StrategicActStatistics.Measure(dull, firstSeed: 1, seeds: 20);

        Assert.True(report.Sound, report.Render());
        // One room wide is one route, so there are no forks at all — and the fork measures say so by covering
        // no acts rather than by averaging a zero nobody measured.
        Assert.Equal(0, report.Measures.Single(measure => measure.Name == "weakest fork").Count);
        Assert.Contains("[over 0 act(s)]", report.Render(), StringComparison.Ordinal);
        Assert.Equal(0, report.Measures.Single(measure => measure.Name == "route spread %").Most);
    }

    // A measure that is true of every act ever generated is a measure nobody can use. The narrowest row was
    // one: an act ends on a single boss room however wide it ran, so counting the boss rows made every act
    // 1 wide at its narrowest and the line said nothing at all.
    [Fact]
    public void An_acts_width_is_measured_over_the_rows_the_player_chooses_in()
    {
        var report = StrategicActStatistics.Measure(Act(), firstSeed: 1, seeds: 60);
        var narrowest = report.Measures.Single(measure => measure.Name == "narrowest row");
        var widest = report.Measures.Single(measure => measure.Name == "widest row");

        Assert.InRange(narrowest.Least, Act().MinWidth, Act().MaxWidth);
        Assert.InRange(widest.Most, Act().MinWidth, Act().MaxWidth);
        Assert.True(narrowest.Least >= 2, "the boss row is being counted as the act's narrowest");
    }

    // The report a human actually reads, printed once so a change to it is a change somebody saw.
    [Fact]
    public void A_report_reads_as_a_report()
    {
        var report = StrategicActStatistics.Measure(Act(), firstSeed: 1, seeds: 200);
        output.WriteLine(report.Render());

        Assert.Contains("sound", report.Render(), StringComparison.Ordinal);
        Assert.Contains("rooms", report.Render(), StringComparison.Ordinal);
        Assert.Contains("budget 3..8, aims at 5", report.Render(), StringComparison.Ordinal);
        Assert.Contains("none — the filler", report.Render(), StringComparison.Ordinal);
    }
}
