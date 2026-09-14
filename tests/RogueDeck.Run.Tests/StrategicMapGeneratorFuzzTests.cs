using System.Diagnostics;
using RogueDeck.Run;
using Xunit.Abstractions;

namespace RogueDeck.Run.Tests;

// THE WHOLE PIPELINE, SWEPT (map rework S10).
//
// The source document asks a procedural generator for volume rather than for examples (§43), and it asks for one
// thing per run: no crash, no invalid act, no broken promise, no drift between two runs of the same seed, and a
// bounded amount of work. That is ONE invariant here —
//
//   EVERY ACT THIS GENERATOR RETURNS KEEPS EVERY PROMISE ITS SPEC MADE, OR IT DOES NOT RETURN AN ACT AT ALL.
//
// — swept over the four BnB act lengths at three levels of demand, from a spec that promises nothing to one whose
// floor of challenge and fork threshold both bite. The distributions it prints are the material S12 authors the
// real numbers from.
public class StrategicMapGeneratorFuzzTests(ITestOutputHelper output)
{
    private const int Seeds = 250;

    private static readonly IReadOnlyList<MapLaneProfile> Lanes =
    [
        Lane("gauntlet", (MapNodeKind.Combat, 9), (MapNodeKind.Elite, 4), (MapNodeKind.MultiCombat, 2), (MapNodeKind.Event, 1), (MapNodeKind.Shop, 0)),
        Lane("wilds", (MapNodeKind.Combat, 5), (MapNodeKind.Event, 5), (MapNodeKind.Elite, 2), (MapNodeKind.Treasure, 2)),
        Lane("errands", (MapNodeKind.Combat, 3), (MapNodeKind.Event, 2), (MapNodeKind.Shop, 5), (MapNodeKind.Rest, 3), (MapNodeKind.Workbench, 2)),
        Lane("hoard", (MapNodeKind.Combat, 4), (MapNodeKind.Treasure, 5), (MapNodeKind.Event, 2), (MapNodeKind.Elite, 1)),
    ];

