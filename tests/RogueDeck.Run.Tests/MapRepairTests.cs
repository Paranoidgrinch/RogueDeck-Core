using RogueDeck.Run;
using Xunit.Abstractions;

namespace RogueDeck.Run.Tests;

// THE DETERMINISTIC REPAIR (map rework S10).
//
// The source document asks a repair to be provable rather than plausible (§42): the act keeps its rooms, gains
// no illegal placement, keeps or improves its budgets, and does the same thing twice. Those are the fuzz tests
// at the bottom. The hand-built act at the top is the document's own worked example — a fork offering the same
// shop twice, and an event standing somewhere it decides nothing — which a single swap turns into a choice
// without changing what the act holds.
public class MapRepairTests(ITestOutputHelper output)
{
    private static readonly MapLaneProfile Lane = new("plain", new Dictionary<MapNodeKind, int>
    {
        [MapNodeKind.Combat] = 5,
        [MapNodeKind.Event] = 4,
        [MapNodeKind.Shop] = 3,
        [MapNodeKind.Rest] = 2,
        [MapNodeKind.Elite] = 2,
        [MapNodeKind.Treasure] = 2,
    });

    //   r0        a C
    //           /     \
    //   r1     b $     c $      ← the fork: a shop either way, so nothing is decided
    //          |       |
    //   r2     d ?     e C      ← the event that would make it a choice, standing where it makes none
    //           \     /
    //   r3        f B
    private static StrategicRoomPlan ByHand(params (string Room, MapNodeKind Kind)[] rooms)
    {
        StrategicSlot Slot(string id, int row, int column, bool boss = false) => new()
        {
            Id = new NodeId(id),
            Row = row,
            Column = column,
            Strand = new StrandId(column),
            IsBoss = boss,
        };

        var rows = new List<StrategicRow>
        {
            new() { Index = 0, Slots = [Slot("a", 0, 0)] },
            new() { Index = 1, Slots = [Slot("b", 1, 0), Slot("c", 1, 1)] },
            new() { Index = 2, Slots = [Slot("d", 2, 0), Slot("e", 2, 1)] },
            new() { Index = 3, Slots = [Slot("f", 3, 0, boss: true)] },
        };
        var edges = new List<MapEdge>
        {
            new(new NodeId("a"), new NodeId("b")), new(new NodeId("a"), new NodeId("c")),
            new(new NodeId("b"), new NodeId("d")), new(new NodeId("c"), new NodeId("e")),
            new(new NodeId("d"), new NodeId("f")), new(new NodeId("e"), new NodeId("f")),
        };
        var strands = new List<StrategicStrand>
        {
            new() { Id = new StrandId(0), BornRow = 0, LastRow = 3 },
            new() { Id = new StrandId(1), BornRow = 1, LastRow = 2, ParentId = new StrandId(0) },
        };

        var topology = new StrategicTopology(seed: 1, rows, edges, strands);
        var profiles = new StrategicStrandProfiles(topology, [Lane],
        [
            new StrandProfileSpan { Strand = new StrandId(0), FromRow = 0, ProfileIndex = 0, Profile = Lane },
            new StrandProfileSpan { Strand = new StrandId(1), FromRow = 1, ProfileIndex = 0, Profile = Lane },
        ]);

        var kinds = rooms.ToDictionary(room => new NodeId(room.Room), room => room.Kind);
        kinds[new NodeId("f")] = MapNodeKind.Boss;
        return new StrategicRoomPlan(profiles, new StrategicRoomSpec { KindWeights = Lane.KindWeights }, kinds, [], []);
    }

    // Horizon 1, so the choice is about the two rooms the fork leads into and nothing else — the shape the
    // document's example is written in.
    private static readonly ForkQualityRules Forks = new() { HorizonRows = 1, MinimumContrast = 50 };

