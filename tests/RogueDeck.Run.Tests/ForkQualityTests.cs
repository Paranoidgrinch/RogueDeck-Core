using RogueDeck.Run;
using Xunit.Abstractions;

namespace RogueDeck.Run.Tests;

// WHETHER A FORK IS A CHOICE (map rework S9).
//
// The three cases below are the source document's own (§41), and they are hand-drawn for the usual reason: a
// fork score computed on a generated act is a plausible number whatever it is. The third is the one that proves
// the decision horizon is doing work rather than being carried as a parameter — two branches that begin the same
// way and end differently are the same choice at horizon 1 and a real one at horizon 2.
public class ForkQualityTests(ITestOutputHelper output)
{
    private static readonly NodeType Mark = new("test.mark");
    private static readonly ForkQualityRules Rules = new();

    // "a>b" edges, and a role per room. Rooms are named in the order they are added, which is the order a fork's
    // branches come back in.
    private static (RunMap Map, Dictionary<NodeId, MapNodeKind> Roles) Graph(
        IReadOnlyList<(string Id, MapNodeKind Role)> rooms, params string[] edges)
    {
        var builder = new RunMapBuilder();
        var roles = new Dictionary<NodeId, MapNodeKind>();
        foreach (var (id, role) in rooms)
        {
            builder.AddNode(new NodeId(id), Mark, id);
            roles[new NodeId(id)] = role;
        }
        foreach (var edge in edges)
        {
            var ends = edge.Split('>');
            builder.Connect(ends[0], ends[1]);
        }
        return (builder.Build(), roles);
    }

    private static ForkQualityReport Measure(
        (RunMap Map, Dictionary<NodeId, MapNodeKind> Roles) graph, ForkQualityRules? rules = null) =>
        ForkQualityEvaluator.Measure(new GeneratedMap(graph.Map, graph.Roles), rules ?? Rules);

    //        ? → C
    //   fork
    //        ? → C        both ways are the same walk, so nothing was decided
    [Fact]
    public void A_fork_between_two_identical_futures_is_worth_nothing()
    {
        var report = Measure(Graph(
            [("fork", MapNodeKind.Combat), ("l0", MapNodeKind.Event), ("l1", MapNodeKind.Combat),
             ("r0", MapNodeKind.Event), ("r1", MapNodeKind.Combat)],
            "fork>l0", "fork>r0", "l0>l1", "r0>r1"));

        var fork = Assert.Single(report.Forks);
        Assert.Equal(0, fork.Contrast);
        Assert.Equal(1, report.Hollow);
    }

    //        E → C        danger and loot
    //   fork
    //        R → $        a breather and a purse
    [Fact]
    public void A_fork_between_a_gauntlet_and_an_errand_is_worth_the_difference()
    {
        var report = Measure(Graph(
            [("fork", MapNodeKind.Combat), ("l0", MapNodeKind.Elite), ("l1", MapNodeKind.Combat),
             ("r0", MapNodeKind.Rest), ("r1", MapNodeKind.Shop)],
            "fork>l0", "fork>r0", "l0>l1", "r0>r1"));

        var fork = Assert.Single(report.Forks);

        // Elite+Combat is (d40 r35), Rest+Shop is (h30 $30 r10): 40 + 30 + 30 + 25 apart, all dimensions
        // weighted 1. Counted by hand, because that is the only way to know the metric is the one intended.
        Assert.Equal(125, fork.Contrast);
        Assert.Equal(0, report.Hollow);
    }

    // THE HORIZON DOING REAL WORK. Both ways begin with a fight; one ends at an elite and the other at a
    // campfire. A generator judging by the next room alone calls this no choice at all.
    [Fact]
    public void A_choice_that_only_shows_itself_a_row_later_needs_a_horizon_to_see()
    {
        var graph = Graph(
            [("fork", MapNodeKind.Event), ("l0", MapNodeKind.Combat), ("l1", MapNodeKind.Elite),
             ("r0", MapNodeKind.Combat), ("r1", MapNodeKind.Rest)],
            "fork>l0", "fork>r0", "l0>l1", "r0>r1");

        Assert.Equal(0, Assert.Single(Measure(graph, Rules with { HorizonRows = 1 }).Forks).Contrast);

        // Combat+Elite is (d40 r35), Combat+Rest is (d10 h30 r5): 30 + 30 + 30.
        Assert.Equal(90, Assert.Single(Measure(graph, Rules with { HorizonRows = 2 }).Forks).Contrast);
    }

