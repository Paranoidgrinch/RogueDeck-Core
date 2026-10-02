using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Playtest feedback 2, D2/D3: every route through an act is asked which way often enough, and every route ends on
// a campfire before the boss.
public class StrategicDecisionsTests
{
    private static readonly StrategicTopologyRules Forked = new()
    {
        ContinueWeight = 3,
        SplitWeight = 3,
        MergeWeight = 3,
        MinForksPerRoute = 5,
        ForkFreeTailRows = 1,
    };

    // The fewest rooms with two or more ways on that any entry-to-boss route passes, counting only rooms whose
    // next row is not the fork-free tail.
    private static int FewestForks(StrategicTopology topology, int forkRows)
    {
        var fewest = new Dictionary<NodeId, int>();
        foreach (var row in topology.Rows.Where(row => row.Slots.All(slot => !slot.IsBoss)))
            foreach (var slot in row.Slots)
            {
                var before = topology.PredecessorsOf(slot.Id).Select(id => fewest[id]).DefaultIfEmpty(0).Min();
                fewest[slot.Id] = before + (slot.Row < forkRows && topology.SuccessorsOf(slot.Id).Count >= 2 ? 1 : 0);
            }
        var last = topology.Rows.Last(row => row.Slots.All(slot => !slot.IsBoss));
        return last.Slots.Min(slot => fewest[slot.Id]);
    }

    [Theory]
    [InlineData(23)]
    [InlineData(24)]
    [InlineData(25)]
    [InlineData(35)]
    public void Every_route_forks_at_least_the_promised_times_and_the_act_stays_sound(int rows)
    {
        var failures = new List<string>();
        var short_ = 0;
        for (var seed = 1; seed <= 2000; seed++)
        {
            var topology = StrategicTopologyGenerator.Generate(seed, rows, 1, 2, 4, Forked);
            var problems = StrategicTopologyValidator.Validate(topology, rows, 1, 2, 4, Forked);
            var fewest = FewestForks(topology, rows - 1 - 1 - Forked.ForkFreeTailRows);
            Assert.Equal(fewest, StrategicTopologyGenerator.FewestForksPerRoute(topology, Forked));
            if (fewest < Forked.MinForksPerRoute)
                short_++;
            // Always sound; never more than two forks short where the walk leaves no room for a crossway.
            if ((problems.Count > 0 || fewest < Forked.MinForksPerRoute - 2) && failures.Count < 5)
                failures.Add($"seed {seed}: fewest {fewest} · {string.Join(" / ", problems)}");
        }
        Assert.Empty(failures);
        // The walk alone falls short now and then (measured: 53 of 2000 at 23 rows); the generator retries those.
        Assert.True(short_ <= 100, $"{short_} of 2000 acts fell short of the promise");
    }

    // The direction, not the value: without the promise the same walks leave routes that fork twice or less.
    [Fact]
    public void Without_the_promise_some_routes_fork_rarely()
    {
        var rules = Forked with { MinForksPerRoute = 0 };
        var thin = Enumerable.Range(1, 500)
            .Count(seed => FewestForks(StrategicTopologyGenerator.Generate(seed, 23, 1, 2, 4, rules), 23 - 3) < 3);
        Assert.True(thin > 50, $"only {thin} of 500 walks had a route forking under three times");
    }

    [Fact]
    public void The_promise_never_changes_the_rooms_only_adds_ways()
    {
        var plain = StrategicTopologyGenerator.Generate(7, 23, 1, 2, 4, Forked with { MinForksPerRoute = 0 });
        var forked = StrategicTopologyGenerator.Generate(7, 23, 1, 2, 4, Forked);
        Assert.Equal(plain.Widths, forked.Widths);
        Assert.Superset(plain.Edges.ToHashSet(), forked.Edges.ToHashSet());
    }

    [Fact]
    public void The_last_row_before_the_boss_is_the_fixed_kind_and_the_repair_keeps_it()
    {
        var spec = new StrategicActSpec
        {
            Rows = 23,
            Topology = Forked,
            LaneProfiles = [new MapLaneProfile("all", new Dictionary<MapNodeKind, int>
            {
                [MapNodeKind.Combat] = 6, [MapNodeKind.Event] = 3, [MapNodeKind.Elite] = 2, [MapNodeKind.Rest] = 1,
                [MapNodeKind.Shop] = 1,
            })],
            Rooms = new StrategicRoomSpec
            {
                KindWeights = new Dictionary<MapNodeKind, int> { [MapNodeKind.Combat] = 1 },
                PreBossKind = MapNodeKind.Rest,
                Rules = new StrategicRoomRules { NoRepeatKinds = new HashSet<MapNodeKind> { MapNodeKind.Rest } },
            },
        };
        for (var seed = 1; seed <= 200; seed++)
        {
            var act = StrategicMapGenerator.Generate(spec, seed);
            var lastRow = act.Topology.Slots.Where(slot => !slot.IsBoss).Max(slot => slot.Row);
            Assert.All(act.Topology.Slots.Where(slot => slot.Row == lastRow),
                slot => Assert.Equal(MapNodeKind.Rest, act.Plan.Kinds[slot.Id]));
            Assert.All(act.Topology.Slots.Where(slot => slot.Row == lastRow - 1),
                slot => Assert.NotEqual(MapNodeKind.Rest, act.Plan.Kinds[slot.Id]));
        }
    }

    // …and the whole generator, which retries a thin act, keeps the promise on every seed.
    [Fact]
    public void The_generator_keeps_the_fork_promise_on_every_seed()
    {
        var spec = new StrategicActSpec
        {
            Rows = 23,
            Topology = Forked,
            LaneProfiles = [new MapLaneProfile("all", new Dictionary<MapNodeKind, int> { [MapNodeKind.Combat] = 1 })],
        };
        for (var seed = 1; seed <= 2000; seed++)
            Assert.True(StrategicTopologyGenerator.FewestForksPerRoute(
                StrategicMapGenerator.Generate(spec, seed).Topology, Forked) >= Forked.MinForksPerRoute, $"seed {seed}");
    }
}
