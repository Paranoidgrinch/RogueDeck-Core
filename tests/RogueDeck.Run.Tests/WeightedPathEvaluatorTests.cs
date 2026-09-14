using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// THE WEIGHTED PATH DP, ON GRAPHS WHOSE ANSWER CAN BE COUNTED BY HAND (map rework S2).
//
// This is the piece every later aggregate constraint rests on — PathPressure, reward density, recovery density —
// so it is proved on diamonds and ladders rather than on generated maps, where a plausible number and a correct
// number look the same. It also has to keep giving the per-path COUNT checker its old answers to the unit, since
// MapConstraintValidator now runs on top of it.
public class WeightedPathEvaluatorTests
{
    private static readonly NodeType Mark = new("test.mark");

    // Builds a graph from "from>to" edges and a role per room. Entries are the rooms nothing leads into unless
    // named explicitly, which is how a generated map behaves.
    private static (RunMap Map, Dictionary<NodeId, MapNodeKind> Roles) Graph(
        IReadOnlyDictionary<string, MapNodeKind> rooms, params string[] edges)
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

    // Weight per ROLE — the shape PathPressure will use.
    private static Func<NodeId, MapNodeKind, double> By(IReadOnlyDictionary<MapNodeKind, double> weights) =>
        (_, kind) => weights.GetValueOrDefault(kind);

    private static readonly Dictionary<MapNodeKind, double> Challenge = new()
    {
        [MapNodeKind.Combat] = 1.0,
        [MapNodeKind.MultiCombat] = 1.5,
        [MapNodeKind.Elite] = 2.5,
    };

    //        Elite
    //       /     \
    // Start        Boss
    //       \     /
    //        Shop
    [Fact]
    public void The_cheapest_way_round_an_elite_is_worth_what_the_elite_is_not()
    {
        var (map, roles) = Graph(
            new Dictionary<string, MapNodeKind>
            {
                ["start"] = MapNodeKind.Event,
                ["elite"] = MapNodeKind.Elite,
                ["shop"] = MapNodeKind.Shop,
                ["boss"] = MapNodeKind.Boss,
            },
            "start>elite", "start>shop", "elite>boss", "shop>boss");

        var weight = By(new Dictionary<MapNodeKind, double> { [MapNodeKind.Elite] = 3 });

        Assert.Equal(0, WeightedPathEvaluator.MinimumPathScore(map, roles, weight));
        Assert.Equal(3, WeightedPathEvaluator.MaximumPathScore(map, roles, weight));
    }

    // A room every route crosses is paid for once, not once per route that crosses it.
    [Fact]
    public void A_room_every_route_crosses_counts_once()
    {
        var (map, roles) = Graph(
            new Dictionary<string, MapNodeKind>
            {
                ["start"] = MapNodeKind.Combat,
                ["left"] = MapNodeKind.Event,
                ["right"] = MapNodeKind.Event,
                ["boss"] = MapNodeKind.Elite,
            },
            "start>left", "start>right", "left>boss", "right>boss");

        var weight = By(Challenge);

        // Combat 1 + Elite 2.5 on either way round — the events are worth nothing.
        Assert.Equal(3.5, WeightedPathEvaluator.MinimumPathScore(map, roles, weight));
        Assert.Equal(3.5, WeightedPathEvaluator.MaximumPathScore(map, roles, weight));
    }

    // Two forks in a row: the worst and best routes are each a CHOICE at every fork, not the worst or best room.
    [Fact]
    public void Nested_forks_compound_rather_than_being_judged_one_room_at_a_time()
    {
        var (map, roles) = Graph(
            new Dictionary<string, MapNodeKind>
            {
                ["start"] = MapNodeKind.Rest,
                ["a"] = MapNodeKind.Elite,          // 2.5
                ["b"] = MapNodeKind.Combat,         // 1.0
                ["a1"] = MapNodeKind.MultiCombat,   // 1.5
                ["a2"] = MapNodeKind.Rest,          // 0
                ["b1"] = MapNodeKind.Combat,        // 1.0
                ["boss"] = MapNodeKind.Rest,
            },
            "start>a", "start>b", "a>a1", "a>a2", "b>b1", "a1>boss", "a2>boss", "b1>boss");

        var weight = By(Challenge);

        Assert.Equal(2.0, WeightedPathEvaluator.MinimumPathScore(map, roles, weight)); // b + b1
        Assert.Equal(4.0, WeightedPathEvaluator.MaximumPathScore(map, roles, weight)); // a + a1
    }

