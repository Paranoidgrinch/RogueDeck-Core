using RogueDeck.Bot;
using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Sandbox.Tests;

// ⚠⚠ THE MAP IS FINISHED BEFORE THE RUN TAKES A STEP. Which encounter every combat node holds, which shop,
// which event — all of it is decided at generation, which means the set of games a seed can hand out is not
// something to be sampled by playing. It can be read.
//
// The oracle reads it, and what it is allowed to claim is narrow on purpose: not that one path is EASIER —
// no weight it has knows about the deck that meets a fight — but how far apart the paths are on one scale
// (spread), and where the runner's own doors fell in that field (rank). These tests hold it to exactly that,
// and to the two places it would quietly start lying: comparing a walk that stopped short against walks that
// did not, and printing zeros when nobody authored the scale it is reading.
public class MapOracleTests
{
    private static readonly NodeType Combat = new("run.combat");

    private static Node Fight(string id, string encounter, string tag = MapNodeTags.Combat) =>
        new(new NodeId(id), Combat, new EncounterRef(new EncounterId(encounter)), [tag]);

    private static Node Quiet(string id, string tag) =>
        new(new NodeId(id), new NodeType("run.event"), new EventRef(new EventId("nothing")), [tag]);

    // An encounter is worth what its enemies carry, so a fight of N health weighs -N on the fallback scale.
    private static EncounterDefinition Enemy(string id, params int[] healths) =>
        new(new EncounterId(id),
            [.. healths.Select((hp, index) => new EncounterEnemy($"{id}-{index}", hp, []))]);

    private static MapOracle.Weights Scale(params EncounterDefinition[] encounters) =>
        MapOracle.Weights.For(Blueprint(encounters));

    private static RunBlueprint Blueprint(IReadOnlyList<EncounterDefinition> encounters) =>
        new([], new Dictionary<string, EventScript>(), encounters, [], [], new RunMap([]));

    // A diamond: one door leads past a fight, the other past a rest.
    private static RunMap Diamond(params Node[] middle)
    {
        var nodes = new List<Node> { Quiet("entry", MapNodeTags.Event) };
        nodes.AddRange(middle);
        nodes.Add(Fight("boss", "boss-fight", MapNodeTags.Boss));
        return new RunMap(nodes)
        {
            EntryNodeIds = [new NodeId("entry")],
            Edges =
            [
                .. middle.Select(m => new MapEdge(new NodeId("entry"), m.Id)),
                .. middle.Select(m => new MapEdge(m.Id, new NodeId("boss"))),
            ],
        };
    }

    // ── What the doors are worth ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_spread_is_what_the_doors_on_this_map_are_worth()
    {
        var map = Diamond(Fight("hard", "ogre"), Quiet("easy", MapNodeTags.Rest));
        var survey = MapOracle.Survey(1, "act", map, Scale(Enemy("ogre", 60), Enemy("boss-fight", 100)));

        Assert.Equal(2, survey.Paths);
        // Both paths carry the boss; only one carries the ogre, and the ogre is the whole difference.
        Assert.Equal(-100, survey.Lightest.Weight);
        Assert.Equal(-160, survey.Heaviest.Weight);
        Assert.Equal(60, survey.Spread);
    }

    // ⚠ A MAP WHOSE PATHS ALL WEIGH THE SAME SAYS SO, and that is a finding rather than a failure: there is
    // nothing to navigate here, and a runner losing on this map is not losing at the doors.
    [Fact]
    public void A_map_that_hands_out_one_game_has_no_spread()
    {
        var map = Diamond(Fight("left", "ogre"), Fight("right", "ogre"));
        var survey = MapOracle.Survey(1, "act", map, Scale(Enemy("ogre", 60), Enemy("boss-fight", 100)));

        Assert.Equal(2, survey.Paths);
        Assert.Equal(0, survey.Spread);
    }

    // ── Where the runner's doors fell ────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_walk_is_ranked_among_the_paths_it_could_have_taken()
    {
        var map = Diamond(Fight("hard", "ogre"), Quiet("easy", MapNodeTags.Rest));
        var scale = Scale(Enemy("ogre", 60), Enemy("boss-fight", 100));

        var light = MapOracle.Survey(1, "act", map, scale, ["entry", "easy", "boss"]);
        var heavy = MapOracle.Survey(1, "act", map, scale, ["entry", "hard", "boss"]);

        Assert.False(light.Partial);
        Assert.Equal(1, light.Rank);
        Assert.Equal(2, light.Field);
        Assert.Equal(2, heavy.Rank);
    }

