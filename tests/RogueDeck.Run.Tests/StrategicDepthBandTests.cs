using RogueDeck.Run;
using Xunit.Abstractions;

namespace RogueDeck.Run.Tests;

// WHERE IN AN ACT A ROLE SITS (map rework S7).
//
// S6 made "how many of a role the act holds" a budget. That number is silent about depth, and a number silent
// about depth is satisfied by every shop standing in the opening third. A band is the sentence that forbids it.
//
// The claim these tests are written to falsify is the one a band exists for: WITHOUT bands the count in a quarter
// of the act is whatever the weights happened to produce — nothing at all in some acts, four times the share in
// others — and WITH them it is inside the authored range in every act. Everything else here is the rules a band
// must NOT break: it never lifts a depth gate, it never overrides the act's own ceiling, a row belongs to exactly
// one band, and a band that asks for nothing changes nothing.
public class StrategicDepthBandTests(ITestOutputHelper output)
{
    private const int Rows = 25;

    private static readonly IReadOnlyList<MapLaneProfile> Lanes =
    [
        Lane("gauntlet", (MapNodeKind.Combat, 9), (MapNodeKind.Elite, 4), (MapNodeKind.Event, 1)),
        Lane("wilds", (MapNodeKind.Combat, 5), (MapNodeKind.Event, 5), (MapNodeKind.Elite, 2), (MapNodeKind.Treasure, 2)),
        Lane("errands", (MapNodeKind.Combat, 3), (MapNodeKind.Event, 2), (MapNodeKind.Shop, 5), (MapNodeKind.Rest, 3)),
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

    private static StrategicStrandProfiles Act(int seed, int rows = Rows) =>
        StrategicStrandProfileAssigner.Assign(seed, StrategicTopologyGenerator.Generate(seed, rows), Lanes);

    private static StrategicRoomPlan Plan(int seed, StrategicRoomSpec spec, int rows = Rows) =>
        StrategicRoomAllocator.Allocate(MapSeedStreams.From(seed).Rooms, Act(seed, rows), spec);

    // The act in quarters, which is the document's own example split.
    private static IReadOnlyList<DepthBandBudget> Quarters(
        params (MapNodeKind Kind, int Min, int Target, int Max)[][] budgets) =>
        Enumerable.Range(0, 4)
            .Select(index => new DepthBandBudget
            {
                StartPercent = index * 25,
                EndPercent = (index + 1) * 25,
                Budgets = budgets[index].ToDictionary(
                    entry => entry.Kind,
                    entry => new RoomBudget { Min = entry.Min, Target = entry.Target, Max = entry.Max }),
            })
            .ToList();

    private static int InBand(StrategicRoomPlan plan, int startPercent, int endPercent, MapNodeKind kind)
    {
        var rows = plan.Topology.Rows.Count;
        return plan.Topology.Slots.Count(slot => !slot.IsBoss
            && MapDepth.Percent(slot.Row, rows) >= startPercent
            && (MapDepth.Percent(slot.Row, rows) < endPercent || (endPercent >= 100 && MapDepth.Percent(slot.Row, rows) >= 100))
            && plan.KindOf(slot.Id) == kind);
    }

    // THE HEADLINE. The same act-wide count of shops, drawn once with nothing said about depth and once with a
    // band range per quarter — and the difference is not the total, it is the spread.
    [Fact]
    public void A_band_stops_a_role_from_clustering_where_an_act_wide_count_cannot()
    {
        const int seeds = 200;
        var loose = Spec with
        {
            RoomBudgets = new Dictionary<MapNodeKind, RoomBudget> { [MapNodeKind.Shop] = new() { Target = 8 } },
        };
        var banded = loose with
        {
            DepthBands = Quarters(
                [(MapNodeKind.Shop, 1, 2, 3)],
                [(MapNodeKind.Shop, 1, 2, 3)],
                [(MapNodeKind.Shop, 1, 2, 3)],
                [(MapNodeKind.Shop, 1, 2, 3)]),
        };

        var looseSpread = new List<int>();
        var bandedSpread = new List<int>();
        for (var seed = 1; seed <= seeds; seed++)
        {
            looseSpread.Add(InBand(Plan(seed, loose), 0, 25, MapNodeKind.Shop));
            bandedSpread.Add(InBand(Plan(seed, banded), 0, 25, MapNodeKind.Shop));
        }

        output.WriteLine($"shops in the first quarter — unbanded {looseSpread.Min()}..{looseSpread.Max()} "
            + $"(avg {looseSpread.Average():F2}) · banded {bandedSpread.Min()}..{bandedSpread.Max()} "
            + $"(avg {bandedSpread.Average():F2})");

        // Unbanded, an act's opening quarter is at the mercy of which flavours opened it: some acts hold none.
        Assert.Equal(0, looseSpread.Min());
        Assert.True(looseSpread.Max() >= 3, $"unbanded spread only reached {looseSpread.Max()}");

        // Banded, every single act is inside the authored range — that is the whole sentence a band adds.
        Assert.All(bandedSpread, count => Assert.InRange(count, 1, 3));
    }

    [Fact]
    public void A_band_ceiling_keeps_a_role_out_of_a_stretch_of_the_act_entirely()
    {
        var spec = Spec with
        {
            RoomBudgets = new Dictionary<MapNodeKind, RoomBudget> { [MapNodeKind.Shop] = new() { Target = 6 } },
            DepthBands =
            [
                new DepthBandBudget
                {
                    StartPercent = 0,
                    EndPercent = 33,
                    Budgets = new Dictionary<MapNodeKind, RoomBudget> { [MapNodeKind.Shop] = new() { Max = 0 } },
                },
            ],
        };

        var shops = 0;
        for (var seed = 1; seed <= 200; seed++)
        {
            var plan = Plan(seed, spec);
            Assert.Equal(0, InBand(plan, 0, 33, MapNodeKind.Shop));
            Assert.Empty(plan.Forced);
            shops += plan.Count(MapNodeKind.Shop);
        }

        // A ceiling of zero in one band is not a ceiling on the act: the shops still get placed, just later.
        output.WriteLine($"{shops / 200.0:F2} shops per act, none of them in the first third");
        Assert.True(shops > 200 * 4, $"only {shops} shops over 200 acts");
    }

    [Fact]
    public void A_band_minimum_is_kept_inside_its_own_band()
    {
        var spec = Spec with
        {
            RoleMinimumDepthPercent = new Dictionary<MapNodeKind, int> { [MapNodeKind.Elite] = 20 },
            // Exactly four elites are allowed, and at least three of them must stand in the act's last quarter.
            // If the two promises did NOT overlap the band would place its three and the act's own minimum would
            // then ask for four more and hit the ceiling — so `Honoured` is what proves they are one count.
            RoomBudgets = new Dictionary<MapNodeKind, RoomBudget>
            {
                [MapNodeKind.Elite] = new() { Min = 4, Target = 4, Max = 4 },
            },
            DepthBands =
            [
                new DepthBandBudget
                {
                    StartPercent = 75,
                    EndPercent = 100,
                    Budgets = new Dictionary<MapNodeKind, RoomBudget> { [MapNodeKind.Elite] = new() { Min = 3, Target = 3 } },
                },
            ],
        };

        for (var seed = 1; seed <= 200; seed++)
        {
            var plan = Plan(seed, spec);
            Assert.True(plan.Honoured, plan.Render());
            Assert.True(InBand(plan, 75, 100, MapNodeKind.Elite) >= 3, plan.Render());
            Assert.Equal(InBand(plan, 75, 100, MapNodeKind.Elite), plan.CountInBand(0, MapNodeKind.Elite));

            // A BAND MINIMUM COUNTS TOWARD THE ACT'S OWN. "At least three elites in the last quarter" plus "at
            // least four in the act" is four elites, not seven — the two sentences overlap and the allocator
            // knows it, because there is one count per role and a band only narrows where it is kept.
            Assert.Equal(4, plan.Count(MapNodeKind.Elite));
        }
    }

    // THE DOCUMENT'S OWN §14 RULE: a band asks within the depth gates and never lifts them.
    [Fact]
    public void A_band_cannot_lift_a_depth_gate_and_says_so_by_name()
    {
        var spec = Spec with
        {
            RoleMinimumDepthPercent = new Dictionary<MapNodeKind, int> { [MapNodeKind.Elite] = 60 },
            DepthBands =
            [
                new DepthBandBudget
                {
                    StartPercent = 0,
                    EndPercent = 25,
                    Budgets = new Dictionary<MapNodeKind, RoomBudget> { [MapNodeKind.Elite] = new() { Min = 2, Target = 2 } },
                },
            ],
        };

        var plan = Plan(seed: 7, spec);
        output.WriteLine(plan.Render());

        Assert.Equal(0, InBand(plan, 0, 60, MapNodeKind.Elite));
        var shortfall = Assert.Single(plan.Shortfalls);
        Assert.Equal(MapNodeKind.Elite, shortfall.Kind);
        Assert.Equal(2, shortfall.Missing);
        Assert.NotNull(shortfall.Band);
        Assert.Equal("0-25 %", shortfall.Band!.Label);
        Assert.Contains("0-25 %", shortfall.ToString());
        Assert.Contains("60 %", shortfall.Reason);
    }

    [Fact]
    public void A_bands_minimum_cannot_push_the_act_past_its_own_ceiling()
    {
        var spec = Spec with
        {
            RoomBudgets = new Dictionary<MapNodeKind, RoomBudget>
            {
                [MapNodeKind.Rest] = new() { Min = 2, Target = 2, Max = 2 },
            },
            DepthBands = Quarters(
                [(MapNodeKind.Rest, 2, 2, 2)],
                [(MapNodeKind.Rest, 2, 2, 2)],
                [],
                []),
        };

        var plan = Plan(seed: 11, spec);
        output.WriteLine(plan.Render());

        // Two bands demand two rests each while the act allows two in all. The act's ceiling wins, and the band
        // that lost says which promise went unkept — it is not quietly rounded away.
        Assert.Equal(2, plan.Count(MapNodeKind.Rest));
        Assert.NotEmpty(plan.Shortfalls);
        Assert.All(plan.Shortfalls, shortfall => Assert.NotNull(shortfall.Band));
    }

    // A ROW BELONGS TO EXACTLY ONE BAND. Quarters written 0-25 / 25-50 must not both claim the 25 % row, and the
    // act's deepest row must not fall out of the bottom of the last one.
    [Fact]
    public void A_row_at_a_bands_edge_belongs_to_the_band_that_starts_there()
    {
        var spec = Spec with { DepthBands = Quarters([], [], [], []) };

        var shallow = new DepthBandBudget { StartPercent = 0, EndPercent = 25 };
        var deeper = new DepthBandBudget { StartPercent = 25, EndPercent = 50 };
        Assert.False(shallow.Contains(25));
        Assert.True(deeper.Contains(25));
        Assert.False(shallow.Overlaps(deeper));

        // And 100 % is inclusive, or the deepest room an act has would sit in no band at all.
        var deepest = new DepthBandBudget { StartPercent = 75, EndPercent = 100 };
        Assert.True(deepest.Contains(100));

        var topology = StrategicTopologyGenerator.Generate(seed: 3, rows: Rows);
        foreach (var slot in topology.Slots.Where(slot => !slot.IsBoss))
        {
            var band = spec.BandIndexOf(slot.Row, Rows);
            Assert.InRange(band, 0, 3);
            Assert.Equal(MapDepth.Percent(slot.Row, Rows) / 25 is 4 ? 3 : MapDepth.Percent(slot.Row, Rows) / 25, band);
        }
    }

    [Fact]
    public void Overlapping_bands_are_refused_rather_than_resolved_by_authoring_order()
    {
        var spec = Spec with
        {
            DepthBands =
            [
                new DepthBandBudget { StartPercent = 0, EndPercent = 50 },
                new DepthBandBudget { StartPercent = 40, EndPercent = 100 },
            ],
        };

        var problem = Assert.Throws<ArgumentException>(() => spec.Validate());
        Assert.Contains("overlap", problem.Message);
    }

    [Theory]
    [InlineData(50, 50)]
    [InlineData(60, 40)]
    [InlineData(-1, 50)]
    [InlineData(0, 101)]
    public void A_band_that_cannot_hold_a_row_by_its_own_numbers_is_refused(int start, int end)
    {
        var band = new DepthBandBudget { StartPercent = start, EndPercent = end };
        Assert.ThrowsAny<ArgumentException>(() => band.Validate());
    }

    [Fact]
    public void A_band_never_budgets_for_a_boss_or_a_mimic()
    {
        foreach (var kind in new[] { MapNodeKind.Boss, MapNodeKind.Mimic })
        {
            var band = new DepthBandBudget
            {
                StartPercent = 0,
                EndPercent = 50,
                Budgets = new Dictionary<MapNodeKind, RoomBudget> { [kind] = new() { Min = 1, Target = 1 } },
            };
            Assert.Throws<ArgumentException>(() => band.Validate());
        }
    }

    // THE REGRESSION GUARD ON S6. Bands enter the score as a factor, so a spec whose bands ask for nothing must
    // produce the byte-identical act it did before bands existed — otherwise every recorded seed moved.
    [Fact]
    public void Bands_that_ask_for_nothing_change_nothing()
    {
        var budgets = new Dictionary<MapNodeKind, RoomBudget>
        {
            [MapNodeKind.Elite] = new() { Min = 4, Target = 7, Max = 9 },
            [MapNodeKind.Shop] = new() { Min = 2, Target = 4, Max = 5 },
            [MapNodeKind.Treasure] = new() { Target = 5 },
        };
        var unbanded = Spec with { RoomBudgets = budgets };
        var banded = unbanded with { DepthBands = Quarters([], [], [], []) };

        for (var seed = 1; seed <= 300; seed++)
            Assert.Equal(Plan(seed, unbanded).Render(), Plan(seed, banded).Render());
    }

    [Fact]
    public void The_same_seed_bands_the_same_act_the_same_way()
    {
        var spec = Spec with
        {
            RoomBudgets = new Dictionary<MapNodeKind, RoomBudget> { [MapNodeKind.Shop] = new() { Target = 8 } },
            DepthBands = Quarters(
                [(MapNodeKind.Shop, 1, 2, 3)],
                [(MapNodeKind.Shop, 1, 2, 3)],
                [(MapNodeKind.Shop, 1, 2, 3)],
                [(MapNodeKind.Shop, 1, 2, 3)]),
        };

        for (var seed = 1; seed <= 300; seed++)
            Assert.Equal(Plan(seed, spec).Render(), Plan(seed, spec).Render());
    }

    [Fact]
    public void One_banded_act_read_out_loud()
    {
        var spec = Spec with
        {
            RoleMinimumDepthPercent = new Dictionary<MapNodeKind, int>
            {
                [MapNodeKind.Elite] = 20,
                [MapNodeKind.Shop] = 15,
            },
            RoomBudgets = new Dictionary<MapNodeKind, RoomBudget>
            {
                [MapNodeKind.Elite] = new() { Min = 4, Target = 7, Max = 9 },
                [MapNodeKind.Shop] = new() { Min = 3, Target = 5, Max = 6 },
                [MapNodeKind.Rest] = new() { Min = 2, Target = 4, Max = 5 },
                [MapNodeKind.Treasure] = new() { Target = 5, Max = 7 },
            },
            DepthBands = Quarters(
                [(MapNodeKind.Elite, 0, 0, 1), (MapNodeKind.Shop, 0, 1, 2)],
                [(MapNodeKind.Elite, 1, 2, 3), (MapNodeKind.Shop, 1, 2, 2)],
                [(MapNodeKind.Elite, 1, 2, 3), (MapNodeKind.Shop, 1, 2, 2)],
                [(MapNodeKind.Elite, 1, 3, 4), (MapNodeKind.Shop, 0, 1, 2)]),
        };

        var plan = Plan(seed: 20260911, spec);
        output.WriteLine(plan.Render());
        Assert.True(plan.Honoured, plan.Render());
    }
}
