using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Dsl;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Run.Tests;

// Tests that a whole authored run (deck + events + encounters + map) round-trips as JSON and can be run after
// rebuilding against a code-provided combat content library.
public class RunBlueprintTests
{
    private static readonly RunResourceId Gold = StandardRunIds.Gold;
    private static readonly EncounterId GoblinFight = new("goblin-fight");
    private static readonly JsonSerializerOptions Options = RunJson.CreateOptions();

    // The code-provided combat content: card + enemy-action definitions (EffectPrograms), referenced by id.
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

    private static RunBlueprint Demo()
    {
        var shrine = new EventScriptBuilder("shrine")
            .Situation("shrine", "An ancient shrine.", s => s
                .Choice("gold", c => c.TextKey("Loot").GainResource(Gold, 20)))
            .Build();

        var encounter = new EncounterDefinition(
            GoblinFight,
            enemies: new[] { new EncounterEnemy("goblin", 12, new[] { new EnemyActionDefinitionId("slam") }) },
            heroResources: new[] { new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3) });

        var map = new RunMap(new Node[]
        {
            new(new NodeId("shrine"), StandardRunIds.EventNode, new EventRef(new EventId("shrine"))),
            new(new NodeId("fight"), StandardRunIds.CombatNode, new EncounterRef(GoblinFight)),
        });

        var smite = new CardBlueprint("smite")
        {
            Program = new EffectProgram<CardPlayContext>(
                new DealDamageNode<CardPlayContext>(
                    new EventTargetCombatantTargetSelector(), new ConstantExpression<CardPlayContext>(6))),
        };
        var slam = new EnemyActionBlueprint("slam", new ActionIntent("Slam", IntentKind.Attack))
        {
            Program = new EffectProgram<EnemyActionContext>(
                new DealDamageNode<EnemyActionContext>(
                    new EventTargetCombatantTargetSelector(), new ConstantExpression<EnemyActionContext>(4))),
        };

        return new RunBlueprint(
            Enumerable.Repeat(new CardDefinitionId("smite"), 5).ToList(),
            new Dictionary<string, EventScript> { ["shrine"] = shrine },
            new[] { encounter },
            new[] { CardData.From(smite) },
            new[] { EnemyActionData.From(slam) },
            map);
    }

    [Fact]
    public void RunBlueprint_round_trips()
    {
        var json1 = RunJson.ToJson(Demo(), Options);
        var back = RunJson.FromJson<RunBlueprint>(json1, Options);
        Assert.Equal(json1, RunJson.ToJson(back, Options));
    }

    [Fact]
    public void RunStart_round_trips_and_seeds_the_initial_run()
    {
        var blueprint = Demo() with
        {
            Start = new RunStart
            {
                HeroName = "Ironclad",
                MaxHealth = 80,
                StartingHealth = 72,
                Resources = new Dictionary<string, int> { [Gold.Value] = 99 },
            },
        };

        var back = RunJson.FromJson<RunBlueprint>(RunJson.ToJson(blueprint, Options), Options);

        Assert.Equal("Ironclad", back.Start.HeroName);
        Assert.Equal(80, back.Start.MaxHealth);
        Assert.Equal(72, back.Start.StartingHealth);
        Assert.Equal(99, back.Start.Resources[Gold.Value]);

        var run = back.CreateInitialRun(new RunId("t"), randomSeed: 1);
        Assert.Equal(72, run.Health.Current);
        Assert.Equal(80, run.Health.Max);
        Assert.Equal(99, run.GetResource(Gold));
        Assert.Equal(5, run.Deck.Count); // the demo deck (5 smites)
    }

    [Fact]
    public void CombatResources_round_trip()
    {
        var blueprint = Demo() with
        {
            CombatResources = new[]
            {
                new CombatResourceData { Id = "mana", DisplayName = "Mana", StartingAmount = 2, Max = 5, RefillEachTurn = true },
            },
        };

        var back = RunJson.FromJson<RunBlueprint>(RunJson.ToJson(blueprint, Options), Options);

        var mana = Assert.Single(back.CombatResources);
        Assert.Equal("mana", mana.Id);
        Assert.Equal("Mana", mana.DisplayName);
        Assert.Equal(2, mana.StartingAmount);
        Assert.Equal(5, mana.Max);
        Assert.True(mana.RefillEachTurn);
    }

    [Fact]
    public void Default_RunStart_reproduces_the_historical_hard_coded_start()
    {
        var run = Demo().CreateInitialRun(new RunId("t"), randomSeed: 1);

        Assert.Equal(30, run.Health.Current);
        Assert.Equal(40, run.Health.Max);
    }

    [Fact]
    public void A_deserialized_blueprint_builds_and_runs_events_and_combat()
    {
        var blueprint = RunJson.FromJson<RunBlueprint>(RunJson.ToJson(Demo(), Options), Options);

        var contentBuilder = new RunContentRegistryBuilder()
            .SetEncounters(new EncounterCatalog(Library(), blueprint.Encounters));
        foreach (var (id, script) in blueprint.Events)
            contentBuilder.RegisterEvent(new EventId(id), script);
        var content = contentBuilder.Build();

        var defs = new RunDefinitionRegistryBuilder();
        new StandardRunPackage(new AutoPlayCombatDriver(), content).RegisterDefinitions(defs);
        var registry = defs.Build();

        var run = new RunState(new RunId("run"), new HealthState(30, 40), blueprint.Map);
        foreach (var card in blueprint.Deck)
            run.AddDeckCard(card);

        new RunRunner(registry, new ScriptedChoiceProvider("gold"), content: content).Run(run);

        Assert.Equal(20, run.GetResource(Gold));
        Assert.Equal(RunResult.Victory, run.Result);
    }
}