    // A THREE-WAY FORK IS ONLY AS GOOD AS ITS MOST REDUNDANT PAIR: two identical ways are a decoy, and a decoy
    // is thought spent for nothing however good the third way is.
    [Fact]
    public void A_fork_with_a_decoy_is_reported_by_its_weakest_pair_and_its_sharpest()
    {
        var report = Measure(Graph(
            [("fork", MapNodeKind.Combat), ("a", MapNodeKind.Elite), ("b", MapNodeKind.Elite),
             ("c", MapNodeKind.Rest)],
            "fork>a", "fork>b", "fork>c"), Rules with { HorizonRows = 1 });

        var fork = Assert.Single(report.Forks);
        Assert.Equal(3, fork.Ways);
        Assert.Equal(0, fork.Contrast);
        Assert.Equal(90, fork.Widest); // Elite (d30 r30) against Rest (h30): 30 + 30 + 30
        Assert.Equal(1, report.Hollow);
    }

    // A BRANCH THAT SPLITS AGAIN IS AN EXPECTATION, not a sum: what it holds is averaged over the futures it
    // leads to, weighted by how many there are, or a branchier way on would look richer for being branchier.
    [Fact]
    public void A_branch_that_splits_again_is_worth_what_its_futures_are_worth_on_average()
    {
        var report = Measure(Graph(
            [("fork", MapNodeKind.Event), ("l0", MapNodeKind.Combat), ("l1", MapNodeKind.Elite),
             ("l2", MapNodeKind.Rest), ("r0", MapNodeKind.Combat), ("r1", MapNodeKind.Combat)],
            "fork>l0", "fork>r0", "l0>l1", "l0>l2", "r0>r1"), Rules with { HorizonRows = 2 });

        // `l0` splits again, so it is a fork of its own and the report holds both.
        var fork = report.Forks.Single(entry => entry.Room.Value == "fork");
        var branch = fork.Branches.Single(way => way.Successor.Value == "l0");

        Assert.Equal(2, branch.Futures);
        // Combat, then half an Elite and half a campfire: d(10+15) h15 r(5+15).
        Assert.Equal(new ChoiceSignature(Danger: 25, Recovery: 15, Economy: 0, Reward: 20, Variance: 0),
            branch.Expected);
    }

    // A weight of zero silences a dimension. The one knob that lets an author ask "are these two ways different
    // in DANGER", which is a different question from "are they different".
    [Fact]
    public void A_dimension_weighted_nothing_is_not_part_of_the_difference()
    {
        var graph = Graph(
            [("fork", MapNodeKind.Combat), ("l", MapNodeKind.Rest), ("r", MapNodeKind.Shop)],
            "fork>l", "fork>r");

        // Rest (h30) against Shop ($30 r10): 30 + 30 + 10 with everything weighted.
        Assert.Equal(70, Assert.Single(Measure(graph, Rules with { HorizonRows = 1 }).Forks).Contrast);

        // Neither is dangerous, so a question only about danger has the same answer for both.
        var danger = Rules with { HorizonRows = 1, DimensionWeights = new ChoiceSignature(1, 0, 0, 0, 0) };
        Assert.Equal(0, Assert.Single(Measure(graph, danger).Forks).Contrast);
    }

    [Fact]
    public void A_horizon_longer_than_the_act_is_refused_rather_than_counted()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ForkQualityRules { HorizonRows = 0 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ForkQualityRules { HorizonRows = ForkQualityRules.MaximumHorizonRows + 1 }.Validate());
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

    [Fact]
    public void The_same_seed_ranks_the_same_forks_twice()
    {
        Assert.Equal(
            ForkQualityEvaluator.Measure(Plan(seed: 4711), Rules).Render(),
            ForkQualityEvaluator.Measure(Plan(seed: 4711), Rules).Render());
    }

    // WHAT THE STRATEGIC GENERATOR'S FORKS ARE WORTH, as a number rather than as a hope — the measurement S13's
    // seed report is built on, and the baseline S10's repair will have to improve.
    [Fact]
    public void The_forks_of_a_generated_act_are_mostly_real_choices()
    {
        var contrasts = new List<int>();
        var hollow = 0;
        var forks = 0;
        for (var seed = 1; seed <= 500; seed++)
        {
            var report = ForkQualityEvaluator.Measure(Plan(seed), Rules);
            contrasts.AddRange(report.Forks.Select(fork => fork.Contrast));
            hollow += report.Hollow;
            forks += report.Count;
        }

        output.WriteLine($"23-row act, 500 seeds · {forks} forks · contrast {contrasts.Min()}..{contrasts.Max()}"
            + $" (mean {contrasts.Average():F1}) · hollow {hollow} ({hollow * 100.0 / forks:F1} %)");

        // A fork that decides nothing is a defect, not a style: the generator may draw a few and must not draw
        // mostly those. The threshold is deliberately loose — S10 is what will tighten it.
        Assert.True(hollow * 100.0 / forks < 10, $"{hollow * 100.0 / forks:F1} % of forks decide nothing");
        Assert.True(contrasts.Average() > 40, $"the average fork is worth only {contrasts.Average():F1}");
    }
}
