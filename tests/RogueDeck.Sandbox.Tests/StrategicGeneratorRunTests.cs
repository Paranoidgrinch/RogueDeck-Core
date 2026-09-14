using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Sandbox.Tests;

// BOTH GENERATORS SHIP, AND THE RUN REMEMBERS WHICH ONE IT IS (map rework S11).
//
// A BnB map is never saved — it is regenerated on resume from the seed and the starting loadout. So the choice of
// generator is not a menu setting: it is part of the run's identity, and a choice that lived only in the menu
// would hand a resumed run a different map. The player closes the game standing in front of an elite and comes
// back to a shop. Every test here is about that one sentence.
public class StrategicGeneratorRunTests
{
    private const string Fight = "goblin-fight";

    // A game that authors BOTH generators: the shape rules of the strategic one, and the content rules (pools,
    // balance, node refs) that either generator realizes its rooms from.
    private static RunBlueprint BothGenerators(bool strategicRules = true)
    {
        var smite = new CardBlueprint("smite")
        {
            Program = new EffectProgram<CardPlayContext>(new DealDamageNode<CardPlayContext>(
                new EventTargetCombatantTargetSelector(), new ConstantExpression<CardPlayContext>(6))),
        };
        var slam = new EnemyActionBlueprint("slam", new ActionIntent("Slam", IntentKind.Attack))
        {
            Program = new EffectProgram<EnemyActionContext>(new DealDamageNode<EnemyActionContext>(
                new EventTargetCombatantTargetSelector(), new ConstantExpression<EnemyActionContext>(2))),
        };
        var encounter = new EncounterDefinition(
            new EncounterId(Fight),
            new[] { new EncounterEnemy("goblin", 1, new[] { new EnemyActionDefinitionId("slam") }) },
            heroResources: new[] { new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3) });
        var candidates = new[] { new EncounterPoolEntry(new EncounterId(Fight)) };

