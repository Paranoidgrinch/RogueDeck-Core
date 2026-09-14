using RogueDeck.Run;
using Xunit.Abstractions;

namespace RogueDeck.Run.Tests;

// ONE ACT, FROM A SPEC AND A SEED (map rework S10).
//
// The four stages have been separately callable since S4; this is the pipeline over them, and the two things
// worth proving about a pipeline are that it always gives the same answer and that it never gives a quiet wrong
// one. An act that promises nothing cannot fail. An act that promises something either keeps it or says, with
// the seed and the numbers in hand, that it could not.
public class StrategicMapGeneratorTests(ITestOutputHelper output)
{
    private static readonly IReadOnlyList<MapLaneProfile> Lanes =
    [
        new("gauntlet", new Dictionary<MapNodeKind, int>
        {
            [MapNodeKind.Combat] = 9,
            [MapNodeKind.Elite] = 4,
            [MapNodeKind.MultiCombat] = 2,
            [MapNodeKind.Event] = 1,
        }),
        new("wilds", new Dictionary<MapNodeKind, int>
        {
            [MapNodeKind.Combat] = 5,
            [MapNodeKind.Event] = 5,
            [MapNodeKind.Elite] = 2,
            [MapNodeKind.Treasure] = 2,
        }),
        new("errands", new Dictionary<MapNodeKind, int>
        {
            [MapNodeKind.Combat] = 3,
            [MapNodeKind.Event] = 2,
            [MapNodeKind.Shop] = 5,
            [MapNodeKind.Rest] = 3,
        }),
    ];

    private static readonly StrategicRoomSpec Rooms = new()
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
    };

    private static StrategicActSpec Act(int rows = 23) => new()
    {
        Rows = rows,
        LaneProfiles = Lanes,
        Rooms = Rooms,
        PathPressure = new PathPressureRules { Minimum = 120 },
        ForkQuality = new ForkQualityRules { MinimumContrast = 40 },
    };

    // THE WHOLE POINT, IN ONE NUMBER: an act that demands budgets, a floor of challenge and forks that decide
    // something is built for every seed asked — most of them on the first attempt, the rest by repair and, where
    // that is not enough, by another whole act from the same run seed.
    [Fact]
    public void Every_seed_builds_an_act_that_keeps_what_it_promised()
    {
        var spec = Act();
        var attempts = new List<int>();
        var repairs = new List<int>();

        for (var seed = 1; seed <= 300; seed++)
        {
            var act = StrategicMapGenerator.Generate(spec, seed);
            Assert.True(act.Clean, act.Render());
            Assert.True(act.Pressure.Held);
            attempts.Add(act.Attempt);
            repairs.Add(act.Repairs.Count);
        }

        output.WriteLine($"300 seeds · first attempt {attempts.Count(attempt => attempt == 0)}"
            + $" · retries {attempts.Sum()} · repairs {repairs.Sum()} ({repairs.Average():F1} per act)");
        Assert.True(attempts.Count(attempt => attempt == 0) >= 270,
            $"only {attempts.Count(attempt => attempt == 0)} of 300 seeds were satisfied on the first attempt");
    }

    [Fact]
    public void The_same_seed_builds_the_same_act()
    {
        var spec = Act();
        Assert.Equal(
            StrategicMapGenerator.Generate(spec, 4711).Render(),
            StrategicMapGenerator.Generate(spec, 4711).Render());

        // And a different attempt is a different act, not the same one twice: the retry is a new family of
        // streams rather than a second roll of the same dice.
        Assert.NotEqual(
            StrategicMapGenerator.Attempt(spec, 4711, 0).Plan.Render(),
            StrategicMapGenerator.Attempt(spec, 4711, 1).Plan.Render());
    }

    // A SPEC NO SEED CAN SATISFY IS REFUSED IMMEDIATELY, in the validator's words rather than as eight failed
    // attempts: "six elites in a twelve-row act" is an answer and "the generator gave up" is not.
    [Fact]
    public void A_spec_no_seed_can_satisfy_is_refused_before_a_seed_is_spent()
    {
        var spec = Act() with
        {
            Rooms = Rooms with
            {
                RoleMinimumDepthPercent = new Dictionary<MapNodeKind, int> { [MapNodeKind.Elite] = 99 },
                RoomBudgets = new Dictionary<MapNodeKind, RoomBudget>
                {
                    [MapNodeKind.Elite] = new() { Min = 9, Target = 9, Max = 9 },
                },
            },
        };

        var refused = Assert.Throws<StrategicMapGenerationException>(() => StrategicMapGenerator.Generate(spec, 1));
        output.WriteLine(refused.Message);

        Assert.Equal(0, refused.Attempts);
        Assert.Null(refused.Best);
        Assert.NotEmpty(refused.Impossible);
        Assert.Contains("No seed can build this act as authored", refused.Message);
    }

    // A SPEC THAT IS MERELY OUT OF REACH fails differently: every attempt is really built, and what comes back
    // is the closest one with the six facts the source document asks for (§25) — the seed, how many acts were
    // tried, what went unkept, what the act held, what its thinnest route was worth, what its forks were worth.
    [Fact]
    public void An_act_that_cannot_be_satisfied_comes_back_with_the_numbers_rather_than_a_shrug()
    {
        var spec = Act() with { PathPressure = new PathPressureRules { Minimum = 330 } };

        var failed = Assert.Throws<StrategicMapGenerationException>(() => StrategicMapGenerator.Generate(spec, 7));
        output.WriteLine(failed.Message);

        Assert.Equal(7, failed.Seed);
        Assert.Equal(spec.MaxGenerationAttempts, failed.Attempts);
        Assert.NotNull(failed.Best);
        Assert.Empty(failed.Impossible);
        Assert.Contains("seed 7", failed.Message);
        Assert.Contains("budgets", failed.Message);
        Assert.Contains("pressure", failed.Message);
        Assert.Contains("forks", failed.Message);
        Assert.True(failed.Best!.Defects.PressureMissing > 0);
    }

    // An act that promises nothing cannot break a promise. The defaults are therefore never a source of
    // exceptions, which is what makes the strictness above safe to have.
    [Fact]
    public void An_act_that_promises_nothing_is_built_on_the_first_attempt_every_time()
    {
        var spec = new StrategicActSpec { Rows = 23, LaneProfiles = Lanes };
        for (var seed = 1; seed <= 100; seed++)
        {
            var act = StrategicMapGenerator.Generate(spec, seed);
            Assert.Equal(0, act.Attempt);
            Assert.Empty(act.Repairs);
            Assert.True(act.Clean);
        }
    }

    // The pipeline hands each stage its OWN stream. Proved where it would hurt: two stages given the same seed
    // would correlate the act's shape with its rooms, which no assertion about a single act could ever show.
    [Fact]
    public void Each_stage_draws_from_its_own_stream()
    {
        var streams = MapSeedStreams.From(20260914);
        var seeds = new[] { streams.Topology, streams.Strands, streams.Rooms, streams.Repair, streams.Content };
        Assert.Equal(seeds.Length, seeds.Distinct().Count());

        var act = StrategicMapGenerator.Attempt(Act(), 20260914, attempt: 0);
        Assert.Equal(streams.Topology, act.Topology.Seed);
    }
}
