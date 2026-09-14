using RogueDeck.Run;
using Xunit.Abstractions;

namespace RogueDeck.Run.Tests;

// WHAT ONE ROUTE ASKS OF A PLAYER (map rework S8).
//
// This is the promise that replaces the per-path role minimums, so the tests are written against the thing that
// promise is for: the route that walks round everything. A map where the elites sit on one side and the campfires
// on the other satisfies every act-wide budget ever authored and can still be strolled through, and the only
// number that notices is this one.
//
// The exact arithmetic is proved on a hand-drawn act, because on a generated one a plausible score and a correct
// score are indistinguishable. The generated acts are then held to the two claims a DP can get wrong quietly: the
// route it reports is a REAL route through the map, and it is worth exactly what the number says.
public class StrategicPathPressureTests(ITestOutputHelper output)
{
    private static readonly NodeType Mark = new("test.mark");

    private static readonly PathPressureRules Challenge = new()
    {
        KindPressure = new Dictionary<MapNodeKind, int>
        {
            [MapNodeKind.Combat] = 10,
            [MapNodeKind.MultiCombat] = 15,
            [MapNodeKind.Elite] = 25,
        },
    };

    //   r0   C  ?      ← a fight or an errand
    //   r1   E  R      ← the elite, or the campfire that walks round it
    //   r2   B         ← the boss both routes end in
    //
    // Thinnest: ? R B = 0. Richest: C E B = 35. Which is the entire point of the measurement.
    private static GeneratedMap ByHand()
    {
        var roles = new Dictionary<NodeId, MapNodeKind>();
        var builder = new RunMapBuilder();

        void Add(string id, MapNodeKind role)
        {
            builder.AddNode(new NodeId(id), Mark, id);
            roles[new NodeId(id)] = role;
        }

        Add("r0c0", MapNodeKind.Combat);
        Add("r0c1", MapNodeKind.Event);
        Add("r1c0", MapNodeKind.Elite);
        Add("r1c1", MapNodeKind.Rest);
        Add("r2c0", MapNodeKind.Boss);
        builder.Entry("r0c0").Entry("r0c1");
        builder.Connect("r0c0", "r1c0").Connect("r0c0", "r1c1").Connect("r0c1", "r1c1");
        builder.Connect("r1c0", "r2c0").Connect("r1c1", "r2c0");
        return new GeneratedMap(builder.Build(), roles);
    }

    [Fact]
    public void The_route_round_the_elite_is_worth_nothing_and_the_route_through_it_is_worth_both_rooms()
    {
        var report = StrategicPathPressure.Measure(ByHand(), Challenge);

        Assert.Equal(0, report.Thinnest.Pressure);
        Assert.Equal(35, report.Richest.Pressure);
        Assert.Equal("?RB", report.Thinnest.Letters);
        Assert.Equal("CEB", report.Richest.Letters);
        Assert.Equal(new[] { "r0c1", "r1c1", "r2c0" }, report.Thinnest.Rooms.Select(room => room.Value));
        Assert.Equal(new[] { "r0c0", "r1c0", "r2c0" }, report.Richest.Rooms.Select(room => room.Value));
    }

    // A promise is about the floor and nothing else: an act whose richest route is a spike still KEEPS its
    // promise, and an act that allows the spike still reports it. Two bits, measured apart.
    [Fact]
    public void The_floor_is_a_promise_and_the_ceiling_is_only_ever_a_remark()
    {
        var demanding = StrategicPathPressure.Measure(ByHand(), Challenge with { Minimum = 20, Maximum = 30 });

        Assert.False(demanding.Held);
        Assert.Equal(20, demanding.Missing);
        Assert.False(demanding.WithinMaximum);
        Assert.Equal(5, demanding.Excess);

        var content = StrategicPathPressure.Measure(ByHand(), Challenge);
        Assert.True(content.Held);
        Assert.True(content.WithinMaximum);
        Assert.Equal(0, content.Missing);
        Assert.Equal(0, content.Excess);
    }