    [Fact]
    public void A_fork_offering_the_same_shop_twice_is_mended_by_one_swap()
    {
        var plan = ByHand(
            ("a", MapNodeKind.Combat), ("b", MapNodeKind.Shop), ("c", MapNodeKind.Shop),
            ("d", MapNodeKind.Event), ("e", MapNodeKind.Combat));

        var repaired = MapRepair.Repair(plan, new PathPressureRules(), Forks, new RepairRules());
        output.WriteLine(string.Join(Environment.NewLine, repaired.Operations));

        // One swap: a shop for the event, so the fork offers a shop one way and an event the other.
        var operation = Assert.Single(repaired.Operations);
        Assert.Equal(RepairKind.Swap, operation.Operation);
        Assert.True(repaired.Clean);

        // AND THE ACT STILL HOLDS WHAT IT HELD: two shops and one event, which is what makes a swap the
        // preferred repair — it cannot break a count, because it does not change one.
        Assert.Equal(2, repaired.Plan.Count(MapNodeKind.Shop));
        Assert.Equal(1, repaired.Plan.Count(MapNodeKind.Event));
        Assert.Equal(plan.Kinds.Count, repaired.Plan.Kinds.Count);

        var fork = ForkQualityEvaluator.Measure(repaired.Plan, Forks).Forks.Single();
        Assert.True(fork.Contrast >= 50, $"the mended fork is still only worth {fork.Contrast}");
    }

    [Fact]
    public void An_act_with_nothing_wrong_with_it_is_left_alone()
    {
        var plan = ByHand(
            ("a", MapNodeKind.Combat), ("b", MapNodeKind.Shop), ("c", MapNodeKind.Elite),
            ("d", MapNodeKind.Event), ("e", MapNodeKind.Combat));

        var repaired = MapRepair.Repair(plan, new PathPressureRules(), Forks, new RepairRules());

        Assert.Empty(repaired.Operations);
        Assert.Equal(plan.Kinds, repaired.Plan.Kinds);
    }

    // A ROUTE BELOW THE FLOOR IS MENDED BY MOVING DANGER ONTO IT, never by inventing any. The act holds one
    // elite before and one after; what changes is that it now stands where every route meets it, which is the
    // whole difference between an act that asks something of a player and an act that can be walked round.
    [Fact]
    public void A_route_that_walks_round_everything_is_mended_by_moving_the_danger_onto_it()
    {
        var plan = ByHand(
            ("a", MapNodeKind.Combat), ("b", MapNodeKind.Elite), ("c", MapNodeKind.Rest),
            ("d", MapNodeKind.Combat), ("e", MapNodeKind.Event));

        // The campfire route is worth 10 of the 30 promised: one fight, then nothing that asks anything at all.
        var pressure = new PathPressureRules { Minimum = 30 };
        Assert.Equal(10, StrategicPathPressure.Measure(plan, pressure).Thinnest.Pressure);

        var repaired = MapRepair.Repair(plan, pressure, new ForkQualityRules(), new RepairRules());
        output.WriteLine(string.Join(Environment.NewLine, repaired.Operations));

        Assert.True(StrategicPathPressure.Measure(repaired.Plan, pressure).Held);
        Assert.Equal(1, repaired.Plan.Count(MapNodeKind.Elite));
        Assert.Equal(plan.Count(MapNodeKind.Combat), repaired.Plan.Count(MapNodeKind.Combat));
        Assert.Equal(plan.Count(MapNodeKind.Rest), repaired.Plan.Count(MapNodeKind.Rest));
    }

    // ————— generated acts —————

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

    // An act that demands things: a floor of challenge near what v0.0.0 asks of an Act I route, forks that have
    // to decide something, and budgets with real minimums.
    private static StrategicActSpec Demanding => new()
    {
        Rows = 23,
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
        PathPressure = new PathPressureRules { Minimum = 120 },
        ForkQuality = new ForkQualityRules { MinimumContrast = 40 },
    };

