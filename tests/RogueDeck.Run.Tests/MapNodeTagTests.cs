using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Dsl;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Run.Tests;

// A generated Elite is an ordinary combat node with an ordinary encounter payload, so the role it was drawn
// for is the one thing realization would otherwise throw away. Node tags keep it — and they have to survive a
// save, or a resumed run would quietly forget which of its fights were elites.
public class MapNodeTagTests
{
    private static readonly JsonSerializerOptions Options = RunJson.CreateOptions();

    [Fact]
    public void A_realized_node_carries_the_role_it_was_generated_for()
    {
        Assert.Equal([MapNodeTags.Elite], MapNodeRealizer.Tags(MapNodeKind.Elite));
        Assert.Equal([MapNodeTags.Treasure], MapNodeRealizer.Tags(MapNodeKind.Treasure));
        Assert.Equal([MapNodeTags.Shop], MapNodeRealizer.Tags(MapNodeKind.Shop));
    }

    [Fact]
    public void Tags_survive_serialization()
    {
        var map = new RunMapBuilder()
            .AddNode(new NodeId("fight"), StandardRunIds.CombatNode,
                new EncounterRef(new EncounterId("e")), [MapNodeTags.Elite])
            .Build();

        var restored = RunJson.FromJson<RunMap>(RunJson.ToJson(map, Options), Options);

        Assert.True(restored.Nodes[0].HasTag(MapNodeTags.Elite));
    }

    // The tags only earn their keep once a reaction can READ them, which means they have to ride along on the
    // events a relic hangs off: nodeEntered when the stop begins, combatResolved when the fight it was is over.
    // One run, one tagged elite fight, two programs — "on entering an Elite" and "after winning at an Elite".
    [Fact]
    public void Tags_ride_along_on_node_entered_and_combat_resolved()
    {
        var map = new RunMapBuilder()
            .AddNode(new NodeId("fight"), StandardRunIds.CombatNode,
                new CombatNodePayload(TrivialFight), [MapNodeTags.Elite])
            .Build();
        var run = NewRun(map);
        run.InstallProgram(GainGoldOn<NodeEnteredRunEvent>(
            "entered", RunEventValues.NodeHasTag(MapNodeTags.Elite), 5));
        run.InstallProgram(GainGoldOn<CombatResolvedRunEvent>(
            "won", RunExpr.And(RunEventValues.CombatWasVictory, RunEventValues.NodeHasTag(MapNodeTags.Elite)), 7));

        new RunRunner(BuildRegistry(), new ScriptedChoiceProvider()).Run(run);

        Assert.Equal(12, run.GetResource(StandardRunIds.Gold));
    }

    // …and an ordinary fight is not an elite: the same two programs pay nothing when the node carries no tag.
    [Fact]
    public void An_untagged_node_answers_no_to_every_tag()
    {
        var map = new RunMapBuilder()
            .AddNode(new NodeId("fight"), StandardRunIds.CombatNode, new CombatNodePayload(TrivialFight))
            .Build();
        var run = NewRun(map);
        run.InstallProgram(GainGoldOn<NodeEnteredRunEvent>(
            "entered", RunEventValues.NodeHasTag(MapNodeTags.Elite), 5));

        new RunRunner(BuildRegistry(), new ScriptedChoiceProvider()).Run(run);

        Assert.Equal(0, run.GetResource(StandardRunIds.Gold));
    }

    // The condition is data, like every other event field — a relic that reads a tag survives a save.
    [Fact]
    public void The_tag_condition_round_trips_as_data()
    {
        var condition = RunEventValues.NodeHasTag(MapNodeTags.Shop);

        var restored = RunJson.FromJson<IRunExpression<bool>>(
            RunJson.ToJson(condition, Options), Options);

        Assert.Equal(MapNodeTags.Shop, Assert.IsType<EventNodeHasTagExpression>(restored).Tag);
    }

    // Asked outside a node event it fails loudly rather than answering a quiet "no" — the same bargain every
    // other event field makes, so a reaction hung on the wrong event says so.
    [Fact]
    public void Asking_for_a_tag_outside_a_node_event_says_so()
    {
        var run = NewRun(new RunMap(Array.Empty<Node>()));
        var context = new RunEvalContext(run, new RunStartedRunEvent(run.Id));

        var error = Assert.Throws<InvalidOperationException>(
            () => RunEventValues.NodeHasTag(MapNodeTags.Elite).Evaluate(context));

        Assert.Contains("RunStartedRunEvent", error.Message, StringComparison.Ordinal);
    }

    private static InstalledRunProgram GainGoldOn<TEvent>(string id, IRunExpression<bool> condition, int gold)
        where TEvent : IRunEvent =>
        new(new RunProgramId(id), new DataTriggeredRunEffect<TEvent>(
            condition, [new GainResourceTemplate(StandardRunIds.Gold, RunExpr.Const(gold))]));

    private static RunDefinitionRegistry BuildRegistry()
    {
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        return builder.Build();
    }

    private static RunState NewRun(RunMap map)
    {
        var run = new RunState(new RunId("run"), new HealthState(30, 30), map);
        run.AddDeckCard(new CardDefinitionId("smite"));
        return run;
    }

    // The smallest honest fight: one 6-damage card, one 5-HP enemy, won on the first play.
    private static Playthrough TrivialFight(RunState run)
    {
        var blueprint = new ScenarioBlueprint();
        blueprint.Cards.Add(new CardBlueprint("smite")
        {
            Program = Effects.Program(Effects.DealDamage(Targets.EventTarget, 6)),
        });
        blueprint.Hero = new HeroBlueprint("knight")
        {
            MaxHealth = run.Health.Max,
            CurrentHealth = run.Health.Current,
        };
        blueprint.Hero.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));
        blueprint.Enemies.Add(new EnemyBlueprint("goblin") { MaxHealth = 5 });

        var script = new ScenarioScript().HeroPlays("smite", "goblin").Build();
        return new Playthrough(blueprint, script, combatId: "fight");
    }

    // An untagged node writes no "tags" property at all, so every map authored before tags existed keeps its
    // exact bytes.
    [Fact]
    public void An_untagged_node_writes_no_tags_property()
    {
        var map = new RunMapBuilder()
            .AddNode(new NodeId("fight"), StandardRunIds.CombatNode, new EncounterRef(new EncounterId("e")))
            .Build();

        Assert.DoesNotContain("tags", RunJson.ToJson(map, Options), StringComparison.Ordinal);
    }
}
