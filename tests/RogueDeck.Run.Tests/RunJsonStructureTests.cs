using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Run.Tests;

// Tests for serializing the authoring structures (S: EventScript, EncounterDefinition, RunMap). Round-trip is
// checked by re-serialization idempotence; EventScript also round-trips functionally (deserialize and run it).
public class RunJsonStructureTests
{
    private static readonly RunResourceId Gold = StandardRunIds.Gold;
    private static readonly JsonSerializerOptions Options = RunJson.CreateOptions();

    private static void RoundTrips<T>(T value) where T : class
    {
        var json1 = RunJson.ToJson(value, Options);
        var back = RunJson.FromJson<T>(json1, Options);
        Assert.Equal(json1, RunJson.ToJson(back, Options));
    }

    private static EventScript Shrine() =>
        new EventScriptBuilder("shrine")
            .Situation("shrine", "An ancient shrine.", s => s
                .Choice("heal", c => c.TextKey("Pray").Heal(8))
                .Choice("gamble", c => c
                    .TextKey("Gamble")
                    .Require(RunExpr.HasResource(Gold, 10))
                    .PayResource(Gold, 10)
                    .GainResource(Gold, RunExpr.RandomRange(0, 30)))
                .Choice("leave", c => c.TextKey("Leave").Then("shrine")))
            .Build();

    [Fact]
    public void EventScript_round_trips()
    {
        RoundTrips(Shrine());
    }

    [Fact]
    public void EventScript_round_trips_functionally()
    {
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        var registry = builder.Build();

        var rebuilt = RunJson.FromJson<EventScript>(RunJson.ToJson(Shrine(), Options), Options);

        var run = new RunState(new RunId("run"), new HealthState(20, 40), new RunMap(Array.Empty<Node>()));
        var node = new Node(new NodeId("n"), StandardRunIds.EventNode, rebuilt);
        var processor = new RunEffectProcessor();
        var context = new NodeResolveContext(run, new ScriptedChoiceProvider("heal"), registry, processor);
        new EventNodeResolver().Resolve(context, node);
        processor.ResolvePending(run, registry);

        Assert.Equal(28, run.Health.Current); // healed 8
    }

    [Fact]
    public void EncounterDefinition_round_trips()
    {
        var encounter = new EncounterDefinition(
            new EncounterId("goblin-fight"),
            enemies: new[]
            {
                new EncounterEnemy("goblin", 12, new[] { new EnemyActionDefinitionId("slam") }),
                new EncounterEnemy("brute", 20, new[] { new EnemyActionDefinitionId("smash") },
                    new[] { new StartingStatusSpec(new StatusDefinitionId("enraged"), Stacks: 1) }),
            },
            heroResources: new[] { new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3) });

        RoundTrips(encounter);
    }

    [Fact]
    public void RunMap_of_data_nodes_round_trips()
    {
        var map = new RunMap(new Node[]
        {
            new(new NodeId("shrine"), StandardRunIds.EventNode, new EventRef(new EventId("shrine"))),
            new(new NodeId("fight"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("goblin-fight"))),
        });

        RoundTrips(map);

        var rebuilt = RunJson.FromJson<RunMap>(RunJson.ToJson(map, Options), Options);
        Assert.Equal(2, rebuilt.Nodes.Count);
        Assert.IsType<EventRef>(rebuilt.Nodes[0].Payload);
        Assert.IsType<EncounterRef>(rebuilt.Nodes[1].Payload);
        Assert.Equal(new EventId("shrine"), ((EventRef)rebuilt.Nodes[0].Payload).Id);
    }

    [Fact]
    public void A_node_with_a_non_data_payload_is_not_serializable()
    {
        var map = new RunMap(new[]
        {
            new Node(new NodeId("f"), StandardRunIds.CombatNode, new CombatNodePayload(_ => throw new Exception())),
        });
        Assert.Throws<NotSupportedException>(() => RunJson.ToJson(map, Options));
    }
}