        return new RunBlueprint(
            Enumerable.Repeat(new CardDefinitionId("smite"), 5).ToList(),
            new Dictionary<string, EventScript>(),
            new[] { encounter },
            new[] { CardData.From(smite) },
            new[] { EnemyActionData.From(slam) },
            new RunMap(Array.Empty<Node>()))
        {
            MapGeneration = new MapGenerationSpec
            {
                Rows = 6,
                MinWidth = 1,
                MaxWidth = 2,
                KindWeights = new Dictionary<MapNodeKind, int> { [MapNodeKind.Combat] = 1 },
                Encounters = new EncounterDistribution
                {
                    ByRole = new Dictionary<MapNodeKind, IReadOnlyList<EncounterPoolEntry>>
                    {
                        [MapNodeKind.Combat] = candidates,
                        [MapNodeKind.Boss] = candidates,
                    },
                },
            },
            StrategicMapGeneration = strategicRules
                ? new StrategicActSpec
                {
                    Rows = 8,
                    LaneProfiles =
                    [
                        new("plain", new Dictionary<MapNodeKind, int> { [MapNodeKind.Combat] = 1 }),
                    ],
                    Rooms = new StrategicRoomSpec
                    {
                        KindWeights = new Dictionary<MapNodeKind, int> { [MapNodeKind.Combat] = 1 },
                    },
                }
                : null,
        };
    }

    // The whole point of the seam: the same seed, two generators, two different acts — and each of them a real,
    // walkable map rather than a spec that happens not to throw.
    [Fact]
    public void The_same_seed_draws_a_different_act_on_each_generator()
    {
        var blueprint = BothGenerators();

        var legacy = blueprint.BuildRunMap(seed: 7, startingLoadout: 0);
        var strategic = blueprint.BuildRunMap(seed: 7, startingLoadout: 0, MapGenerators.Strategic);

        Assert.NotEqual(Signature(legacy), Signature(strategic));
        Assert.Empty(RunMapValidator.Validate(legacy));
        Assert.Empty(RunMapValidator.Validate(strategic));

        // The strategic act is as long as its own rules say, not as long as the other generator's.
        Assert.Equal(8, strategic.Depths().Values.Max() + 1);
    }

    // A GAME THAT NEVER AUTHORED THE SECOND GENERATOR keeps working when something asks for it. An act cannot be
    // built to rules nobody wrote, and a run is a bad place to find that out.
    [Fact]
    public void A_game_without_strategic_rules_falls_back_rather_than_failing()
    {
        var blueprint = BothGenerators(strategicRules: false);

        Assert.Equal(
            Signature(blueprint.BuildRunMap(7, 0)),
            Signature(blueprint.BuildRunMap(7, 0, MapGenerators.Strategic)));
    }

    // THE SAVE CARRIES THE CHOICE, and a resumed run rebuilds ITS OWN map — content and all, not just its shape.
    [Fact]
    public void A_run_started_on_the_strategic_generator_resumes_on_it()
    {
        var blueprint = BothGenerators();
        var run = blueprint.CreateInitialRun(
            new RunId("run"), randomSeed: 7, characterId: null, MapGenerators.Strategic);

        var save = run.Snapshot();
        Assert.Equal(MapGenerators.Strategic, save.MapGenerator);

        var rebuilt = blueprint.BuildRunMap(save.RandomSeed, save.MapGenerationLoadout ?? 0, save.MapGenerator);
        Assert.Equal(Content(run.Map), Content(rebuilt));

        // And the run that comes back out of the save file knows what it is, so the NEXT save says so too.
        var restored = RunState.Restore(save, rebuilt, null);
        Assert.Equal(MapGenerators.Strategic, restored.Snapshot().MapGenerator);
    }

    // A SAVE WRITTEN BEFORE ANY OF THIS has no generator recorded, and no generator recorded means the one every
    // run has always used. No migration, no guessing: the property defaults.
    [Fact]
    public void A_save_written_before_the_choice_existed_resumes_on_the_rule_based_generator()
    {
        var blueprint = BothGenerators();
        var run = blueprint.CreateInitialRun(new RunId("run"), randomSeed: 7);

        var save = run.Snapshot();
        Assert.Null(save.MapGenerator);

        Assert.Equal(
            Content(blueprint.BuildRunMap(save.RandomSeed, save.MapGenerationLoadout ?? 0)),
            Content(blueprint.BuildRunMap(save.RandomSeed, save.MapGenerationLoadout ?? 0, save.MapGenerator)));
    }

    // The acts of a multi-act run are laid out up front and each draws from its own seed, on either generator.
    [Fact]
    public void Every_act_of_a_run_is_drawn_by_the_generator_the_run_chose()
    {
        var blueprint = BothGenerators();
        var acts = new List<RunAct>
        {
            new("one"),
            new("two"),
        };
        var multi = blueprint with { Acts = acts };

        var plan = multi.BuildActPlan(seed: 3, startingLoadout: 0, MapGenerators.Strategic);

        Assert.Equal(2, plan.Count);
        Assert.NotEqual(Signature(plan[0].Map), Signature(plan[1].Map));
        foreach (var act in plan)
        {
            Assert.Equal(8, act.Map.Depths().Values.Max() + 1);
            Assert.Empty(RunMapValidator.Validate(act.Map));
        }
    }

    // A run really plays on the new generator, through the real content path — the arc's standing rule that a
    // generated map is only proved by walking it.
    [Fact]
    public void A_strategic_act_walks_to_victory_through_the_real_content_path()
    {
        var blueprint = BothGenerators();
        var content = RunPlayback.BuildContent(blueprint);
        var defs = new RunDefinitionRegistryBuilder();
        new StandardRunPackage(new AutoPlayCombatDriver(), content).RegisterDefinitions(defs);

        var run = blueprint.CreateInitialRun(
            new RunId("run"), randomSeed: 7, characterId: null, MapGenerators.Strategic);
        var boss = Assert.Single(run.Map.Nodes, node => run.Map.SuccessorIds(node.Id).Count == 0);

        new RunRunner(defs.Build(), new ScriptedChoiceProvider(), content: content).Run(run);

        Assert.Equal(RunResult.Victory, run.Result);
        Assert.Equal(boss.Id, run.CurrentNodeId);
    }

    // WHERE THE ROOMS ARE DRAWN is part of what the strategic generator promises (§2.4): its edges do not cross
    // when each row is laid out in column order, and a promise the map does not record is one the frontends
    // cannot keep.
    [Fact]
    public void A_strategic_map_says_where_its_rooms_are_drawn()
    {
        var map = BothGenerators().BuildRunMap(7, 0, MapGenerators.Strategic);

        Assert.Equal(map.Nodes.Count, map.Layout.Count);
        foreach (var node in map.Nodes)
            Assert.Single(map.Layout, entry => entry.Node == node.Id);

        // The rule-based generator records none, and that is not a defect: it never promised an order.
        Assert.Empty(BothGenerators().BuildRunMap(7, 0).Layout);
    }

    // …AND A COORDINATE IS ONLY WORTH WHAT IT DECODES TO. Counting the entries is not the test: the generator
    // wrote one per room and every one of them was correct as a pair of numbers, while every room in the act
    // still landed on the same drawing cell — the act reached the player as a heap, and nothing failed.
    //
    // RunMap.Layout is a SCREEN coordinate on the drawing grid (MapLayout): depth along X, lane along Y. So what
    // has to be asserted is what a frontend gets back out of it.
    [Fact]
    public void Every_room_of_a_strategic_map_is_drawn_in_its_own_place()
    {
        var map = BothGenerators().BuildRunMap(7, 0, MapGenerators.Strategic);
        var drawn = MapGraphLayout.Resolve(map);

        var cells = map.Nodes
            .ToDictionary(node => node.Id, node => (
                Depth: MapLayout.DepthOf(drawn[node.Id].X),
                Lane: MapLayout.LaneOf(drawn[node.Id].Y)));

        // No two rooms share a cell. This is the whole bug in one line.
        Assert.Equal(map.Nodes.Count, cells.Values.Distinct().Count());

        // The depth a frontend reads is the row the generator meant, and the lane is the column: the ids the
        // generator hands out say both, so the coordinate can be checked against the room's own name.
        foreach (var node in map.Nodes)
        {
            var parts = node.Id.Value.TrimStart('r').Split('c');
            Assert.Equal(int.Parse(parts[0]), cells[node.Id].Depth);
            Assert.Equal(int.Parse(parts[1]), cells[node.Id].Lane);
        }

        // …and the act runs the way an act runs: every depth from the first row to the last is drawn, exactly
        // once each, so no row of the map is laid on top of another.
        var depths = cells.Values.Select(cell => cell.Depth).Distinct().OrderBy(depth => depth).ToList();
        Assert.Equal(Enumerable.Range(0, depths.Count), depths);

        // THE NO-CROSSINGS PROMISE, read off the drawing rather than off the topology: within one step of depth
        // the edges keep their order, so a lower lane never leads to a higher one than its neighbour does.
        foreach (var group in map.Edges.GroupBy(edge => cells[edge.From].Depth))
        {
            var ordered = group.OrderBy(edge => cells[edge.From].Lane).ThenBy(edge => cells[edge.To].Lane).ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                var (before, after) = (ordered[i - 1], ordered[i]);
                Assert.False(cells[before.From].Lane < cells[after.From].Lane
                    && cells[before.To].Lane > cells[after.To].Lane,
                    $"{before.From.Value}→{before.To.Value} crosses {after.From.Value}→{after.To.Value}");
            }
        }
    }

    // AN ACT'S TWO SPECS ARE ONE DESCRIPTION AND THEY FALL BACK TOGETHER (found by BnB's Act V, which is three
    // boss rooms and no treasure room at all). An act that brings its own content rules but no strategic ones
    // used to borrow the BLUEPRINT's strategic rules — another act's length, another act's budgets — and then
    // asked its own content spec for rooms that act does not have. Such an act is drawn by the rule-based
    // generator, whichever generator the run was started on, because that is the only description it has.
    [Fact]
    public void An_act_that_authors_no_strategic_rules_does_not_borrow_the_documents()
    {
        var blueprint = BothGenerators();
        var gauntlet = new MapGenerationSpec
        {
            Rows = 0,
            BossRooms = 2,
            MinWidth = 1,
            MaxWidth = 1,
            Encounters = blueprint.MapGeneration!.Encounters,
        };
        var withActs = blueprint with
        {
            Acts =
            [
                new RunAct("one", blueprint.MapGeneration!) { StrategicMapGeneration = blueprint.StrategicMapGeneration },
                new RunAct("two", gauntlet),
            ],
        };

        var plan = withActs.BuildActPlan(7, 0, MapGenerators.Strategic);

        // The first act is the strategic one it authored: eight rows, and a layout to draw them by.
        Assert.Equal(8, plan[0].Map.Nodes.Select(node => node.Id.Value.Split('c')[0]).Distinct().Count());
        Assert.NotEmpty(plan[0].Map.Layout);
        // The second is its own two boss rooms, not the first act's eight rows — and it records no layout,
        // which is how you can tell which generator drew it.
        Assert.Equal(2, plan[1].Map.Nodes.Count);
        Assert.Empty(plan[1].Map.Layout);
    }

    private static string Signature(RunMap map) =>
        string.Join("|", map.Nodes.Select(node => node.Id.Value)) + "##"
        + string.Join("|", map.Edges.Select(edge => $"{edge.From.Value}->{edge.To.Value}")) + "##"
        + string.Join("|", map.EntryNodeIds.Select(id => id.Value));

    // The same map INCLUDING what stands in every room — the claim a resume has to make.
    private static string Content(RunMap map) => Signature(map) + "##" + string.Join("|", map.Nodes.Select(
        node => $"{node.Id.Value}:{node.Type.Value}:{node.Payload}:{string.Join(",", node.Tags)}"));
}