    // ⚠⚠ THE ONE PLACE THIS WOULD QUIETLY LIE. A run that died mid-act walked a PREFIX, and ranking a prefix
    // against whole paths compares fourteen rooms with twenty-three. Here the two whole paths weigh exactly
    // the same — so a whole-path ranking would call the walk joint-first — while at the second room the
    // walk is on the heavier of the two. The rank has to say so.
    [Fact]
    public void A_walk_that_stopped_short_is_ranked_against_prefixes_of_its_own_length()
    {
        var nodes = new List<Node>
        {
            Quiet("entry", MapNodeTags.Event),
            Fight("a", "ogre"), Quiet("x", MapNodeTags.Rest),
            Quiet("b", MapNodeTags.Rest), Fight("y", "ogre"),
            Fight("boss", "boss-fight", MapNodeTags.Boss),
        };
        var map = new RunMap(nodes)
        {
            EntryNodeIds = [new NodeId("entry")],
            Edges =
            [
                new MapEdge(new NodeId("entry"), new NodeId("a")),
                new MapEdge(new NodeId("entry"), new NodeId("b")),
                new MapEdge(new NodeId("a"), new NodeId("x")),
                new MapEdge(new NodeId("b"), new NodeId("y")),
                new MapEdge(new NodeId("x"), new NodeId("boss")),
                new MapEdge(new NodeId("y"), new NodeId("boss")),
            ],
        };
        var scale = Scale(Enemy("ogre", 60), Enemy("boss-fight", 100));

        // Both whole routes hold one ogre and the boss: as whole paths they are indistinguishable.
        var whole = MapOracle.Survey(1, "act", map, scale);
        Assert.Equal(0, whole.Spread);

        var died = MapOracle.Survey(1, "act", map, scale, ["entry", "a"]);

        Assert.True(died.Partial);
        Assert.Equal(2, died.Field);      // two prefixes of length two, not two whole paths
        Assert.Equal(2, died.Rank);       // and the walk is on the heavier of them
    }

    // ⚠ EVERY "BEST" ON THE LINE IS OVER THE FIELD THE RANK IS OVER. Here the only rest on the map sits in
    // the THIRD room, past where the walk died. Measured against whole paths the walk looks like it threw a
    // rest away; measured against what it could actually have reached in two rooms, there was none to take.
    [Fact]
    public void The_best_a_partial_walk_is_measured_against_is_a_partial_one()
    {
        var nodes = new List<Node>
        {
            Quiet("entry", MapNodeTags.Event),
            Fight("a", "ogre"), Quiet("rest", MapNodeTags.Rest),
            Fight("b", "ogre"), Fight("c", "ogre"),
            Fight("boss", "boss-fight", MapNodeTags.Boss),
        };
        var map = new RunMap(nodes)
        {
            EntryNodeIds = [new NodeId("entry")],
            Edges =
            [
                new MapEdge(new NodeId("entry"), new NodeId("a")),
                new MapEdge(new NodeId("entry"), new NodeId("b")),
                new MapEdge(new NodeId("a"), new NodeId("rest")),
                new MapEdge(new NodeId("b"), new NodeId("c")),
                new MapEdge(new NodeId("rest"), new NodeId("boss")),
                new MapEdge(new NodeId("c"), new NodeId("boss")),
            ],
        };
        var scale = Scale(Enemy("ogre", 60), Enemy("boss-fight", 100));

        Assert.Equal(1, MapOracle.Survey(1, "act", map, scale).MostRests);

        var died = MapOracle.Survey(1, "act", map, scale, ["entry", "b"]);

        Assert.True(died.Partial);
        Assert.Equal(0, died.MostRests);  // no two-room route on this map reaches a rest
    }

    // ── The scale names itself ───────────────────────────────────────────────────────────────────────────

    // ⚠⚠ THE INSTRUMENT MUST NOT READ ITSELF. B&B ships an empty BalanceManifest, so an oracle that trusted
    // it would report "every path on every map ever generated is worth the same" — a statement about the
    // manifest wearing the clothes of a statement about the game.
    [Fact]
    public void An_unauthored_manifest_is_not_a_world_without_enemies()
    {
        var weights = Scale(Enemy("ogre", 40, 20));

        Assert.Equal("enemy-hp", weights.Name);
        Assert.Equal(-60, weights.Of(Fight("room", "ogre")));
    }

    [Fact]
    public void An_authored_manifest_is_used_and_said_so()
    {
        var blueprint = Blueprint([Enemy("ogre", 40, 20)]) with
        {
            Balance = new BalanceManifest { Encounters = new Dictionary<string, int> { ["ogre"] = -75 } },
        };
        var weights = MapOracle.Weights.For(blueprint);

        Assert.Equal("authored", weights.Name);
        Assert.Equal(-75, weights.Of(Fight("room", "ogre")));
    }

