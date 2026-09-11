using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// WHAT AN ACT HOLDS, AND WHERE (map rework S6).
//
// The rule-based generator draws each room independently and then repairs the result: gate funnels to keep the
// per-path minimums, a rewrite into Combat to keep the ceilings. Here the counts are an ACT-WIDE budget, the
// promises are placed before anything is drawn, a ceiling is a hard filter, and the route's own flavour — the
// strand profile S5 bound — decides what a room is likely to be.
//
// The tests are written to be falsifiable on the two claims that matter: a budget is actually kept (and its
// failure reported rather than bent), and what a room holds follows its ROUTE and not its column.
public class StrategicRoomAllocatorTests
{
    private const int Rows = 23;

    // Four authored flavours with real weight tables — S6 is the step that reads them. `gauntlet` forbids shops
    // outright (an authored 0), which is a thing a lane could not say while lanes were columns.
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

    private static StrategicStrandProfiles Act(int seed, int rows = Rows, IReadOnlyList<MapLaneProfile>? lanes = null) =>
        StrategicStrandProfileAssigner.Assign(
            seed, StrategicTopologyGenerator.Generate(seed, rows), lanes ?? Lanes);

    private static IEnumerable<StrategicSlot> RoomsOf(StrategicRoomPlan plan) =>
        plan.Topology.Slots.Where(slot => !slot.IsBoss);

    [Fact]
    public void Every_room_holds_a_role_and_only_the_boss_rooms_hold_the_boss()
    {
        var profiles = Act(seed: 1);
        var plan = StrategicRoomAllocator.Allocate(seed: 1, profiles, Spec);

        Assert.Equal(profiles.Topology.NodeCount, plan.Kinds.Count);
        Assert.True(plan.Honoured);
        foreach (var slot in profiles.Topology.Slots)
        {
            var kind = plan.KindOf(slot.Id);
            Assert.Equal(slot.IsBoss, kind == MapNodeKind.Boss);
            // A Mimic is a realized Treasure, never a placed role.
            Assert.NotEqual(MapNodeKind.Mimic, kind);
        }

        Assert.Equal(1, plan.Count(MapNodeKind.Boss));
        Assert.Equal(plan.Kinds.Count, plan.Counts.Values.Sum());
    }

    // A MINIMUM IS A PROMISE, and it is kept before a single room is drawn by weight — which is the whole reason
    // the old generator needed gate funnels. Asserted across seeds, because "it worked on seed 1" is not a
    // guarantee.
    [Fact]
    public void A_promised_role_is_placed_before_anything_is_drawn_by_weight()
    {
        var spec = Spec with
        {
            RoomBudgets = new Dictionary<MapNodeKind, RoomBudget>
            {
                [MapNodeKind.Elite] = new() { Target = 8, Min = 6, Max = 10 },
                [MapNodeKind.Shop] = new() { Target = 3, Min = 3, Max = 4 },
                [MapNodeKind.Rest] = new() { Target = 3, Min = 2, Max = 4 },
            },
        };

        for (var seed = 1; seed <= 200; seed++)
        {
            var plan = StrategicRoomAllocator.Allocate(seed, Act(seed), spec);

            Assert.Empty(plan.Shortfalls);
            Assert.Empty(plan.Forced);
            Assert.True(plan.Count(MapNodeKind.Elite) >= 6, plan.Render());
            Assert.True(plan.Count(MapNodeKind.Shop) >= 3, plan.Render());
            Assert.True(plan.Count(MapNodeKind.Rest) >= 2, plan.Render());
        }
    }

    [Fact]
    public void A_ceiling_is_a_ceiling_and_not_a_preference()
    {
        var spec = Spec with
        {
            RoomBudgets = new Dictionary<MapNodeKind, RoomBudget>
            {
                // A target AT the ceiling and a generous weight: the need factor wants more and may not have it.
                [MapNodeKind.Elite] = new() { Target = 3, Max = 3 },
                [MapNodeKind.Shop] = new() { Target = 2, Max = 2 },
                [MapNodeKind.Treasure] = new() { Target = 1, Max = 1 },
            },
        };

        for (var seed = 1; seed <= 200; seed++)
        {
            var plan = StrategicRoomAllocator.Allocate(seed, Act(seed), spec);
            Assert.True(plan.Count(MapNodeKind.Elite) <= 3, plan.Render());
            Assert.True(plan.Count(MapNodeKind.Shop) <= 2, plan.Render());
            Assert.True(plan.Count(MapNodeKind.Treasure) <= 1, plan.Render());
            Assert.Empty(plan.Forced);
        }
    }

