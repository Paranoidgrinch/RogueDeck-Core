using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Sandbox.Tests;

// Phase 4 — a run generates its map per run at start (RunSetup) and rebuilds the identical map on resume. Driven
// through RunPlayback.BuildContent (the real content path, per the arc's lesson: never a hand-built registry), so
// the encounters the generator places actually resolve in combat.
public class MapGenerationRunTests
{
    private const string Fight = "goblin-fight";

    // A blueprint whose map is generated (all-combat rows so a headless run auto-resolves to the boss), with one
    // trivially winnable encounter the generator draws for every combat/elite/boss node.
    private static RunBlueprint GeneratedRun()
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
            new RunMap(Array.Empty<Node>())) // authored map is empty — generation must replace it
        {
            MapGeneration = new MapGenerationSpec
            {
                Rows = 4,
                MinWidth = 1,
                MaxWidth = 2,
                MinEnemiesPerPath = 2,
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
        };
    }

    [Fact]
    public void A_generated_map_walks_to_victory_through_BuildContent()
    {
        var blueprint = GeneratedRun();
        var content = RunPlayback.BuildContent(blueprint);
        var defs = new RunDefinitionRegistryBuilder();
        new StandardRunPackage(new AutoPlayCombatDriver(), content).RegisterDefinitions(defs);

        var run = blueprint.CreateInitialRun(new RunId("run"), randomSeed: 7);

        // The authored map was empty; generation replaced it with a real act ending in a single boss leaf.
        Assert.NotEmpty(run.Map.Nodes);
        Assert.NotNull(run.GeneratedMapLoadout);
        var boss = Assert.Single(run.Map.Nodes, n => run.Map.SuccessorIds(n.Id).Count == 0);

        new RunRunner(defs.Build(), new ScriptedChoiceProvider(), content: content).Run(run);

        Assert.Equal(RunResult.Victory, run.Result);
        Assert.Equal(boss.Id, run.CurrentNodeId);
    }

    [Fact]
    public void Resume_rebuilds_the_identical_generated_map()
    {
        var blueprint = GeneratedRun();
        var run = blueprint.CreateInitialRun(new RunId("run"), randomSeed: 7);

        var save = run.Snapshot();
        Assert.Equal(run.GeneratedMapLoadout, save.MapGenerationLoadout);

        var rebuilt = blueprint.BuildRunMap(save.RandomSeed, save.MapGenerationLoadout ?? 0);
        Assert.Equal(Signature(run.Map), Signature(rebuilt));
    }

    [Fact]
    public void The_same_seed_generates_the_same_map_but_different_seeds_differ()
    {
        var blueprint = GeneratedRun();
        var a = blueprint.CreateInitialRun(new RunId("a"), randomSeed: 7).Map;
        var b = blueprint.CreateInitialRun(new RunId("b"), randomSeed: 7).Map;
        var c = blueprint.CreateInitialRun(new RunId("c"), randomSeed: 8).Map;

        Assert.Equal(Signature(a), Signature(b));
        Assert.NotEqual(Signature(a), Signature(c));
    }

    private static string Signature(RunMap map) =>
        string.Join("|", map.Nodes.Select(n => n.Id.Value)) + "##"
        + string.Join("|", map.Edges.Select(e => $"{e.From.Value}->{e.To.Value}")) + "##"
        + string.Join("|", map.EntryNodeIds.Select(id => id.Value));
}