    // ⚠ A MIMIC IS COUNTED AS THE AMBUSH IT IS. `MapRole` calls it "treasure" so a player's log does not
    // spoil it; the oracle is not a player, and a survey that scored it as a quiet room with gold in it
    // would be wrong about exactly the node the map put there to be wrong about.
    [Fact]
    public void A_mimic_weighs_what_is_waiting_inside_it()
    {
        var mimic = Fight("box", "ogre", MapNodeTags.Mimic);

        Assert.Equal(MapNodeTags.Treasure, MapRole.Of(mimic));
        Assert.Equal(MapNodeTags.Mimic, MapOracle.Role(mimic));
        Assert.Equal(-60, Scale(Enemy("ogre", 60)).Of(mimic));
    }

    // ── THE ROUTES THEMSELVES (O3) ───────────────────────────────────────────────────────────────────────
    // The survey reduces the field to numbers; `Routes` hands the field over as rooms to walk, so each one
    // can be played. That is what turns "how far does this player get?" — which mixes navigation with
    // difficulty — into "is there a way through this act for this player at all?".

    [Fact]
    public void Every_route_through_the_map_is_handed_over()
    {
        var map = Diamond(Fight("hard", "ogre"), Quiet("easy", MapNodeTags.Rest));
        var routes = MapOracle.Routes(map);

        Assert.Equal(2, routes.Count);
        Assert.Contains(routes, r => r.SequenceEqual(new[] { "entry", "hard", "boss" }));
        Assert.Contains(routes, r => r.SequenceEqual(new[] { "entry", "easy", "boss" }));
    }

    // ⚠ THE TWO READINGS OF ONE MAP MUST AGREE. The survey counts paths to report a spread; Routes hands
    // them over to be walked. A map where the two disagreed would mean a route was measured that nobody can
    // walk, or walked that nobody measured.
    [Fact]
    public void The_routes_handed_over_are_the_paths_that_were_counted()
    {
        var blueprint = Generated();
        var run = blueprint.CreateInitialRun(new RunId("r"), randomSeed: 11);
        var weights = MapOracle.Weights.For(blueprint);

        for (var act = 1; act <= run.Acts.Count; act++)
        {
            var map = run.Acts[act - 1].Map;
            Assert.Equal(
                MapOracle.Survey(act, "act", map, weights).Paths,
                MapOracle.Routes(map).Count);
        }
    }

    [Fact]
    public void A_route_is_asked_for_by_act_and_comes_back_walkable()
    {
        var blueprint = Generated();
        var run = blueprint.CreateInitialRun(new RunId("r"), randomSeed: 11);
        var rooms = run.Acts[1].Map.Nodes.Select(n => n.Id.Value).ToHashSet(StringComparer.Ordinal);

        var routes = MapOracle.RoutesOfAct(blueprint, seed: 11, act: 2);

        Assert.NotEmpty(routes);
        foreach (var route in routes)
        {
            Assert.All(route, room => Assert.Contains(room, rooms));
            // A route visits each room once: a repeat would be a cycle, and these maps are DAGs.
            Assert.Equal(route.Count, route.Distinct(StringComparer.Ordinal).Count());
        }
    }

    // An act the run does not have is answered with nothing rather than with an exception or act one.
    [Fact]
    public void An_act_that_does_not_exist_has_no_routes()
    {
        Assert.Empty(MapOracle.RoutesOfAct(Generated(), seed: 11, act: 9));
        Assert.Empty(MapOracle.RoutesOfAct(Generated(), seed: 11, act: 0));
    }

    // ── WHERE A LOSING WALK WAS STILL SAVABLE (O4) ───────────────────────────────────────────────────────
    // A failing route and a clearing route run together for a while and then part. Where they part is the
    // last moment the run was savable; the door taken after it is the one that cost the act.

    private static MapOracle.RouteVerdict Route(bool cleared, params string[] rooms) => new(rooms, cleared);

    [Fact]
    public void The_last_exit_is_the_room_where_the_winner_and_the_loser_parted()
    {
        var reading = MapOracle.WhereItWasSealed([
            Route(true,  "a", "b", "win", "boss"),
            Route(false, "a", "b", "lose", "boss"),
        ]);

        Assert.Equal(1, reading.Failing);
        Assert.Equal(1, reading.Savable);
        Assert.Equal("b", reading.LastExit);   // they were together through b and parted after it
        Assert.Equal(2, reading.LastExitDepth);
    }

    // ⚠ THE DEEPEST EXIT ACROSS THE LOSING ROUTES, because that is the latest anything could still have been
    // done. A route that went wrong at the first door does not make the map hopeless from the first door.
    [Fact]
    public void The_reading_reports_the_latest_chance_anyone_had()
    {
        var reading = MapOracle.WhereItWasSealed([
            Route(true,  "a", "b", "c", "win"),
            Route(false, "a", "off", "x", "y"),      // parted after a
            Route(false, "a", "b", "c", "lose"),     // parted after c — the latest chance
        ]);

        Assert.Equal(2, reading.Failing);
        Assert.Equal(2, reading.Savable);
        Assert.Equal("c", reading.LastExit);
        Assert.Equal(3, reading.LastExitDepth);
    }