    // A TARGET IS SOFT AND STILL MOSTLY MET. The need factor measures the act's PACE — on schedule is neutral,
    // behind is likelier — so the average lands on the target without the allocator ever being told to stop.
    [Fact]
    public void An_act_lands_near_the_counts_its_budget_asked_for()
    {
        var spec = Spec with
        {
            RoomBudgets = new Dictionary<MapNodeKind, RoomBudget>
            {
                [MapNodeKind.Elite] = new() { Target = 8 },
                [MapNodeKind.Shop] = new() { Target = 4 },
                [MapNodeKind.Rest] = new() { Target = 4 },
                [MapNodeKind.Treasure] = new() { Target = 5 },
            },
        };

        const int seeds = 200;
        var totals = new Dictionary<MapNodeKind, int>();
        for (var seed = 1; seed <= seeds; seed++)
        {
            var plan = StrategicRoomAllocator.Allocate(seed, Act(seed), spec);
            foreach (var (kind, budget) in spec.RoomBudgets)
                totals[kind] = totals.GetValueOrDefault(kind) + plan.Count(kind);
        }

        foreach (var (kind, budget) in spec.RoomBudgets)
        {
            var average = (double)totals[kind] / seeds;
            Assert.InRange(average, budget.Target - 1.5, budget.Target + 1.5);
        }
    }

    // AN AUTHORED 0 IS A REFUSAL, and it is the sentence a lane profile could not say while a lane was a column:
    // there are no shops on the gauntlet, ever, however much the act wants shops.
    [Fact]
    public void A_route_that_refuses_a_role_never_holds_one()
    {
        var spec = Spec with
        {
            RoomBudgets = new Dictionary<MapNodeKind, RoomBudget> { [MapNodeKind.Shop] = new() { Target = 8, Min = 4 } },
        };

        var elsewhere = 0;
        for (var seed = 1; seed <= 200; seed++)
        {
            var plan = StrategicRoomAllocator.Allocate(seed, Act(seed), spec);
            Assert.Equal(0, plan.CountOnProfile(0, MapNodeKind.Shop));
            elsewhere += plan.CountOnProfile(2, MapNodeKind.Shop);
        }

        // And the refusal is about the gauntlet, not about shops: the errand routes are full of them.
        Assert.True(elsewhere > 100, $"errand routes held only {elsewhere} shops");
    }

    // THE FALSIFIABLE HALF. Rooms in the SAME COLUMN are compared against each other, grouped by the flavour
    // their route carries: if a room's content followed its column, the two groups would hold the same mix. The
    // gauntlet routes hold markedly more combat than the errand routes in the very same column.
    [Fact]
    public void What_a_room_holds_follows_its_route_and_not_its_column()
    {
        const int column = 1;
        var rooms = new Dictionary<int, int>();
        var combat = new Dictionary<int, int>();

        for (var seed = 1; seed <= 300; seed++)
        {
            var profiles = Act(seed);
            var plan = StrategicRoomAllocator.Allocate(seed, profiles, Spec);
            foreach (var slot in RoomsOf(plan).Where(slot => slot.Column == column))
            {
                var index = profiles.IndexOf(slot.Id);
                rooms[index] = rooms.GetValueOrDefault(index) + 1;
                if (plan.KindOf(slot.Id) == MapNodeKind.Combat)
                    combat[index] = combat.GetValueOrDefault(index) + 1;
            }
        }

        Assert.True(rooms[0] > 200 && rooms[2] > 200, $"too few samples: {rooms[0]} / {rooms[2]}");
        var gauntlet = (double)combat[0] / rooms[0];
        var errands = (double)combat[2] / rooms[2];
        Assert.True(gauntlet > errands + 0.2,
            $"combat share in column {column}: gauntlet {gauntlet:P0} vs errands {errands:P0}");
    }

