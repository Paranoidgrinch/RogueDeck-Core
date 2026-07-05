using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Run.Tests;

// Tests for the combat bridge's run projection (Phase G1): the resolver projects the run deck onto the hero
// (via the deck mapper) and injects each relic's combat contributions into the spawned fight, all on the
// still-mutable blueprint before it is driven.
public class CombatBridgeProjectionTests
{
    // Captures the blueprint it is handed (after projection) and returns a fixed result without running combat.
    private sealed class CapturingDriver : ICombatDriver
    {
        public ScenarioBlueprint? Captured { get; private set; }

        public CombatDriveResult Drive(Playthrough playthrough)
        {
            Captured = playthrough.Blueprint;
            return new CombatDriveResult(CombatResult.Victory, playthrough.Blueprint.Hero!.CurrentHealth ?? 0);
        }
    }

    // A minimal triggered effect definition used only to prove combat contributions are injected.
    private sealed class MarkerContribution : ITriggeredEffectDefinition
    {
        public MarkerContribution(string id) => Id = new TriggeredEffectDefinitionId(id);
        public TriggeredEffectDefinitionId Id { get; }
        public Type EventType => typeof(MarkerContribution); // never actually raised in this test
    }

    private static Playthrough BuildEncounter(RunState run)
    {
        var blueprint = new ScenarioBlueprint
        {
            Hero = new HeroBlueprint("knight")
            {
                MaxHealth = run.Health.Max,
                CurrentHealth = run.Health.Current,
            },
        };
        blueprint.Hero.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));
        // Deliberately does NOT add the deck — the bridge projects it.
        var goblin = new EnemyBlueprint("goblin") { MaxHealth = 5 };
        blueprint.Enemies.Add(goblin);
        return new Playthrough(blueprint, new ScenarioScript().Build(), combatId: "fight");
    }

    private static RunState NewRun(params string[] deck)
    {
        var map = new RunMap(Array.Empty<Node>());
        var run = new RunState(new RunId("run"), new HealthState(25, 30), map);
        foreach (var card in deck)
            run.AddDeckCard(new CardDefinitionId(card));
        return run;
    }

    private static NodeResolveContext Context(RunState run, RunDefinitionRegistry registry) =>
        new(run, new ScriptedChoiceProvider(), registry, new RunEffectProcessor());

    private static RunDefinitionRegistry Registry()
    {
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        return builder.Build();
    }

    [Fact]
    public void Bridge_projects_the_run_deck_onto_the_hero()
    {
        var run = NewRun("strike", "strike", "defend");
        var driver = new CapturingDriver();
        var resolver = new CombatNodeResolver(driver);
        var node = new Node(new NodeId("fight"), StandardRunIds.CombatNode, new CombatNodePayload(BuildEncounter));

        resolver.Resolve(Context(run, Registry()), node);

        var deck = driver.Captured!.Hero!.Deck;
        Assert.Equal(3, deck.Count);
        Assert.Equal(
            new[] { "strike", "strike", "defend" },
            deck.Select(e => e.Card.ToString()).ToArray());
    }

    [Fact]
    public void Deck_mapper_lets_upgrades_map_to_a_different_combat_card()
    {
        var run = NewRun("strike", "strike");
        run.Deck[0].Upgrade(); // first copy is upgraded

        var driver = new CapturingDriver();
        // Convention: an upgraded copy fights as "<id>+".
        var resolver = new CombatNodeResolver(driver, card =>
            card.UpgradeLevel > 0 ? new CardDefinitionId(card.DefinitionId + "+") : card.DefinitionId);
        var node = new Node(new NodeId("fight"), StandardRunIds.CombatNode, new CombatNodePayload(BuildEncounter));

        resolver.Resolve(Context(run, Registry()), node);

        Assert.Equal(
            new[] { "strike+", "strike" },
            driver.Captured!.Hero!.Deck.Select(e => e.Card.ToString()).ToArray());
    }

    [Fact]
    public void Relic_combat_contributions_are_injected_into_the_fight()
    {
        var run = NewRun("strike");
        var marker = new MarkerContribution("relic-buff");
        run.AddRelic(new RelicInstance(new RelicDefinition(
            new RelicId("warstone"), "Warstone",
            combatContributions: new ITriggeredEffectDefinition[] { marker })));

        var driver = new CapturingDriver();
        var resolver = new CombatNodeResolver(driver);
        var node = new Node(new NodeId("fight"), StandardRunIds.CombatNode, new CombatNodePayload(BuildEncounter));

        resolver.Resolve(Context(run, Registry()), node);

        Assert.Contains(marker, driver.Captured!.TriggeredPrograms);
    }

    [Fact]
    public void Relic_combat_rules_authored_as_data_reach_the_fight()
    {
        // End-to-end wiring of face (b) as data: a RelicData combat rule → ToDefinition → the relic's combat
        // contribution → injected into the spawned fight as a TriggeredProgramDefinition for the trigger's event.
        var run = NewRun("strike");
        var relic = new RelicData
        {
            Id = "aegis",
            DisplayName = "Aegis",
            CombatRules = new[]
            {
                new RelicCombatRule
                {
                    Trigger = "turnStarted",
                    Program = RelicCombatTriggers.Get("turnStarted").NewProgram(),
                    Priority = 0,
                },
            },
        }.ToDefinition();
        run.AddRelic(new RelicInstance(relic));

        var driver = new CapturingDriver();
        var resolver = new CombatNodeResolver(driver);
        var node = new Node(new NodeId("fight"), StandardRunIds.CombatNode, new CombatNodePayload(BuildEncounter));

        resolver.Resolve(Context(run, Registry()), node);

        var contribution = Assert.Single(driver.Captured!.TriggeredPrograms);
        Assert.Equal(typeof(TurnStartedCombatEvent), contribution.EventType);
    }
}