    // ⚠⚠ "NO EXIT" IS THE FINDING, NOT A GAP. Nothing cleared, so no losing route shares a room with a
    // winner — because there is no winner. No door on this map led anywhere this player could finish.
    [Fact]
    public void A_map_where_nothing_cleared_offers_no_exit_at_all()
    {
        var reading = MapOracle.WhereItWasSealed([
            Route(false, "a", "b", "x"),
            Route(false, "a", "c", "y"),
        ]);

        Assert.Equal(2, reading.Failing);
        Assert.Equal(0, reading.Savable);
        Assert.Null(reading.LastExit);
    }

    // ⚠ A DOOR-LEVEL READING AND ONLY THAT. When every route clears, nothing failed and there is nothing to
    // explain — the fault, if a particular walk still died, belongs to a fight and to the fight autopsy.
    [Fact]
    public void A_map_where_everything_cleared_has_nothing_to_explain()
    {
        var reading = MapOracle.WhereItWasSealed([Route(true, "a", "b"), Route(true, "a", "c")]);

        Assert.Equal(0, reading.Failing);
        Assert.Equal(0, reading.Savable);
        Assert.Null(reading.LastExit);
    }

    // ── The surveyed map is the map the run walks ────────────────────────────────────────────────────────

    // ⚠⚠ THE WHOLE INSTRUMENT RESTS ON THIS. The oracle does not watch a run; it rebuilds the run's maps from
    // the seed. `BuildActPlan` is a pure function of (seed, starting loadout, generator) — the same property
    // that lets a saved run resume onto an identical map — but the LOADOUT is computed separately on each
    // side, and a survey of a differently-balanced map would be a careful measurement of a game nobody
    // played. So the two are held against each other, node for node.
    [Fact]
    public void The_maps_surveyed_are_the_maps_the_run_is_given()
    {
        var blueprint = Generated();
        var run = blueprint.CreateInitialRun(new RunId("r"), randomSeed: 7);
        var surveys = MapOracle.SurveyRun(blueprint, seed: 7);

        Assert.Equal(run.Acts.Count, surveys.Count);
        for (var index = 0; index < run.Acts.Count; index++)
            Assert.Equal(run.Acts[index].Map.Nodes.Count, surveys[index].Nodes);

        // …and a walk taken off the run's own map is found on the surveyed one, whole.
        var walked = run.Acts[0].Map.EntryNodeIds.Take(1).Select(id => $"1:{id.Value}").ToList();
        var found = MapOracle.SurveyRun(blueprint, seed: 7, walked: walked);
        Assert.NotNull(found[0].Taken);
        Assert.Single(found[0].Taken!.Nodes);
    }

    // ⚠ A WALK IS FILED UNDER THE ACT IT HAPPENED IN. The rooms of act two mean nothing on act one's map,
    // and an oracle that let them through would rank a walk against a graph it never stood on.
    [Fact]
    public void A_walk_is_read_against_the_act_it_happened_in()
    {
        var blueprint = Generated();
        var run = blueprint.CreateInitialRun(new RunId("r"), randomSeed: 7);
        var second = run.Acts[1].Map.EntryNodeIds[0].Value;

        // Act two's first room, mislabelled as act one's: it is not on act one's map and must not be read.
        var surveys = MapOracle.SurveyRun(blueprint, seed: 7, walked: [$"2:{second}"]);

        Assert.Null(surveys[0].Taken);
        Assert.NotNull(surveys[1].Taken);
    }

    // A two-act game whose maps are GENERATED — the only shape in which the claim above means anything.
    private static RunBlueprint Generated()
    {
        var spec = new MapGenerationSpec
        {
            Rows = 6,
            MinWidth = 2,
            MaxWidth = 3,
            KindWeights = new Dictionary<MapNodeKind, int> { [MapNodeKind.Combat] = 1 },
            Encounters = new EncounterDistribution
            {
                ByRole = new Dictionary<MapNodeKind, IReadOnlyList<EncounterPoolEntry>>
                {
                    [MapNodeKind.Combat] = [new EncounterPoolEntry(new EncounterId("ogre"))],
                    [MapNodeKind.Boss] = [new EncounterPoolEntry(new EncounterId("boss-fight"))],
                },
            },
        };

        return Blueprint([Enemy("ogre", 60), Enemy("boss-fight", 100)]) with
        {
            MapGeneration = spec,
            Acts = [new RunAct("one") { MapGeneration = spec }, new RunAct("two") { MapGeneration = spec }],
        };
    }
}