    [Fact]
    public void A_depth_gate_is_a_hard_filter_here()
    {
        var spec = Spec with
        {
            RoleMinimumDepthPercent = new Dictionary<MapNodeKind, int>
            {
                [MapNodeKind.Elite] = 40,
                [MapNodeKind.Shop] = 25,
                [MapNodeKind.Rest] = 30,
            },
            RoomBudgets = new Dictionary<MapNodeKind, RoomBudget>
            {
                [MapNodeKind.Elite] = new() { Target = 8, Min = 5 },
                [MapNodeKind.Shop] = new() { Target = 4, Min = 3 },
            },
        };

        for (var seed = 1; seed <= 200; seed++)
        {
            var plan = StrategicRoomAllocator.Allocate(seed, Act(seed), spec);
            var rows = plan.Topology.Rows.Count;
            foreach (var slot in RoomsOf(plan))
            {
                var gate = spec.RoleMinimumDepthPercent.GetValueOrDefault(plan.KindOf(slot.Id));
                Assert.True(MapDepth.Percent(slot.Row, rows) >= gate,
                    $"{plan.KindOf(slot.Id)} at {MapDepth.Percent(slot.Row, rows)} % needs {gate} %");
            }
            Assert.Empty(plan.Forced);
        }
    }

    // A CHOICE BETWEEN TWO IDENTICAL ROOMS IS NOT A CHOICE — and it is invisible in any count of what an act
    // holds, which is why it needs its own rule. Tested from both sides: the penalty off, and the penalty
    // absolute.
    [Fact]
    public void A_fork_can_be_made_to_stop_offering_the_same_thing_twice()
    {
        var spec = Spec with
        {
            RoomBudgets = new Dictionary<MapNodeKind, RoomBudget>
            {
                [MapNodeKind.Event] = new() { Target = 12 },
                [MapNodeKind.Treasure] = new() { Target = 8 },
            },
        };

        var unpenalized = Twins(spec with { Rules = spec.Rules with { SameAtForkPercent = 100 } });
        var forbidden = Twins(spec with { Rules = spec.Rules with { SameAtForkPercent = 0 } });

        Assert.True(unpenalized > 20, $"only {unpenalized} identical fork pairs without the rule");
        Assert.Equal(0, forbidden);
    }

    // Pairs of rooms in one row that the same room leads into and that hold the same non-Combat role.
    private static int Twins(StrategicRoomSpec spec)
    {
        var twins = 0;
        for (var seed = 1; seed <= 100; seed++)
        {
            var plan = StrategicRoomAllocator.Allocate(seed, Act(seed), spec);
            foreach (var row in plan.Topology.Rows)
                foreach (var slot in row.Slots)
                    foreach (var other in plan.Topology.PredecessorsOf(slot.Id)
                        .SelectMany(plan.Topology.SuccessorsOf)
                        .Distinct()
                        .Where(other => other.Value.CompareTo(slot.Id.Value) > 0))
                        if (plan.KindOf(slot.Id) == plan.KindOf(other)
                            && plan.KindOf(slot.Id) != MapNodeKind.Combat
                            && plan.KindOf(slot.Id) != MapNodeKind.Boss)
                            twins++;
        }
        return twins;
    }

    // A HARD CONSTRAINT, not a penalty: the second shop in a row is a room the player has no reason to enter.
    [Fact]
    public void A_shop_never_stands_next_door_to_a_shop()
    {
        var spec = Spec with
        {
            // Enough shops that they would have to crowd if nothing stopped them.
            RoomBudgets = new Dictionary<MapNodeKind, RoomBudget> { [MapNodeKind.Shop] = new() { Target = 10 } },
        };

        var adjacent = 0;
        var permitted = 0;
        for (var seed = 1; seed <= 100; seed++)
        {
            var banned = StrategicRoomAllocator.Allocate(seed, Act(seed), spec);
            adjacent += Neighbours(banned, MapNodeKind.Shop);
            var allowed = StrategicRoomAllocator.Allocate(seed, Act(seed), spec with
            {
                Rules = spec.Rules with { NoRepeatKinds = new HashSet<MapNodeKind>() },
            });
            permitted += Neighbours(allowed, MapNodeKind.Shop);
        }

        Assert.Equal(0, adjacent);
        Assert.True(permitted > 10, $"without the ban only {permitted} shop pairs ended up adjacent");
    }

    private static int Neighbours(StrategicRoomPlan plan, MapNodeKind kind) => plan.Topology.Edges
        .Count(edge => plan.TryKindOf(edge.From, out var from) && from == kind
            && plan.TryKindOf(edge.To, out var to) && to == kind);

