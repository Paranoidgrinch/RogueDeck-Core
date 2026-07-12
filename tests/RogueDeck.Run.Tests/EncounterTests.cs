using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Dsl;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Run.Tests;

// Tests for data-defined encounters (R7b): a combat node references an EncounterId, and the bridge assembles
// the fight from a shared content library + encounter roster + run projection — no per-node closure.
public class EncounterTests
{
    private static readonly EncounterId GoblinFight = new("goblin-fight");

    // The authored-once combat content: a 6-damage smite and a goblin slam.
    private static CombatContentLibrary Library() => new(
        cards: new[]
        {
            new CardBlueprint("smite") { Program = Effects.Program(Effects.DealDamage(Targets.EventTarget, 6)) },
        },
        enemyActions: new[]
        {
            new EnemyActionBlueprint("slam", new ActionIntent("Slam", IntentKind.Attack))
            {
                Program = new EffectProgram<EnemyActionContext>(new DealDamageNode<EnemyActionContext>(
                    CombatantTargetSelectors.EventTarget, new ConstantExpression<EnemyActionContext>(4))),
            },
        });

    private static EncounterDefinition GoblinEncounter(int goblinHp = 12) => new(
        GoblinFight,
        enemies: new[]
        {
            new EncounterEnemy("goblin", goblinHp, new[] { new EnemyActionDefinitionId("slam") }),
        },
        heroResources: new[] { new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3) });

    private static EncounterCatalog Catalog(int goblinHp = 12) =>
        new(Library(), new[] { GoblinEncounter(goblinHp) });

    private static RunContentRegistry Content(int goblinHp = 12) =>
        new RunContentRegistryBuilder().SetEncounters(Catalog(goblinHp)).Build();

    private static RunState NewRun(int current = 30, int max = 30, params string[] deck)
    {
        var map = new RunMap(new[]
        {
            new Node(new NodeId("fight"), StandardRunIds.CombatNode, new EncounterRef(GoblinFight)),
        });
        var run = new RunState(new RunId("run"), new HealthState(current, max), map);
        foreach (var card in deck)
            run.AddDeckCard(new CardDefinitionId(card));
        return run;
    }

    [Fact]
    public void A_data_encounter_node_runs_to_victory_through_the_run()
    {
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage(new AutoPlayCombatDriver(), Content()).RegisterDefinitions(builder);
        var registry = builder.Build();

        var run = NewRun(deck: new[] { "smite", "smite", "smite", "smite", "smite" });
        new RunRunner(registry, new ScriptedChoiceProvider()).Run(run);

        Assert.Equal(RunResult.Victory, run.Result);
        Assert.Contains(run.EventHistory.OfType<CombatResolvedRunEvent>(),
            e => e.Result == CombatResult.Victory);
    }

    [Fact]
    public void Run_global_combat_resources_are_injected_into_every_hero_with_refills()
    {
        // A custom "mana" resource defined run-global (via the library) must appear on the fight's hero with its
        // starting/max, plus a per-turn refill — alongside the encounter's own energy.
        var library = new CombatContentLibrary(
            heroResources: new[] { new ResourceSpec(new ResourceId("mana"), 2, 5) },
            heroResourceRefills: new[] { new ResourceRefillSpec(new ResourceId("mana"), 5) });
        var catalog = new EncounterCatalog(library, new[] { GoblinEncounter() });

        var playthrough = catalog.Build(GoblinFight, NewRun(), randomSeed: 1);

        var mana = Assert.Single(playthrough.Blueprint.Hero.Resources, r => r.Resource.value == "mana");
        Assert.Equal(2, mana.Current);
        Assert.Equal(5, mana.Max);
        Assert.Contains(playthrough.Blueprint.TurnStartResourceRefills, r => r.Resource.value == "mana" && r.Max == 5);
        Assert.Contains(playthrough.Blueprint.Hero.Resources, r => r.Resource == StandardCombatIds.EnergyResource);
    }

    [Fact]
    public void An_encounter_resource_id_is_not_overridden_by_a_run_global_one()
    {
        // The encounter already defines energy 3/3; a run-global energy must not add a second pool or override it.
        var library = new CombatContentLibrary(
            heroResources: new[] { new ResourceSpec(StandardCombatIds.EnergyResource, 99, 99) });
        var catalog = new EncounterCatalog(library, new[] { GoblinEncounter() });

        var playthrough = catalog.Build(GoblinFight, NewRun(), randomSeed: 1);

        var energy = Assert.Single(
            playthrough.Blueprint.Hero.Resources, r => r.Resource == StandardCombatIds.EnergyResource);
        Assert.Equal(3, energy.Current); // the encounter's 3/3 wins
    }

    [Fact]
    public void The_encounter_uses_the_run_deck_and_hp_projection()
    {
        var builder = new RunDefinitionRegistryBuilder();
        // Capturing driver records the assembled blueprint instead of playing.
        var capture = new CapturingDriver();
        new StandardRunPackage(capture, Content()).RegisterDefinitions(builder);
        var registry = builder.Build();

        var run = NewRun(current: 22, max: 30, deck: new[] { "smite", "smite" });
        new RunRunner(registry, new ScriptedChoiceProvider()).Run(run);

        var bp = capture.Captured!;
        Assert.Equal(30, bp.Hero!.MaxHealth);
        Assert.Equal(22, bp.Hero!.CurrentHealth);          // HP projected from the run
        Assert.Equal(2, bp.Hero!.Deck.Count);              // deck projected from the run
        Assert.Single(bp.Enemies);
        Assert.Equal(12, bp.Enemies[0].MaxHealth);         // roster from the encounter
    }

    [Fact]
    public void An_unknown_encounter_id_faults_clearly()
    {
        var resolver = new CombatNodeResolver(new AutoPlayCombatDriver(), encounters: Catalog());
        var run = NewRun();
        var node = new Node(new NodeId("x"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("nope")));
        var context = new NodeResolveContext(run, new ScriptedChoiceProvider(),
            new RunDefinitionRegistryBuilder().Build(), new RunEffectProcessor());

        Assert.Throws<InvalidOperationException>(() => resolver.Resolve(context, node));
    }

    [Fact]
    public void An_encounter_ref_without_a_catalog_faults_clearly()
    {
        var resolver = new CombatNodeResolver(new AutoPlayCombatDriver()); // no catalog
        var run = NewRun();
        var node = new Node(new NodeId("fight"), StandardRunIds.CombatNode, new EncounterRef(GoblinFight));
        var context = new NodeResolveContext(run, new ScriptedChoiceProvider(),
            new RunDefinitionRegistryBuilder().Build(), new RunEffectProcessor());

        Assert.Throws<InvalidOperationException>(() => resolver.Resolve(context, node));
    }

    [Fact]
    public void Intent_rules_project_from_the_encounter_into_the_enemy_blueprint()
    {
        var encounter = new EncounterDefinition(
            GoblinFight,
            enemies: new[]
            {
                new EncounterEnemy("goblin", 12, new[] { new EnemyActionDefinitionId("slam") },
                    IntentRules: new[]
                    {
                        new EnemyIntentRule(
                            new EnemyHealthPercentCondition(ComparisonOperator.LessOrEqual, 50),
                            new EnemyActionDefinitionId("slam"), Priority: 2),
                    }),
            },
            heroResources: new[] { new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3) });

        var playthrough = new EncounterCatalog(Library(), new[] { encounter })
            .Build(GoblinFight, NewRun(deck: new[] { "smite" }), randomSeed: 1);

        var goblin = playthrough.Blueprint.Enemies.Single(e => e.Id == "goblin");
        var rule = Assert.Single(goblin.IntentRules);
        Assert.Equal(2, rule.Priority);
        Assert.IsType<EnemyHealthPercentCondition>(rule.Condition);
    }

    private sealed class CapturingDriver : ICombatDriver
    {
        public ScenarioBlueprint? Captured { get; private set; }
        public CombatDriveResult Drive(Playthrough playthrough)
        {
            Captured = playthrough.Blueprint;
            return new CombatDriveResult(CombatResult.Victory, playthrough.Blueprint.Hero!.CurrentHealth ?? 0);
        }
    }
}
