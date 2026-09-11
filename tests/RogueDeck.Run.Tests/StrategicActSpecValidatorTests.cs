using RogueDeck.Run;
using Xunit.Abstractions;

namespace RogueDeck.Run.Tests;

// WHETHER AN ACT CAN HOLD WHAT IT WAS AUTHORED TO HOLD (map rework S7).
//
// S6's allocator reports what it could not do, which is the right answer to bad luck and the wrong place to learn
// about bad arithmetic: "six elites, none before 60 % of the act's depth, in a twelve-row act two rooms wide"
// fails for every seed, and hearing about it as a shortfall on seed 4 711 makes it a mystery.
//
// The two severities are the whole design and each test below pins one of them: IMPOSSIBLE means the widest act
// this spec permits cannot satisfy it, so no seed can; TIGHT means the widest can and the narrowest cannot, so
// some seeds will report a shortfall. A width is a range here because the walk draws it per seed.
public class StrategicActSpecValidatorTests(ITestOutputHelper output)
{
    private static readonly IReadOnlyList<MapLaneProfile> Lanes =
    [
        Lane("gauntlet", (MapNodeKind.Combat, 9), (MapNodeKind.Elite, 4), (MapNodeKind.Event, 1)),
        Lane("wilds", (MapNodeKind.Combat, 5), (MapNodeKind.Event, 5), (MapNodeKind.Treasure, 2)),
        Lane("errands", (MapNodeKind.Combat, 3), (MapNodeKind.Event, 2), (MapNodeKind.Shop, 5), (MapNodeKind.Rest, 3)),
        Lane("hoard", (MapNodeKind.Combat, 4), (MapNodeKind.Treasure, 5), (MapNodeKind.Event, 2)),
    ];

    private static MapLaneProfile Lane(string name, params (MapNodeKind Kind, int Weight)[] weights) =>
        new(name, weights.ToDictionary(entry => entry.Kind, entry => entry.Weight));

    // An Act III-shaped act that holds what BnB asks of one, as a baseline to break in one place at a time.
    private static StrategicActSpec Act(StrategicRoomSpec? rooms = null, int rows = 25) => new()
    {
        Rows = rows,
        LaneProfiles = Lanes,
        Rooms = rooms ?? Rooms(),
    };

