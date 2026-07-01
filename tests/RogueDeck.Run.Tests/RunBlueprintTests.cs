using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Tests that a whole authored run (deck + events + map) round-trips as JSON and can be run after rebuilding.
public class RunBlueprintTests
{
    private static readonly RunResourceId Gold = StandardRunIds.Gold;
    private static readonly JsonSerializerOptions Options = RunJson.CreateOptions();

    private static RunBlueprint Demo()
    {
        var shrine = new EventScriptBuilder("shrine")
            .Situation("shrine", "An ancient shrine.", s => s
                .Choice("heal", c => c.TextKey("Pray").Heal(8))
                .Choice("gold", c => c.TextKey("Loot").GainResource(Gold, 20)))
            .Build();

        var map = new RunMap(new Node[]
        {
            new(new NodeId("shrine"), StandardRunIds.EventNode, new EventRef(new EventId("shrine"))),
        });

        return new RunBlueprint(
            new[] { new CardDefinitionId("smite"), new CardDefinitionId("smite") },
            new Dictionary<string, EventScript> { ["shrine"] = shrine },
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
    public void A_deserialized_blueprint_builds_and_runs()
    {
        var blueprint = RunJson.FromJson<RunBlueprint>(RunJson.ToJson(Demo(), Options), Options);

        var contentBuilder = new RunContentRegistryBuilder();
        foreach (var (id, script) in blueprint.Events)
            contentBuilder.RegisterEvent(new EventId(id), script);
        var content = contentBuilder.Build();

        var defs = new RunDefinitionRegistryBuilder();
        new StandardRunPackage(content: content).RegisterDefinitions(defs);
        var registry = defs.Build();

        var run = new RunState(new RunId("run"), new HealthState(20, 40), blueprint.Map);
        foreach (var card in blueprint.Deck)
            run.AddDeckCard(card);

        new RunRunner(registry, new ScriptedChoiceProvider("gold"), content: content).Run(run);

        Assert.Equal(20, run.GetResource(Gold));
        Assert.Equal(2, run.Deck.Count);
    }
}