    // A role nothing weights is worth nothing, so an act of campfires is worth nothing — which is the reading
    // that makes "every route holds two elites" replaceable at all.
    [Fact]
    public void Silence_is_zero_and_an_act_of_campfires_asks_nothing()
    {
        var roles = new Dictionary<NodeId, MapNodeKind>();
        var builder = new RunMapBuilder();
        foreach (var (id, role) in new[]
        {
            ("r0c0", MapNodeKind.Rest), ("r1c0", MapNodeKind.Shop), ("r2c0", MapNodeKind.Workbench),
        })
        {
            builder.AddNode(new NodeId(id), Mark, id);
            roles[new NodeId(id)] = role;
        }
        builder.Connect("r0c0", "r1c0").Connect("r1c0", "r2c0");

        var report = StrategicPathPressure.Measure(new GeneratedMap(builder.Build(), roles), Challenge);

        Assert.Equal(0, report.Thinnest.Pressure);
        Assert.Equal(0, report.Richest.Pressure);
        Assert.Equal(0, report.SpreadPercent);
    }

    // ————— generated acts —————

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

    private static StrategicRoomPlan Plan(int seed, int rows = 23) => StrategicRoomAllocator.Allocate(
        seed, StrategicStrandProfileAssigner.Assign(seed, StrategicTopologyGenerator.Generate(seed, rows), Lanes), Rooms);

    // THE TWO WAYS A REVERSE-TOPOLOGICAL DP GOES WRONG QUIETLY: it reports a score no route achieves, or it
    // reports a "route" that is a set of rooms nobody can walk between. Both are checked here, on every seed.
    [Fact]
    public void Every_reported_route_is_a_route_a_player_can_walk_and_is_worth_what_it_says()
    {
        for (var seed = 1; seed <= 200; seed++)
        {
            var plan = Plan(seed);
            var report = StrategicPathPressure.Measure(plan, Challenge);
            Assert.True(report.Thinnest.Pressure <= report.Richest.Pressure);

            foreach (var route in new[] { report.Thinnest, report.Richest })
            {
                Assert.Contains(route.Rooms[0], plan.Topology.EntryIds);
                Assert.Empty(plan.Topology.SuccessorsOf(route.Rooms[^1]));
                for (var step = 1; step < route.Rooms.Count; step++)
                    Assert.Contains(route.Rooms[step], plan.Topology.SuccessorsOf(route.Rooms[step - 1]));

                Assert.Equal(route.Rooms.Select(plan.KindOf), route.Kinds);
                Assert.Equal(route.Pressure, route.Kinds.Sum(Challenge.PressureOf));
            }
        }
    }

    // A route walks one room per row, so it holds exactly as many rooms as the act has rows — the structural
    // fact the spec validator's upper bound is built on.
    [Fact]
    public void A_route_holds_one_room_per_row()
    {
        foreach (var rows in new[] { 5, 23, 35 })
        {
            var plan = Plan(seed: 7, rows);
            var report = StrategicPathPressure.Measure(plan, Challenge);
            Assert.Equal(rows, report.Thinnest.Rooms.Count);
            Assert.Equal(rows, report.Richest.Rooms.Count);
        }
    }

    [Fact]
    public void The_same_seed_measures_the_same_act_twice()
    {
        Assert.Equal(
            StrategicPathPressure.Measure(Plan(seed: 4711), Challenge).Render(),
            StrategicPathPressure.Measure(Plan(seed: 4711), Challenge).Render());
    }

    // WHAT THE STRATEGIC GENERATOR ACTUALLY PRODUCES, as a number rather than as a hope. Not a threshold anyone
    // has authored yet — S12 does that with the real acts — but the spread is the claim S4 and S6 were building
    // towards: an act whose routes differ is an act where the thin route and the rich one are not the same walk.
    [Fact]
    public void An_act_full_of_forks_has_routes_that_differ()
    {
        var thin = new List<int>();
        var spread = new List<int>();
        for (var seed = 1; seed <= 500; seed++)
        {
            var report = StrategicPathPressure.Measure(Plan(seed), Challenge);
            thin.Add(report.Thinnest.Pressure);
            spread.Add(report.SpreadPercent);
        }

        output.WriteLine($"23-row act, 500 seeds · thinnest {thin.Min()}..{thin.Max()} (mean {thin.Average():F1})"
            + $" · spread {spread.Min()}..{spread.Max()} % (mean {spread.Average():F1} %)");

        // Every act asks SOMETHING of every route — with combat as the filler there is no walk round everything —
        // and the two ends of an act are genuinely different walks rather than the same one twice.
        Assert.True(thin.Min() > 0, "some route through some act was worth nothing at all");
        Assert.True(spread.Average() > 10, $"the routes of an average act differ by only {spread.Average():F1} %");
    }
}
