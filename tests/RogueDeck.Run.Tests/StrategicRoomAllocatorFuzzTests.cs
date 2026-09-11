using RogueDeck.Run;
using Xunit.Abstractions;

namespace RogueDeck.Run.Tests;

// ROOM ALLOCATION, SWEPT (map rework S6, depth bands added in S7).
//
// StrategicRoomAllocatorTests states each rule on a handful of acts. This file runs ONE invariant over every BnB
// act length, several budget sets, the depth gates the BnB acts actually use and a row of rule extremes:
//
//   EVERY ROOM HOLDS A ROLE, AND EVERY RULE THAT DID NOT HOLD IS REPORTED.
//
// That is the whole contract of this step. A depth gate that yielded, a ceiling that was overshot, a minimum that
// went unkept and two shops side by side are all ALLOWED here — and each of them has to appear as a ForcedRoom or
// a RoomShortfall naming itself. S7's depth bands are the same rules over one slice of the act, so they joined the
// same invariant rather than getting one of their own: a band ceiling overshot and a band minimum unkept have to
// name themselves too, and name the BAND while they are at it. A generator that quietly bends a rule and one that reports bending it are the
// same map and a different pipeline, and the difference is what S7's validator and S10's repair have to work
// with.
public class StrategicRoomAllocatorFuzzTests(ITestOutputHelper output)
{
    private static readonly IReadOnlyList<MapLaneProfile> Lanes =
    [
        Lane("gauntlet", (MapNodeKind.Combat, 9), (MapNodeKind.Elite, 4), (MapNodeKind.Event, 1), (MapNodeKind.Shop, 0)),
        Lane("wilds", (MapNodeKind.Combat, 5), (MapNodeKind.Event, 5), (MapNodeKind.Elite, 2), (MapNodeKind.Treasure, 2)),
        Lane("errands", (MapNodeKind.Combat, 3), (MapNodeKind.Event, 2), (MapNodeKind.Shop, 5), (MapNodeKind.Rest, 3), (MapNodeKind.Workbench, 2)),
        Lane("hoard", (MapNodeKind.Combat, 4), (MapNodeKind.Treasure, 5), (MapNodeKind.Event, 2), (MapNodeKind.Elite, 1)),
    ];