    private static MapLaneProfile Lane(string name, params (MapNodeKind Kind, int Weight)[] weights) =>
        new(name, weights.ToDictionary(entry => entry.Kind, entry => entry.Weight));

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
            [MapNodeKind.Treasure] = new() { Min = 3, Target = 6, Max = 9 },
        },
        DepthBands =
        [
            new() { StartPercent = 0, EndPercent = 50, Budgets = new Dictionary<MapNodeKind, RoomBudget> { [MapNodeKind.Elite] = new() { Target = 2, Max = 4 } } },
            new() { StartPercent = 50, EndPercent = 100, Budgets = new Dictionary<MapNodeKind, RoomBudget> { [MapNodeKind.Elite] = new() { Min = 2, Target = 3, Max = 5 } } },
        ],
        RoleMinimumDepthPercent = new Dictionary<MapNodeKind, int> { [MapNodeKind.Elite] = 15, [MapNodeKind.Shop] = 10 },
    };

    public static TheoryData<string, int, int, int> Acts()
    {
        var data = new TheoryData<string, int, int, int>();
        foreach (var rows in new[] { 23, 24, 25, 35 })
        {
            data.Add("nothing promised", rows, 0, 0);
            data.Add("a floor of challenge", rows, 120, 0);
            data.Add("challenge and real forks", rows, 120, 40);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Acts))]
    public void Every_act_it_returns_keeps_every_promise_its_spec_made(
        string demand, int rows, int floor, int contrast)
    {
        var spec = new StrategicActSpec
        {
            Rows = rows,
            LaneProfiles = Lanes,
            Rooms = Rooms,
            PathPressure = new PathPressureRules { Minimum = floor },
            ForkQuality = new ForkQualityRules { MinimumContrast = contrast },
        };

        var attempts = 0;
        var repairs = 0;
        var firstTime = 0;
        var pressures = new List<int>();
        var contrasts = new List<int>();
        var elites = new List<int>();
        var clock = Stopwatch.StartNew();

        for (var seed = 1; seed <= Seeds; seed++)
        {
            var act = StrategicMapGenerator.Generate(spec, seed);

            // THE PROMISES. Everything the spec asked for, read off the act that came back rather than trusted.
            Assert.True(act.Clean, act.Render());
            Assert.Empty(act.Plan.Shortfalls);
            Assert.Empty(act.Plan.Forced);
            Assert.True(act.Pressure.Thinnest.Pressure >= floor);
            Assert.All(act.Forks.Forks, fork => Assert.True(fork.Contrast >= contrast));
            foreach (var (kind, budget) in Rooms.RoomBudgets)
                Assert.InRange(act.Plan.Count(kind), budget.Min, budget.Max);

            // THE ACT ITSELF. A repair moves roles; it must never touch the shape, lose a room, or leave a room
            // empty — and the boss rooms are the shape.
            Assert.Equal(act.Topology.NodeCount, act.Plan.Kinds.Count);
            Assert.Equal(rows, act.Topology.Rows.Count);
            Assert.Equal(0, act.Topology.Crossings);
            foreach (var slot in act.Topology.Slots)
                Assert.Equal(slot.IsBoss, act.Plan.KindOf(slot.Id) == MapNodeKind.Boss);

            // THE WORK. Bounded by what the spec allows, which is what keeps a hard act from eating the budget
            // of every act after it.
            Assert.InRange(act.Attempt, 0, spec.MaxGenerationAttempts - 1);
            Assert.InRange(act.Repairs.Count, 0, spec.Repair.MaxRepairPasses);

            attempts += act.Attempt;
            repairs += act.Repairs.Count;
            if (act.Attempt == 0)
                firstTime++;
            pressures.Add(act.Pressure.Thinnest.Pressure);
            if (act.Forks.Count > 0)
                contrasts.Add(act.Forks.Minimum); // an act that never forked has no weakest fork to report
            elites.Add(act.Plan.Count(MapNodeKind.Elite));
        }

        output.WriteLine($"{rows} rows · {demand} · {Seeds} seeds in {clock.ElapsedMilliseconds} ms"
            + $" · first attempt {firstTime} · retries {attempts} · repairs {repairs}"
            + $" · thin route {pressures.Min()}..{pressures.Max()} (mean {pressures.Average():F0})"
            + $" · weakest fork {contrasts.Min()}..{contrasts.Max()} (mean {contrasts.Average():F0})"
            + $" over {contrasts.Count} of {Seeds} acts that forked at all"
            + $" · elites {elites.Min()}..{elites.Max()}");
    }

    // DETERMINISM ACROSS A SWEEP, not just on one seed: the same spec and the same seed give the same act, and
    // that has to hold for the acts the repair touched most — which are exactly the ones it is hardest to hold
    // for, since a hill climb that consulted anything ambient would show up here first.
    [Fact]
    public void The_same_seed_builds_the_same_act_every_time()
    {
        var spec = new StrategicActSpec
        {
            Rows = 25,
            LaneProfiles = Lanes,
            Rooms = Rooms,
            PathPressure = new PathPressureRules { Minimum = 130 },
            ForkQuality = new ForkQualityRules { MinimumContrast = 50 },
        };

        var repaired = 0;
        for (var seed = 1; seed <= 100; seed++)
        {
            var first = StrategicMapGenerator.Generate(spec, seed);
            var second = StrategicMapGenerator.Generate(spec, seed);
            Assert.Equal(first.Render(), second.Render());
            if (first.Repairs.Count > 0)
                repaired++;
        }

        output.WriteLine($"100 seeds · {repaired} of them needed a repair, and all 100 came back identical twice");
        Assert.True(repaired > 0, "no act needed repairing, so determinism under repair went untested");
    }
}