    // NOT THE OLD CEILING LOGIC. When every role a room could hold is spent, the room goes to what this ROUTE is
    // most about — and on a route that refuses combat outright, combat is not the answer however convenient it
    // would be.
    [Fact]
    public void The_filler_is_what_the_route_wants_and_never_just_combat()
    {
        IReadOnlyList<MapLaneProfile> hoard =
        [
            Lane("hoard", (MapNodeKind.Combat, 0), (MapNodeKind.Treasure, 9), (MapNodeKind.Event, 1)),
        ];
        // The act itself offers nothing but combat, and this route refuses combat: once the two roles the route
        // does want are spent, every room is a room with no legal role at all.
        var spec = new StrategicRoomSpec
        {
            KindWeights = new Dictionary<MapNodeKind, int> { [MapNodeKind.Combat] = 7 },
            RoomBudgets = new Dictionary<MapNodeKind, RoomBudget>
            {
                [MapNodeKind.Treasure] = new() { Target = 3, Max = 3 },
                [MapNodeKind.Event] = new() { Target = 2, Max = 2 },
            },
        };

        var plan = StrategicRoomAllocator.Allocate(seed: 7, Act(seed: 7, lanes: hoard), spec);

        Assert.Equal(0, plan.Count(MapNodeKind.Combat));
        Assert.NotEmpty(plan.Forced);
        Assert.All(plan.Forced, forced =>
        {
            Assert.Equal(MapNodeKind.Treasure, forced.Kind);
            Assert.Contains("ceiling", forced.Reason);
        });
    }

    // A PROMISE THE ACT CANNOT KEEP IS REPORTED. Two ways to make one impossible: ask for more rooms than the
    // act has, and gate the role so deep that almost no room qualifies.
    [Fact]
    public void A_promise_the_act_cannot_keep_is_reported_rather_than_bent()
    {
        var greedy = StrategicRoomAllocator.Allocate(seed: 3, Act(seed: 3), Spec with
        {
            RoomBudgets = new Dictionary<MapNodeKind, RoomBudget>
            {
                [MapNodeKind.Elite] = new() { Target = 500, Min = 500, Max = 500 },
            },
        });

        Assert.False(greedy.Honoured);
        var shortfall = Assert.Single(greedy.Shortfalls);
        Assert.Equal(MapNodeKind.Elite, shortfall.Kind);
        Assert.Equal(500, shortfall.Wanted);
        Assert.True(shortfall.Missing > 0);
        Assert.Contains("already taken", shortfall.Reason);

        var gated = StrategicRoomAllocator.Allocate(seed: 3, Act(seed: 3), Spec with
        {
            RoleMinimumDepthPercent = new Dictionary<MapNodeKind, int> { [MapNodeKind.Workbench] = 100 },
            RoomBudgets = new Dictionary<MapNodeKind, RoomBudget> { [MapNodeKind.Workbench] = new() { Min = 9, Target = 9 } },
        });

        Assert.False(gated.Honoured);
        Assert.Contains("already taken", Assert.Single(gated.Shortfalls).Reason);
        // The act still holds a full set of rooms — a broken promise is reported, not thrown, and never leaves a
        // hole in the map.
        Assert.Equal(gated.Topology.NodeCount, gated.Kinds.Count);
    }

    // A MINIMUM IS ITSELF THE STATEMENT OF INTENT: an act that says "at least two workbenches" and never
    // mentions workbenches in a weight table gets its two workbenches. An authored 0 would still refuse.
    [Fact]
    public void A_minimum_is_placed_even_where_no_weight_table_mentions_it()
    {
        var spec = Spec with
        {
            RoomBudgets = new Dictionary<MapNodeKind, RoomBudget>
            {
                [MapNodeKind.Workbench] = new() { Min = 2, Target = 2 },
            },
        };

        for (var seed = 1; seed <= 50; seed++)
        {
            var plan = StrategicRoomAllocator.Allocate(seed, Act(seed), spec);
            Assert.Empty(plan.Shortfalls);
            Assert.True(plan.Count(MapNodeKind.Workbench) >= 2, plan.Render());
        }

        // And a weight of 0 on every route that exists is a refusal the minimum does not override.
        IReadOnlyList<MapLaneProfile> refusing =
        [
            Lane("plain", (MapNodeKind.Combat, 7), (MapNodeKind.Workbench, 0)),
        ];
        var refused = StrategicRoomAllocator.Allocate(seed: 1, Act(seed: 1, lanes: refusing), spec);
        Assert.Equal(0, refused.Count(MapNodeKind.Workbench));
        Assert.Equal(MapNodeKind.Workbench, Assert.Single(refused.Shortfalls).Kind);
    }