    private static StrategicRoomPlan Allocated(StrategicActSpec spec, int seed) => StrategicRoomAllocator.Allocate(
        MapSeedStreams.From(seed).Rooms,
        StrategicStrandProfileAssigner.Assign(
            MapSeedStreams.From(seed).Strands,
            StrategicTopologyGenerator.Generate(
                MapSeedStreams.From(seed).Topology, spec.Rows, spec.BossRooms, spec.MinWidth, spec.MaxWidth, spec.Topology),
            spec.LaneProfiles,
            spec.LaneAssignment),
        spec.Rooms);

    // EVERY INVARIANT THE SOURCE DOCUMENT ASKS OF A REPAIR (§42), on two hundred acts: the rooms are the same
    // rooms, the shape is untouched, no ceiling is broken, nothing legal became illegal, and the act is never
    // worse than it was.
    [Fact]
    public void A_repair_never_makes_an_act_worse_and_never_changes_its_shape()
    {
        var spec = Demanding;
        var improved = 0;
        var mended = 0;
        var operations = 0;

        for (var seed = 1; seed <= 200; seed++)
        {
            var before = Allocated(spec, seed);
            var repaired = MapRepair.Repair(before, spec.PathPressure, spec.ForkQuality, spec.Repair);
            var after = repaired.Plan;

            Assert.Equal(before.Kinds.Count, after.Kinds.Count);
            Assert.Equal(before.Topology, after.Topology);
            Assert.True(repaired.After <= repaired.Before, $"seed {seed} came out worse than it went in");

            foreach (var slot in after.Topology.Slots)
                Assert.Equal(slot.IsBoss, after.KindOf(slot.Id) == MapNodeKind.Boss);

            foreach (var (kind, budget) in spec.Rooms.RoomBudgets)
                Assert.True(after.Count(kind) <= budget.Max,
                    $"seed {seed} left {after.Count(kind)} {kind} rooms against a ceiling of {budget.Max}");

            if (repaired.After < repaired.Before)
                improved++;
            if (repaired.Clean)
                mended++;
            operations += repaired.Operations.Count;
        }

        output.WriteLine($"200 acts · {improved} improved · {mended} left clean · {operations} operations "
            + $"({operations / 200.0:F1} per act)");
        // Most acts the repair can finish on its own; the rest are what the generator's retries are for, and
        // that division of labour is the number to watch in S12 when the BnB acts get their real thresholds.
        Assert.True(improved > 0, "the repair never improved a single act");
        Assert.True(mended >= 180, $"only {mended} of 200 acts came out clean");
    }

    [Fact]
    public void The_same_act_repairs_the_same_way_twice()
    {
        var spec = Demanding;
        for (var seed = 1; seed <= 20; seed++)
        {
            var first = MapRepair.Repair(Allocated(spec, seed), spec.PathPressure, spec.ForkQuality, spec.Repair);
            var second = MapRepair.Repair(Allocated(spec, seed), spec.PathPressure, spec.ForkQuality, spec.Repair);
            Assert.Equal(
                string.Join("|", first.Operations.Select(operation => operation.ToString())),
                string.Join("|", second.Operations.Select(operation => operation.ToString())));
            Assert.Equal(first.Plan.Render(), second.Plan.Render());
        }
    }

    // An act that promises nothing cannot fail to keep a promise — so the defaults never repair anything, and
    // a generator on the defaults never throws.
    [Fact]
    public void An_act_that_promises_nothing_is_never_repaired()
    {
        var spec = new StrategicActSpec { Rows = 23, LaneProfiles = Lanes };
        for (var seed = 1; seed <= 50; seed++)
        {
            var repaired = MapRepair.Repair(
                Allocated(spec, seed), spec.PathPressure, spec.ForkQuality, spec.Repair);
            Assert.Empty(repaired.Operations);
            Assert.True(repaired.Clean);
        }
    }
}