    private static StrategicRoomSpec Rooms(
        IReadOnlyDictionary<MapNodeKind, RoomBudget>? budgets = null,
        IReadOnlyDictionary<MapNodeKind, int>? gates = null,
        IReadOnlyList<DepthBandBudget>? bands = null) => new()
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
            RoomBudgets = budgets ?? new Dictionary<MapNodeKind, RoomBudget>(),
            RoleMinimumDepthPercent = gates ?? new Dictionary<MapNodeKind, int>(),
            DepthBands = bands ?? [],
        };

    private static DepthBandBudget Band(int start, int end, params (MapNodeKind Kind, int Min, int Target, int Max)[] budgets) => new()
    {
        StartPercent = start,
        EndPercent = end,
        Budgets = budgets.ToDictionary(
            entry => entry.Kind,
            entry => new RoomBudget { Min = entry.Min, Target = entry.Target, Max = entry.Max }),
    };

    private static IEnumerable<string> Messages(StrategicSpecReport report, StrategicSpecSeverity severity) =>
        report.Of(severity).Select(problem => problem.ToString());

    [Fact]
    public void An_act_that_asks_for_what_it_can_hold_reports_nothing_at_all()
    {
        var report = StrategicActSpecValidator.Validate(Act(Rooms(
            budgets: new Dictionary<MapNodeKind, RoomBudget>
            {
                [MapNodeKind.Elite] = new() { Min = 4, Target = 7, Max = 9 },
                [MapNodeKind.Shop] = new() { Min = 3, Target = 5, Max = 6 },
                [MapNodeKind.Rest] = new() { Min = 2, Target = 4, Max = 5 },
                [MapNodeKind.Treasure] = new() { Target = 5, Max = 7 },
            },
            gates: new Dictionary<MapNodeKind, int> { [MapNodeKind.Elite] = 20, [MapNodeKind.Shop] = 15 },
            bands:
            [
                Band(0, 25, (MapNodeKind.Elite, 0, 0, 1), (MapNodeKind.Shop, 0, 1, 2)),
                Band(25, 50, (MapNodeKind.Elite, 1, 2, 3), (MapNodeKind.Shop, 1, 2, 2)),
                Band(50, 75, (MapNodeKind.Elite, 1, 2, 3), (MapNodeKind.Shop, 1, 2, 2)),
                Band(75, 100, (MapNodeKind.Elite, 1, 3, 4), (MapNodeKind.Shop, 0, 1, 2)),
            ])));

        output.WriteLine(report.Render());
        Assert.True(report.Clean, report.Render());
        Assert.True(report.Possible);
    }

    // S4 ALREADY REFUSES THIS ONE, and the point of a validator is that nobody has to spend a seed to hear it.
    // Both are asserted, because a validator that disagreed with the generator would be worse than none.
    [Fact]
    public void An_act_too_short_for_its_own_branch_rule_is_refused_before_a_seed_is_spent()
    {
        var report = StrategicActSpecValidator.Validate(Act(rows: 3));

        output.WriteLine(report.Render());
        Assert.False(report.Possible);
        Assert.Contains("too few for the 3 rows a branch must live",
            string.Join(" ", Messages(report, StrategicSpecSeverity.Impossible)));
        Assert.Throws<ArgumentException>(() =>
            StrategicTopologyGenerator.Generate(seed: 1, rows: 3, bossRooms: 1, minWidth: 2, maxWidth: 4, new StrategicTopologyRules()));
    }

    // A 4-row act cannot fork at all: the earliest split is row 1 and a branch must live three rows, which the
    // boss's own convergence will not allow. Not broken, and not a thing to discover by reading a picture.
    [Fact]
    public void An_act_no_row_of_which_may_fork_is_reported_as_parallel_corridors()
    {
        var report = StrategicActSpecValidator.Validate(Act(rows: 4));

        output.WriteLine(report.Render());
        Assert.True(report.Possible);
        Assert.Contains("parallel corridors", string.Join(" ", Messages(report, StrategicSpecSeverity.Tight)));
    }

    [Theory]
    // 24 rows at width 2..4 hold 48..96 rooms, so the act's reach is a range and so is the verdict.
    [InlineData(60, StrategicSpecSeverity.Tight)]
    [InlineData(100, StrategicSpecSeverity.Impossible)]
    public void A_minimum_is_weighed_against_both_ends_of_the_acts_width(int minimum, StrategicSpecSeverity severity)
    {
        var report = StrategicActSpecValidator.Validate(Act(Rooms(
            budgets: new Dictionary<MapNodeKind, RoomBudget>
            {
                [MapNodeKind.Elite] = new() { Min = minimum, Target = minimum },
            })));

        output.WriteLine(report.Render());
        Assert.Equal(severity is StrategicSpecSeverity.Tight, report.Possible);
        Assert.Contains("Elite", string.Join(" ", Messages(report, severity)));
        Assert.Contains("48 room(s) at the act's narrowest and 96 at its widest",
            string.Join(" ", Messages(report, severity)));
    }

    [Theory]
    // Gated to the act's deepest row, a role has ONE row to stand in — 2 rooms at the narrowest, 4 at the widest.
    [InlineData(3, StrategicSpecSeverity.Tight)]
    [InlineData(9, StrategicSpecSeverity.Impossible)]
    public void A_depth_gate_narrows_what_a_promise_is_weighed_against(int minimum, StrategicSpecSeverity severity)
    {
        var report = StrategicActSpecValidator.Validate(Act(Rooms(
            budgets: new Dictionary<MapNodeKind, RoomBudget>
            {
                [MapNodeKind.Shop] = new() { Min = minimum, Target = minimum },
            },
            gates: new Dictionary<MapNodeKind, int> { [MapNodeKind.Shop] = 100 })));

        output.WriteLine(report.Render());
        Assert.Equal(severity is StrategicSpecSeverity.Tight, report.Possible);
        Assert.Contains("as deep as 100 %", string.Join(" ", Messages(report, severity)));
    }

    // THE SOURCE DOCUMENT'S OWN §14 EXAMPLE, to the number: "a role with earliest depth 35 % cannot be placed in a
    // 0-25 % band even if that band requests one".
    [Theory]
    [InlineData(1, 0, StrategicSpecSeverity.Impossible)]
    [InlineData(0, 1, StrategicSpecSeverity.Tight)]
    public void A_band_asking_for_a_role_the_gates_keep_out_of_it_is_named(
        int minimum, int target, StrategicSpecSeverity severity)
    {
        var report = StrategicActSpecValidator.Validate(Act(Rooms(
            gates: new Dictionary<MapNodeKind, int> { [MapNodeKind.Elite] = 35 },
            bands: [Band(0, 25, (MapNodeKind.Elite, minimum, Math.Max(minimum, target), int.MaxValue))])));

        output.WriteLine(report.Render());
        Assert.Equal(severity is StrategicSpecSeverity.Tight, report.Possible);
        Assert.Contains("a band asks within the gates, it does not lift them",
            string.Join(" ", Messages(report, severity)));
    }

    // A SHORT ACT'S ROWS SIT AT COARSE DEPTHS. A 4-row act has rows at 0, 50 and 100 % and nothing between them,
    // so a 25-50 % band is a sensible sentence about an act that cannot hear it — and the report says which
    // depths the act actually has, because that is the number the author has to author against.
    [Fact]
    public void A_band_no_row_of_a_short_act_falls_into_is_named_with_the_depths_it_does_have()
    {
        var report = StrategicActSpecValidator.Validate(Act(
            Rooms(bands: [Band(25, 50, (MapNodeKind.Shop, 1, 1, int.MaxValue))]),
            rows: 4));

        output.WriteLine(report.Render());
        Assert.False(report.Possible);
        var message = string.Join(" ", Messages(report, StrategicSpecSeverity.Impossible));
        Assert.Contains("no row of a 4-row act sits at a depth inside it", message);
        Assert.Contains("0 / 50 / 100 %", message);
    }

    [Fact]
    public void A_band_demanding_more_than_the_act_allows_in_total_is_a_contradiction()
    {
        var report = StrategicActSpecValidator.Validate(Act(Rooms(
            budgets: new Dictionary<MapNodeKind, RoomBudget>
            {
                [MapNodeKind.Rest] = new() { Min = 2, Target = 2, Max = 2 },
            },
            bands: [Band(25, 50, (MapNodeKind.Rest, 3, 3, int.MaxValue))])));

        output.WriteLine(report.Render());
        Assert.False(report.Possible);
        Assert.Contains("demands at least 3 while the act allows at most 2",
            string.Join(" ", Messages(report, StrategicSpecSeverity.Impossible)));
    }

    [Fact]
    public void Bands_that_cover_the_act_cannot_allow_less_than_the_act_demands()
    {
        var report = StrategicActSpecValidator.Validate(Act(Rooms(
            budgets: new Dictionary<MapNodeKind, RoomBudget>
            {
                [MapNodeKind.Elite] = new() { Min = 6, Target = 6 },
            },
            bands:
            [
                Band(0, 25, (MapNodeKind.Elite, 0, 0, 1)),
                Band(25, 50, (MapNodeKind.Elite, 0, 1, 1)),
                Band(50, 75, (MapNodeKind.Elite, 0, 1, 1)),
                Band(75, 100, (MapNodeKind.Elite, 0, 1, 1)),
            ])));

        output.WriteLine(report.Render());
        Assert.False(report.Possible);
        Assert.Contains("cover the whole act and allow at most 4 between them, while the act demands at least 6",
            string.Join(" ", Messages(report, StrategicSpecSeverity.Impossible)));
    }

    // A BAND THAT DOES NOT COVER THE ACT SAYS NOTHING ABOUT THE ACT'S TOTAL. The same ceilings as above, over half
    // the act, leave the other half free to hold every elite — so this must NOT be reported.
    [Fact]
    public void Bands_over_part_of_an_act_are_no_ceiling_on_the_whole_of_it()
    {
        var report = StrategicActSpecValidator.Validate(Act(Rooms(
            budgets: new Dictionary<MapNodeKind, RoomBudget>
            {
                [MapNodeKind.Elite] = new() { Min = 6, Target = 6 },
            },
            bands: [Band(0, 25, (MapNodeKind.Elite, 0, 0, 1)), Band(25, 50, (MapNodeKind.Elite, 0, 1, 1))])));

        output.WriteLine(report.Render());
        Assert.True(report.Clean, report.Render());
    }

    // AN AUTHORED 0 IN A LANE PROFILE MEANS "NEVER ON THIS ROUTE" (S6). If every profile says it, no room of the
    // act can hold the role, whichever flavours a seed happens to use.
    [Theory]
    [InlineData(2, 0, StrategicSpecSeverity.Impossible)]
    [InlineData(0, 2, StrategicSpecSeverity.Tight)]
    public void A_role_every_route_refuses_cannot_be_promised(
        int minimum, int target, StrategicSpecSeverity severity)
    {
        var spec = Act(Rooms(budgets: new Dictionary<MapNodeKind, RoomBudget>
        {
            [MapNodeKind.Shop] = new() { Min = minimum, Target = Math.Max(minimum, target) },
        })) with
        {
            LaneProfiles =
            [
                Lane("gauntlet", (MapNodeKind.Combat, 9), (MapNodeKind.Shop, 0)),
                Lane("wilds", (MapNodeKind.Combat, 5), (MapNodeKind.Event, 5), (MapNodeKind.Shop, 0)),
            ],
        };

        var report = StrategicActSpecValidator.Validate(spec);

        output.WriteLine(report.Render());
        Assert.Equal(severity is StrategicSpecSeverity.Tight, report.Possible);
        Assert.Contains("lane profiles weights it 0", string.Join(" ", Messages(report, severity)));
    }

    // A TARGET ONLY BENDS A DRAW; IT CANNOT CREATE ONE. A role no lane names and the act weights 0 is legal
    // nowhere in the second pass, so a target without a minimum is a number that does nothing at all — the
    // subtlest trap in the whole spec vocabulary, and invisible in a generated act.
    [Fact]
    public void A_target_that_nothing_weights_is_a_number_that_does_nothing()
    {
        var spec = Act(new StrategicRoomSpec
        {
            KindWeights = new Dictionary<MapNodeKind, int> { [MapNodeKind.Combat] = 7 },
            RoomBudgets = new Dictionary<MapNodeKind, RoomBudget> { [MapNodeKind.Treasure] = new() { Target = 5 } },
        }) with
        {
            LaneProfiles = [Lane("gauntlet", (MapNodeKind.Combat, 9))],
        };

        var report = StrategicActSpecValidator.Validate(spec);

        output.WriteLine(report.Render());
        Assert.False(report.Possible);
        Assert.Contains("Give it a weight, or a minimum",
            string.Join(" ", Messages(report, StrategicSpecSeverity.Impossible)));
    }

    [Fact]
    public void An_act_with_no_role_to_fill_its_rooms_with_is_refused()
    {
        var spec = Act(new StrategicRoomSpec { KindWeights = new Dictionary<MapNodeKind, int>() }) with
        {
            LaneProfiles = [new MapLaneProfile("silent", new Dictionary<MapNodeKind, int> { [MapNodeKind.Combat] = 0 })],
        };

        var report = StrategicActSpecValidator.Validate(spec);

        output.WriteLine(report.Render());
        Assert.False(report.Possible);
        Assert.Contains("nothing to fill the act's rooms with",
            string.Join(" ", Messages(report, StrategicSpecSeverity.Impossible)));
    }

    [Fact]
    public void A_role_that_may_both_repeat_freely_and_never_repeat_is_confusing_rather_than_broken()
    {
        var rooms = Rooms();
        var report = StrategicActSpecValidator.Validate(Act(rooms with
        {
            Rules = rooms.Rules with
            {
                RepeatFreelyKinds = new HashSet<MapNodeKind> { MapNodeKind.Combat, MapNodeKind.Shop },
                NoRepeatKinds = new HashSet<MapNodeKind> { MapNodeKind.Shop, MapNodeKind.Rest },
            },
        }));

        output.WriteLine(report.Render());
        Assert.True(report.Possible);
        Assert.Contains("The ban wins", string.Join(" ", Messages(report, StrategicSpecSeverity.Tight)));
    }

    // MALFORMED AND UNSATISFIABLE ARE DIFFERENT MISTAKES and the types draw the line where they should — but a
    // caller asking "is this act buildable" wants one list of reasons, not a list plus an exception to catch.
    [Fact]
    public void A_spec_that_contradicts_itself_as_data_comes_back_as_a_reason_rather_than_a_throw()
    {
        var report = StrategicActSpecValidator.Validate(Act(Rooms(
            bands: [Band(0, 50), Band(40, 100)])));

        output.WriteLine(report.Render());
        Assert.False(report.Possible);
        Assert.Contains("overlap", string.Join(" ", Messages(report, StrategicSpecSeverity.Impossible)));
    }

    [Fact]
    public void The_gate_refuses_the_impossible_and_lets_the_merely_tight_through()
    {
        var impossible = Act(Rooms(budgets: new Dictionary<MapNodeKind, RoomBudget>
        {
            [MapNodeKind.Elite] = new() { Min = 100, Target = 100 },
        }));
        var tight = Act(Rooms(budgets: new Dictionary<MapNodeKind, RoomBudget>
        {
            [MapNodeKind.Elite] = new() { Min = 60, Target = 60 },
        }));

        var problem = Assert.Throws<ArgumentException>(() => StrategicActSpecValidator.ThrowIfImpossible(impossible));
        Assert.Contains("cannot be generated as authored", problem.Message);
        Assert.Contains("Elite", problem.Message);
        StrategicActSpecValidator.ThrowIfImpossible(tight);
    }

    // THE VALIDATOR AND THE ALLOCATOR HAVE TO AGREE, or the validator is decoration: an act the validator calls
    // possible and clean must come out of the allocator honoured, and one it calls impossible must not.
    [Fact]
    public void What_the_validator_calls_clean_the_allocator_honours()
    {
        var rooms = Rooms(
            budgets: new Dictionary<MapNodeKind, RoomBudget>
            {
                [MapNodeKind.Elite] = new() { Min = 4, Target = 7, Max = 9 },
                [MapNodeKind.Shop] = new() { Min = 3, Target = 5, Max = 6 },
                [MapNodeKind.Rest] = new() { Min = 2, Target = 4, Max = 5 },
            },
            gates: new Dictionary<MapNodeKind, int> { [MapNodeKind.Elite] = 20, [MapNodeKind.Shop] = 15 },
            bands:
            [
                Band(0, 50, (MapNodeKind.Elite, 1, 2, 4), (MapNodeKind.Shop, 1, 2, 3)),
                Band(50, 100, (MapNodeKind.Elite, 2, 4, 6), (MapNodeKind.Shop, 1, 3, 4)),
            ]);
        var spec = Act(rooms);
        Assert.True(StrategicActSpecValidator.Validate(spec).Clean);

        var impossible = Act(rooms with
        {
            RoleMinimumDepthPercent = new Dictionary<MapNodeKind, int> { [MapNodeKind.Elite] = 60 },
        });
        Assert.False(StrategicActSpecValidator.Validate(impossible).Possible);

        var honoured = 0;
        var broken = 0;
        for (var seed = 1; seed <= 200; seed++)
        {
            var profiles = StrategicStrandProfileAssigner.Assign(
                seed, StrategicTopologyGenerator.Generate(seed, spec.Rows), spec.LaneProfiles);
            if (StrategicRoomAllocator.Allocate(MapSeedStreams.From(seed).Rooms, profiles, spec.Rooms).Honoured)
                honoured++;
            if (!StrategicRoomAllocator.Allocate(MapSeedStreams.From(seed).Rooms, profiles, impossible.Rooms).Honoured)
                broken++;
        }

        output.WriteLine($"clean spec: {honoured}/200 acts honoured · impossible spec: {broken}/200 acts reported a problem");
        Assert.Equal(200, honoured);
        Assert.Equal(200, broken);
    }
}