    private static readonly StrategicRoomSpec Spec = new()
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
    };

    private static MapLaneProfile Lane(string name, params (MapNodeKind Kind, int Weight)[] weights) =>
        new(name, weights.ToDictionary(entry => entry.Kind, entry => entry.Weight));

    private static StrategicStrandProfiles Act(int seed, int rows) =>
        StrategicStrandProfileAssigner.Assign(seed, StrategicTopologyGenerator.Generate(seed, rows), Lanes);

    [Theory]
    [InlineData(4)]
    [InlineData(12)]
    [InlineData(23)]    // Act I
    [InlineData(24)]    // Act II
    [InlineData(25)]    // Act III
    [InlineData(35)]    // Act IV
    public void Two_thousand_acts_of_this_length_are_filled_without_a_gap(int rows)
    {
        const int seeds = 2000;
        var spec = Spec with
        {
            RoomBudgets = new Dictionary<MapNodeKind, RoomBudget>
            {
                [MapNodeKind.Elite] = new() { Target = 7 },
                [MapNodeKind.Shop] = new() { Target = 4 },
                [MapNodeKind.Rest] = new() { Target = 4 },
                [MapNodeKind.Treasure] = new() { Target = 5 },
            },
        };

        var rooms = 0;
        var counts = new Dictionary<MapNodeKind, int>();
        for (var seed = 1; seed <= seeds; seed++)
        {
            var plan = StrategicRoomAllocator.Allocate(MapSeedStreams.From(seed).Rooms, Act(seed, rows), spec);
            Check(plan);
            // With no ceilings and no depth gates there is nothing that CAN yield: every room has a legal role.
            Assert.True(plan.Honoured, plan.Render());
            rooms += plan.Kinds.Count;
            foreach (var (kind, count) in plan.Counts)
                counts[kind] = counts.GetValueOrDefault(kind) + count;
        }

        output.WriteLine($"rows {rows}: {(double)rooms / seeds:F1} rooms per act, "
            + string.Join(" ", counts
                .OrderBy(entry => (int)entry.Key)
                .Select(entry => $"{entry.Key} {(double)entry.Value / seeds:F2}")));
    }

    [Theory]
    [InlineData(6, 3, 2, 0, 0)]
    [InlineData(10, 5, 3, 4, 2)]
    [InlineData(1, 1, 1, 1, 1)]
    [InlineData(0, 0, 0, 0, 0)]
    public void A_thousand_acts_keep_these_promises(int elites, int shops, int rests, int treasures, int benches)
    {
        const int seeds = 1000;
        var minimums = new Dictionary<MapNodeKind, int>
        {
            [MapNodeKind.Elite] = elites,
            [MapNodeKind.Shop] = shops,
            [MapNodeKind.Rest] = rests,
            [MapNodeKind.Treasure] = treasures,
            [MapNodeKind.Workbench] = benches,
        };
        var spec = Spec with
        {
            RoomBudgets = minimums.ToDictionary(
                entry => entry.Key,
                entry => new RoomBudget { Min = entry.Value, Target = Math.Max(entry.Value, entry.Value + 2) }),
        };

        var shortfalls = 0;
        for (var seed = 1; seed <= seeds; seed++)
        {
            var plan = StrategicRoomAllocator.Allocate(MapSeedStreams.From(seed).Rooms, Act(seed, rows: 25), spec);
            Check(plan);
            shortfalls += plan.Shortfalls.Count;
            foreach (var (kind, minimum) in minimums)
                Assert.True(plan.Count(kind) >= minimum, plan.Render());
        }

        output.WriteLine($"minimums {elites}/{shops}/{rests}/{treasures}/{benches}: {shortfalls} shortfalls "
            + $"over {seeds} acts");
    }

    // THE DEPTH GATES, WHICH ARE THE RULES MOST LIKELY TO MAKE A ROOM UNFILLABLE: gate every role deep enough and
    // the act's first rows have nothing legal left to hold. Allowed — and then every such room says so.
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(40, 25, 30)]
    [InlineData(60, 50, 70)]
    [InlineData(100, 100, 100)]
    public void A_thousand_acts_under_these_depth_gates(int elite, int shop, int rest)
    {
        const int seeds = 1000;
        var spec = Spec with
        {
            RoleMinimumDepthPercent = new Dictionary<MapNodeKind, int>
            {
                [MapNodeKind.Elite] = elite,
                [MapNodeKind.Shop] = shop,
                [MapNodeKind.Rest] = rest,
            },
            RoomBudgets = new Dictionary<MapNodeKind, RoomBudget>
            {
                [MapNodeKind.Elite] = new() { Target = 7, Min = 4 },
                [MapNodeKind.Shop] = new() { Target = 4, Min = 2 },
                [MapNodeKind.Rest] = new() { Target = 4, Min = 2 },
            },
        };

        var forced = 0;
        var shortfalls = 0;
        for (var seed = 1; seed <= seeds; seed++)
        {
            var plan = StrategicRoomAllocator.Allocate(MapSeedStreams.From(seed).Rooms, Act(seed, rows: 25), spec);
            Check(plan);
            forced += plan.Forced.Count;
            shortfalls += plan.Shortfalls.Count;
        }

        output.WriteLine($"gates {elite}/{shop}/{rest} %: {forced} forced rooms, {shortfalls} shortfalls "
            + $"over {seeds} acts");
    }

    [Theory]
    [InlineData("no penalties at all", 100, 100, 100, 400)]
    [InlineData("a target is a ceiling", 25, 40, 0, 100)]
    [InlineData("repetition forbidden outright", 0, 0, 10, 400)]
    [InlineData("a budget that shouts", 25, 40, 100, 4000)]
    public void A_thousand_acts_under_these_rules(
        string name, int neighbour, int fork, int atTarget, int maximum)
    {
        const int seeds = 1000;
        var spec = Spec with
        {
            Rules = new StrategicRoomRules
            {
                SameAsNeighbourPercent = neighbour,
                SameAtForkPercent = fork,
                AtTargetNeedPercent = atTarget,
                MaxNeedPercent = maximum,
                // Nothing repeats freely and nothing is banned outright: the penalties alone carry the act, so a
                // zero penalty really is a zero score and the fallback path is exercised.
                RepeatFreelyKinds = new HashSet<MapNodeKind>(),
                NoRepeatKinds = new HashSet<MapNodeKind>(),
            },
            RoomBudgets = new Dictionary<MapNodeKind, RoomBudget>
            {
                [MapNodeKind.Elite] = new() { Target = 7, Max = 9 },
                [MapNodeKind.Shop] = new() { Target = 4, Max = 5 },
                [MapNodeKind.Rest] = new() { Target = 4, Max = 5 },
                [MapNodeKind.Treasure] = new() { Target = 5, Max = 7 },
            },
        };

        var counts = new Dictionary<MapNodeKind, int>();
        var forced = 0;
        for (var seed = 1; seed <= seeds; seed++)
        {
            var plan = StrategicRoomAllocator.Allocate(MapSeedStreams.From(seed).Rooms, Act(seed, rows: 25), spec);
            Check(plan);
            forced += plan.Forced.Count;
            foreach (var (kind, count) in plan.Counts)
                counts[kind] = counts.GetValueOrDefault(kind) + count;
        }

        output.WriteLine($"{name}: {forced} forced, "
            + string.Join(" ", counts
                .OrderBy(entry => (int)entry.Key)
                .Select(entry => $"{entry.Key} {(double)entry.Value / seeds:F2}")));
    }

    // THE DEPTH BANDS. A band is the act's own rules over one slice of its depth, so the interesting cases are the
    // ones where a slice cannot carry them: a band nobody can fill, a ceiling of one over half an act, and a band
    // demanding more rooms than it has.
    [Theory]
    [InlineData("shops spread over the quarters")]
    [InlineData("elites held back to the second half")]
    [InlineData("a band asking for more than it holds")]
    [InlineData("every role banded into thirds")]
    public void A_thousand_acts_under_these_depth_bands(string name)
    {
        const int seeds = 1000;
        var spec = Spec with
        {
            RoomBudgets = new Dictionary<MapNodeKind, RoomBudget>
            {
                [MapNodeKind.Elite] = new() { Target = 7, Min = 4 },
                [MapNodeKind.Shop] = new() { Target = 5, Min = 3 },
                [MapNodeKind.Rest] = new() { Target = 4, Min = 2 },
                [MapNodeKind.Treasure] = new() { Target = 5 },
            },
            DepthBands = Bands(name),
        };

        var forced = 0;
        var shortfalls = 0;
        for (var seed = 1; seed <= seeds; seed++)
        {
            var plan = StrategicRoomAllocator.Allocate(MapSeedStreams.From(seed).Rooms, Act(seed, rows: 25), spec);
            Check(plan);
            forced += plan.Forced.Count;
            shortfalls += plan.Shortfalls.Count;

            // WHICHEVER BAND FAILED, IT SAID SO BY NAME. A report that could not tell an act's unkept promise from
            // one slice's would send the author to the wrong number.
            Assert.All(plan.Shortfalls, shortfall => Assert.True(
                shortfall.Band is null || spec.DepthBands.Any(band => band.Label == shortfall.Band.Label),
                shortfall.ToString()));
        }

        output.WriteLine($"{name}: {forced} forced rooms, {shortfalls} shortfalls over {seeds} acts");
    }

    // BANDS AGAINST THE ACT'S LENGTH, which is where a band stops holding anything: a 4-row act's rows sit at
    // 0 / 50 / 100 % and nothing between, so a quarter of that act can be a slice with no room in it at all.
    [Theory]
    [InlineData(4)]
    [InlineData(12)]
    [InlineData(23)]    // Act I
    [InlineData(24)]    // Act II
    [InlineData(25)]    // Act III
    [InlineData(35)]    // Act IV
    public void A_thousand_banded_acts_of_this_length_are_filled_without_a_gap(int rows)
    {
        const int seeds = 1000;
        var spec = Spec with
        {
            RoomBudgets = new Dictionary<MapNodeKind, RoomBudget>
            {
                [MapNodeKind.Elite] = new() { Target = 6, Min = 2 },
                [MapNodeKind.Shop] = new() { Target = 4, Min = 2 },
            },
            DepthBands = Bands("shops spread over the quarters"),
        };

        var empty = 0;
        var shortfalls = 0;
        for (var seed = 1; seed <= seeds; seed++)
        {
            var plan = StrategicRoomAllocator.Allocate(MapSeedStreams.From(seed).Rooms, Act(seed, rows), spec);
            Check(plan);
            shortfalls += plan.Shortfalls.Count;
            empty += Enumerable.Range(0, spec.DepthBands.Count)
                .Count(band => plan.Topology.Slots.All(slot => slot.IsBoss
                    || plan.Spec.BandIndexOf(slot.Row, rows) != band));
        }

        output.WriteLine($"rows {rows}: {shortfalls} shortfalls over {seeds} acts, "
            + $"{empty / (double)seeds:F2} bands per act hold no room at all");
    }

    // WHAT THE VALIDATOR PROMISES, MEASURED. S7's validator is arithmetic on the authored numbers, so it is
    // NECESSARY and not sufficient: it cannot know about bad luck. This is the number that says how close the two
    // are — an act the validator calls clean should come out honoured essentially always, and "essentially" is a
    // figure worth having rather than a hope.
    [Theory]
    [InlineData(23)]
    [InlineData(24)]
    [InlineData(25)]
    [InlineData(35)]
    public void An_act_the_validator_calls_clean_is_honoured(int rows)
    {
        const int seeds = 500;
        var spec = new StrategicActSpec
        {
            Rows = rows,
            LaneProfiles = Lanes,
            Rooms = Spec with
            {
                RoleMinimumDepthPercent = new Dictionary<MapNodeKind, int>
                {
                    [MapNodeKind.Elite] = 20,
                    [MapNodeKind.Shop] = 15,
                },
                RoomBudgets = new Dictionary<MapNodeKind, RoomBudget>
                {
                    [MapNodeKind.Elite] = new() { Min = 3, Target = 6, Max = 9 },
                    [MapNodeKind.Shop] = new() { Min = 2, Target = 4, Max = 6 },
                    [MapNodeKind.Rest] = new() { Min = 2, Target = 4, Max = 5 },
                },
                DepthBands =
                [
                    new DepthBandBudget
                    {
                        StartPercent = 0,
                        EndPercent = 50,
                        Budgets = new Dictionary<MapNodeKind, RoomBudget>
                        {
                            [MapNodeKind.Elite] = new() { Min = 1, Target = 2, Max = 4 },
                            [MapNodeKind.Shop] = new() { Min = 1, Target = 2, Max = 3 },
                        },
                    },
                    new DepthBandBudget
                    {
                        StartPercent = 50,
                        EndPercent = 100,
                        Budgets = new Dictionary<MapNodeKind, RoomBudget>
                        {
                            [MapNodeKind.Elite] = new() { Min = 2, Target = 4, Max = 6 },
                            [MapNodeKind.Shop] = new() { Min = 1, Target = 2, Max = 4 },
                        },
                    },
                ],
            },
        };

        var report = StrategicActSpecValidator.Validate(spec);
        output.WriteLine(report.Render());
        Assert.True(report.Clean, report.Render());

        var honoured = 0;
        for (var seed = 1; seed <= seeds; seed++)
        {
            var plan = StrategicRoomAllocator.Allocate(
                MapSeedStreams.From(seed).Rooms, Act(seed, rows), spec.Rooms);
            Check(plan);
            if (plan.Honoured)
                honoured++;
        }

        output.WriteLine($"rows {rows}: {honoured}/{seeds} acts honoured under a spec the validator calls clean");
        Assert.True(honoured >= seeds - seeds / 100, $"only {honoured} of {seeds} acts were honoured");
    }

    private static IReadOnlyList<DepthBandBudget> Bands(string name) => name switch
    {
        "shops spread over the quarters" => Enumerable.Range(0, 4)
            .Select(index => Band(index * 25, (index + 1) * 25, (MapNodeKind.Shop, 1, 2, 3)))
            .ToList(),
        "elites held back to the second half" =>
        [
            Band(0, 50, (MapNodeKind.Elite, 0, 0, 1)),
            Band(50, 100, (MapNodeKind.Elite, 3, 5, 8)),
        ],
        "a band asking for more than it holds" =>
        [
            Band(0, 25, (MapNodeKind.Shop, 20, 20, 20)),
        ],
        _ =>
        [
            Band(0, 34, (MapNodeKind.Elite, 0, 1, 2), (MapNodeKind.Shop, 1, 2, 2), (MapNodeKind.Rest, 0, 1, 2)),
            Band(34, 67, (MapNodeKind.Elite, 1, 2, 3), (MapNodeKind.Shop, 1, 2, 2), (MapNodeKind.Rest, 1, 2, 2)),
            Band(67, 100, (MapNodeKind.Elite, 1, 3, 4), (MapNodeKind.Shop, 0, 1, 2), (MapNodeKind.Treasure, 1, 2, 3)),
        ],
    };

    private static DepthBandBudget Band(
        int start, int end, params (MapNodeKind Kind, int Min, int Target, int Max)[] budgets) => new()
        {
            StartPercent = start,
            EndPercent = end,
            Budgets = budgets.ToDictionary(
            entry => entry.Kind,
            entry => new RoomBudget { Min = entry.Min, Target = entry.Target, Max = entry.Max }),
        };

    [Fact]
    public void Two_thousand_acts_are_each_filled_identically_the_second_time()
    {
        for (var seed = 1; seed <= 2000; seed++)
        {
            var rooms = MapSeedStreams.From(seed).Rooms;
            var profiles = Act(seed, rows: 23);
            Assert.Equal(
                StrategicRoomAllocator.Allocate(rooms, profiles, Spec).Render(),
                StrategicRoomAllocator.Allocate(rooms, Act(seed, rows: 23), Spec).Render());
        }
    }

    // WHAT A LANE PROFILE IS NOW WORTH, as a number: the same role, counted on the routes that want it and on the
    // routes that do not. Under `column % LaneProfiles.Count` this table could not be produced at all.
    [Fact]
    public void A_routes_flavour_decides_what_it_holds()
    {
        const int seeds = 2000;
        var rooms = new int[Lanes.Count];
        var combat = new int[Lanes.Count];
        var shops = new int[Lanes.Count];
        var treasure = new int[Lanes.Count];

        for (var seed = 1; seed <= seeds; seed++)
        {
            var profiles = Act(seed, rows: 25);
            var plan = StrategicRoomAllocator.Allocate(MapSeedStreams.From(seed).Rooms, profiles, Spec with
            {
                RoomBudgets = new Dictionary<MapNodeKind, RoomBudget>
                {
                    [MapNodeKind.Shop] = new() { Target = 4 },
                    [MapNodeKind.Treasure] = new() { Target = 5 },
                },
            });

            foreach (var slot in plan.Topology.Slots.Where(slot => !slot.IsBoss))
            {
                var lane = profiles.IndexOf(slot.Id);
                rooms[lane]++;
                switch (plan.KindOf(slot.Id))
                {
                    case MapNodeKind.Combat: combat[lane]++; break;
                    case MapNodeKind.Shop: shops[lane]++; break;
                    case MapNodeKind.Treasure: treasure[lane]++; break;
                }
            }
        }

        for (var lane = 0; lane < Lanes.Count; lane++)
            output.WriteLine($"{Lanes[lane].Name,-9} {rooms[lane],6} rooms  "
                + $"combat {(double)combat[lane] / rooms[lane]:P1}  "
                + $"shop {(double)shops[lane] / rooms[lane]:P1}  "
                + $"treasure {(double)treasure[lane] / rooms[lane]:P1}");

        // The gauntlet fights more than the errand run, holds no shops at all, and the hoard holds the treasure.
        Assert.True((double)combat[0] / rooms[0] > (double)combat[2] / rooms[2] + 0.2);
        Assert.Equal(0, shops[0]);
        Assert.True((double)treasure[3] / rooms[3] > (double)treasure[0] / rooms[0] + 0.1);
    }

    [Fact]
    public void One_act_read_out_loud()
    {
        const int seed = 20260911;
        var profiles = Act(seed, rows: 23);
        var plan = StrategicRoomAllocator.Allocate(MapSeedStreams.From(seed).Rooms, profiles, Spec with
        {
            RoleMinimumDepthPercent = new Dictionary<MapNodeKind, int>
            {
                [MapNodeKind.Elite] = 25,
                [MapNodeKind.Shop] = 20,
                [MapNodeKind.Rest] = 30,
            },
            RoomBudgets = new Dictionary<MapNodeKind, RoomBudget>
            {
                [MapNodeKind.Elite] = new() { Target = 7, Min = 5, Max = 9 },
                [MapNodeKind.Shop] = new() { Target = 4, Min = 3, Max = 5 },
                [MapNodeKind.Rest] = new() { Target = 4, Min = 3, Max = 5 },
                [MapNodeKind.Treasure] = new() { Target = 5, Min = 3, Max = 7 },
                [MapNodeKind.Workbench] = new() { Target = 2, Min = 1, Max = 3 },
            },
        });

        output.WriteLine(profiles.Render());
        output.WriteLine(plan.Render());
        Assert.True(plan.Honoured, plan.Render());
    }

    // THE ONE INVARIANT. Every room holds a role, and every rule that did not hold named itself.
    private static void Check(StrategicRoomPlan plan)
    {
        var spec = plan.Spec;
        var topology = plan.Topology;
        var rows = topology.Rows.Count;
        var forced = plan.Forced.Select(entry => entry.Room).ToHashSet();

        Assert.Equal(topology.NodeCount, plan.Kinds.Count);
        foreach (var slot in topology.Slots)
        {
            var kind = plan.KindOf(slot.Id);
            Assert.Equal(slot.IsBoss, kind == MapNodeKind.Boss);
            Assert.NotEqual(MapNodeKind.Mimic, kind);
            if (slot.IsBoss)
                continue;

            // A depth gate held, or this room is on the forced list.
            if (MapDepth.Percent(slot.Row, rows) < spec.EarliestDepthOf(kind))
                Assert.Contains(slot.Id, forced);
        }

        foreach (var (kind, budget) in spec.RoomBudgets)
        {
            // A ceiling held, or it was overshot only by rooms that had no legal role at all.
            var over = plan.Count(kind) - budget.Max;
            if (over > 0)
                Assert.True(plan.Forced.Count(entry => entry.Kind == kind) >= over, plan.Render());

            // A minimum was kept, or the shortfall says by how much.
            if (plan.Count(kind) < budget.Min)
                Assert.Contains(plan.Shortfalls, shortfall => shortfall.Kind == kind && shortfall.Missing > 0);
        }

        // THE SAME TWO CLAIMS PER BAND, over that band's rooms only: a band ceiling held unless rooms in the band
        // had no legal role at all, and a band minimum was kept unless a shortfall says so AND names the band.
        for (var index = 0; index < spec.DepthBands.Count; index++)
        {
            var band = spec.DepthBands[index];
            foreach (var (kind, budget) in band.Budgets)
            {
                var held = plan.CountInBand(index, kind);
                var over = held - budget.Max;
                if (over > 0)
                    Assert.True(
                        plan.Forced.Count(entry => entry.Kind == kind && InBand(plan, index, entry.Room)) >= over,
                        plan.Render());

                if (held < budget.Min)
                    Assert.Contains(plan.Shortfalls, shortfall => shortfall.Kind == kind
                        && shortfall.Band is not null
                        && shortfall.Band.Label == band.Label
                        && shortfall.Missing > 0);
            }
        }

        // A no-repeat ban held along every edge, unless one of the two rooms had no legal role.
        foreach (var edge in topology.Edges)
        {
            if (!plan.TryKindOf(edge.From, out var from) || !plan.TryKindOf(edge.To, out var to) || from != to)
                continue;
            if (spec.Rules.NoRepeatKinds.Contains(from))
                Assert.True(forced.Contains(edge.From) || forced.Contains(edge.To), plan.Render());
        }
    }

    private static bool InBand(StrategicRoomPlan plan, int band, NodeId room) =>
        plan.Topology.TryGetSlot(room, out var slot)
        && slot is not null
        && plan.Spec.BandIndexOf(slot.Row, plan.Topology.Rows.Count) == band;
}