    [Fact]
    public void The_same_seed_fills_the_same_act_the_same_way()
    {
        for (var seed = 1; seed <= 20; seed++)
            Assert.Equal(
                StrategicRoomAllocator.Allocate(seed, Act(seed), Spec).Render(),
                StrategicRoomAllocator.Allocate(seed, Act(seed), Spec).Render());

        var plans = Enumerable.Range(1, 20)
            .Select(seed => StrategicRoomAllocator.Allocate(
                MapSeedStreams.From(seed).Rooms, Act(seed: 1), Spec).Render())
            .Distinct()
            .Count();
        Assert.True(plans > 10, $"only {plans} of 20 room streams filled the same act differently");
    }

    // THE ROOMS STREAM IS ITS OWN. Two different room draws over ONE act: the rooms differ and the shape and the
    // flavours are letter-for-letter the same, which is what lets a content change be measured at all.
    [Fact]
    public void Retuning_what_stands_in_a_room_cannot_reshape_the_act()
    {
        var profiles = Act(seed: 11);
        var first = StrategicRoomAllocator.Allocate(seed: 101, profiles, Spec);
        var second = StrategicRoomAllocator.Allocate(seed: 202, profiles, Spec);

        Assert.NotEqual(first.Render(), second.Render());
        Assert.Equal(profiles.Topology.Render(), second.Topology.Render());
        Assert.Equal(profiles.Render(), second.Profiles.Render());
        Assert.Equal(Flavours(first), Flavours(second));

        // The flavour column of the picture, without the room letters beside it: the rooms are what changed.
        static string Flavours(StrategicRoomPlan plan) => string.Join("|", plan.Render()
            .Split(Environment.NewLine)
            .Where(line => line.Contains("lanes "))
            .Select(line => line[line.IndexOf("lanes ", StringComparison.Ordinal)..]));
    }

    [Fact]
    public void A_spec_that_contradicts_itself_is_refused()
    {
        var profiles = Act(seed: 1);

        Assert.Throws<ArgumentNullException>(() => StrategicRoomAllocator.Allocate(1, null!, Spec));
        Assert.Throws<ArgumentNullException>(() => StrategicRoomAllocator.Allocate(1, profiles, null!));

        // A ceiling below the minimum, and a target outside the bounds it was given.
        Assert.Throws<ArgumentOutOfRangeException>(() => Allocate(Spec with
        {
            RoomBudgets = new Dictionary<MapNodeKind, RoomBudget> { [MapNodeKind.Elite] = new() { Min = 4, Max = 2 } },
        }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Allocate(Spec with
        {
            RoomBudgets = new Dictionary<MapNodeKind, RoomBudget>
            {
                [MapNodeKind.Elite] = new() { Min = 2, Target = 9, Max = 4 },
            },
        }));

        // The boss rooms are the act's shape, and a Mimic is a realized Treasure: neither is budgeted for.
        Assert.Throws<ArgumentException>(() => Allocate(Spec with
        {
            RoomBudgets = new Dictionary<MapNodeKind, RoomBudget> { [MapNodeKind.Boss] = new() { Target = 1 } },
        }));
        Assert.Throws<ArgumentException>(() => Allocate(Spec with
        {
            RoomBudgets = new Dictionary<MapNodeKind, RoomBudget> { [MapNodeKind.Mimic] = new() { Target = 1 } },
        }));

        Assert.Throws<ArgumentOutOfRangeException>(() => Allocate(Spec with
        {
            KindWeights = new Dictionary<MapNodeKind, int> { [MapNodeKind.Combat] = -1 },
        }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Allocate(Spec with
        {
            RoleMinimumDepthPercent = new Dictionary<MapNodeKind, int> { [MapNodeKind.Shop] = 140 },
        }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Allocate(Spec with
        {
            Rules = new StrategicRoomRules { MaxNeedPercent = 50 },
        }));

        // An act with nothing to fill its rooms with is not an act.
        var empty = Assert.Throws<ArgumentException>(() => StrategicRoomAllocator.Allocate(
            1,
            Act(seed: 1, lanes: [Lane("void", (MapNodeKind.Combat, 0))]),
            new StrategicRoomSpec { KindWeights = new Dictionary<MapNodeKind, int>() }));
        Assert.Contains("no role to fill its rooms with", empty.Message);

        void Allocate(StrategicRoomSpec spec) => StrategicRoomAllocator.Allocate(1, profiles, spec);
    }
}