    // Several opening rooms (a generated act has 2-4): the answer is over all of them, not over the first.
    [Fact]
    public void Several_opening_rooms_are_all_considered()
    {
        var builder = new RunMapBuilder();
        var roles = new Dictionary<NodeId, MapNodeKind>();
        foreach (var (id, role) in new[]
        {
            ("easy", MapNodeKind.Event), ("hard", MapNodeKind.Elite), ("boss", MapNodeKind.Boss),
        })
        {
            builder.AddNode(new NodeId(id), Mark, id);
            roles[new NodeId(id)] = role;
        }
        builder.Entry("easy").Entry("hard");
        builder.Connect("easy", "boss").Connect("hard", "boss");
        var map = builder.Build();

        var weight = By(Challenge);
        Assert.Equal(0, WeightedPathEvaluator.MinimumPathScore(map, roles, weight));
        Assert.Equal(2.5, WeightedPathEvaluator.MaximumPathScore(map, roles, weight));
    }

    [Fact]
    public void A_single_room_with_nowhere_to_go_is_worth_itself()
    {
        // With or without a declared entry. A map with no edges has no node with an incoming one, so RunMap
        // treats every room as a root — which is the right reading here: a one-room act is one room to walk.
        var (map, roles) = Graph(new Dictionary<string, MapNodeKind> { ["only"] = MapNodeKind.Elite });
        Assert.Equal(2.5, WeightedPathEvaluator.MinimumPathScore(map, roles, By(Challenge)));

        var builder = new RunMapBuilder();
        builder.AddNode(new NodeId("only"), Mark, "only").Entry("only");
        Assert.Equal(2.5, WeightedPathEvaluator.MinimumPathScore(builder.Build(), roles, By(Challenge)));
    }

    // A map nobody can walk scores nothing rather than infinity — a caller comparing ±∞ to a threshold would
    // silently pass or fail everything.
    [Fact]
    public void A_map_with_no_way_in_scores_nothing()
    {
        var empty = new RunMapBuilder().Build();
        Assert.Equal(0, WeightedPathEvaluator.MinimumPathScore(empty, new Dictionary<NodeId, MapNodeKind>(), By(Challenge)));
        Assert.Equal(0, WeightedPathEvaluator.MaximumPathScore(empty, new Dictionary<NodeId, MapNodeKind>(), By(Challenge)));
    }

    // The role map is the authority on what a room IS; a room it never placed cannot be scored.
    [Fact]
    public void A_room_with_no_role_recorded_is_worth_nothing()
    {
        var (map, roles) = Graph(
            new Dictionary<string, MapNodeKind>
            {
                ["start"] = MapNodeKind.Elite,
                ["middle"] = MapNodeKind.Elite,
                ["boss"] = MapNodeKind.Boss,
            },
            "start>middle", "middle>boss");
        roles.Remove(new NodeId("middle"));

        Assert.Equal(2.5, WeightedPathEvaluator.MinimumPathScore(map, roles, By(Challenge)));
    }

    // The weight is free to look at WHICH room it is, not only at its kind — the same freedom the per-path
    // checker needs for a treasure that may still flip into a mimic.
    [Fact]
    public void The_weight_may_depend_on_the_room_and_not_only_on_its_kind()
    {
        var (map, roles) = Graph(
            new Dictionary<string, MapNodeKind>
            {
                ["start"] = MapNodeKind.Treasure,
                ["other"] = MapNodeKind.Treasure,
                ["boss"] = MapNodeKind.Boss,
            },
            "start>other", "other>boss");

        // start 1 + other 10 + boss 1 — the whole route, not only the interesting room on it.
        var score = WeightedPathEvaluator.MaximumPathScore(
            map, roles, (id, _) => id.Value == "other" ? 10 : 1);
        Assert.Equal(12, score);
    }

    // MapConstraintValidator now runs on this DP. Its answers have to be the same integers as before, on a real
    // generated map rather than on a hand-drawn one.
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(4242)]
    public void Counting_a_role_is_the_same_question_with_a_weight_of_one(int seed)
    {
        var spec = new MapGenerationSpec
        {
            Rows = 9,
            PerPathMinimums = new Dictionary<MapNodeKind, int>
            {
                [MapNodeKind.Elite] = 2,
                [MapNodeKind.Shop] = 1,
                [MapNodeKind.Rest] = 1,
            },
            KindWeights = new Dictionary<MapNodeKind, int>
            {
                [MapNodeKind.Combat] = 6,
                [MapNodeKind.Event] = 3,
                [MapNodeKind.Elite] = 2,
                [MapNodeKind.Rest] = 1,
                [MapNodeKind.Shop] = 1,
                [MapNodeKind.Treasure] = 1,
            },
        };
        var generated = RuleBasedMapGenerator.Generate(
            spec, seed, 0, new BalanceCalculator(new BalanceManifest(), Array.Empty<EncounterDefinition>()),
            (kind, coord, encounter, nodeRef) => new NodeContent(Mark, encounter?.ToString() ?? nodeRef ?? "none"));

        foreach (var kind in generated.Roles.Values.Distinct())
        {
            var role = kind;
            Assert.Equal(
                MapConstraintValidator.WorstPathCount(generated.Map, generated.Roles, k => k == role),
                (int)WeightedPathEvaluator.MinimumPathScore(
                    generated.Map, generated.Roles, (_, k) => k == role ? 1d : 0d));
            Assert.Equal(
                MapConstraintValidator.RichestPathCount(generated.Map, generated.Roles, k => k == role),
                (int)WeightedPathEvaluator.MaximumPathScore(
                    generated.Map, generated.Roles, (_, k) => k == role ? 1d : 0d));
        }
    }

    // ————— the graph-agnostic pass (map rework S8) —————
    //
    // PathPressure asks this question of a StrategicTopology, which is rows, slots and edges and no RunMap at
    // all, so the DP takes a successor function. These two tests are what keeps the RunMap overloads honest as
    // wrappers rather than as a second copy of the traversal.

    [Fact]
    public void The_same_pass_answers_both_ends_and_names_the_routes()
    {
        var (map, roles) = Graph(
            new Dictionary<string, MapNodeKind>
            {
                ["start"] = MapNodeKind.Combat,
                ["elite"] = MapNodeKind.Elite,
                ["rest"] = MapNodeKind.Rest,
                ["boss"] = MapNodeKind.Boss,
            },
            "start>elite", "start>rest", "elite>boss", "rest>boss");

        var scores = WeightedPathEvaluator.Score(
            map.Nodes.Select(node => node.Id).ToList(),
            map.SuccessorIds,
            map.RootIds(),
            id => Challenge.GetValueOrDefault(roles[id]));

        Assert.Equal(1.0, scores.Minimum);
        Assert.Equal(3.5, scores.Maximum);
        Assert.Equal(new[] { "start", "rest", "boss" }, scores.ThinnestRoute.Select(id => id.Value));
        Assert.Equal(new[] { "start", "elite", "boss" }, scores.RichestRoute.Select(id => id.Value));
    }

    // A graph nobody can enter has no route, and a route that does not exist is worth nothing rather than
    // infinity — the same edge the RunMap overloads take, said once for the pass they both run on.
    [Fact]
    public void A_graph_with_no_entries_scores_nothing_and_reports_no_route()
    {
        var (map, roles) = Graph(
            new Dictionary<string, MapNodeKind> { ["only"] = MapNodeKind.Elite });

        var scores = WeightedPathEvaluator.Score(
            map.Nodes.Select(node => node.Id).ToList(), map.SuccessorIds, [],
            id => Challenge.GetValueOrDefault(roles[id]));

        Assert.Equal(0, scores.Minimum);
        Assert.Equal(0, scores.Maximum);
        Assert.Empty(scores.ThinnestRoute);
        Assert.Empty(scores.RichestRoute);
    }
}
