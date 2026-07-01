using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Tests for the run content registry: id-keyed content (events/relics/reward tables/encounters), map
// validation of references, and an EventRef node resolving through the run.
public class RunContentRegistryTests
{
    private static readonly EventId Shrine = new("shrine");
    private static readonly RunResourceId Gold = StandardRunIds.Gold;

    private static EventScript ShrineEvent() =>
        new EventScriptBuilder("shrine")
            .Situation("shrine", "t", s => s.Choice("bless", c => c.GainResource(Gold, 25)))
            .Build();

    private static RunDefinitionRegistry Definitions(RunContentRegistry content)
    {
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage(content: content).RegisterDefinitions(builder);
        return builder.Build();
    }

    [Fact]
    public void Registry_looks_up_registered_content()
    {
        var content = new RunContentRegistryBuilder()
            .RegisterEvent(Shrine, ShrineEvent())
            .RegisterRelic(StandardRelics.Bloodstone())
            .RegisterRewardTable(new RewardTableId("common"), RewardTable.Of(Rewards.Gold(10)))
            .Build();

        Assert.True(content.HasEvent(Shrine));
        Assert.NotNull(content.GetEvent(Shrine));
        Assert.Equal(new RelicId("bloodstone"), content.GetRelic(new RelicId("bloodstone")).Id);
        Assert.NotNull(content.GetRewardTable(new RewardTableId("common")));
    }

    [Fact]
    public void Duplicate_registration_throws()
    {
        var builder = new RunContentRegistryBuilder().RegisterEvent(Shrine, ShrineEvent());
        Assert.Throws<InvalidOperationException>(() => builder.RegisterEvent(Shrine, ShrineEvent()));
    }

    [Fact]
    public void Unknown_lookup_throws_clearly()
    {
        var content = new RunContentRegistryBuilder().Build();
        Assert.Throws<InvalidOperationException>(() => content.GetEvent(new EventId("missing")));
    }

    [Fact]
    public void Validate_passes_for_a_map_whose_references_resolve()
    {
        var content = new RunContentRegistryBuilder().RegisterEvent(Shrine, ShrineEvent()).Build();
        var definitions = Definitions(content);
        var map = new RunMap(new[] { new Node(new NodeId("n"), StandardRunIds.EventNode, new EventRef(Shrine)) });

        content.Validate(map, definitions); // does not throw
    }

    [Fact]
    public void Validate_reports_unknown_references_and_missing_resolvers()
    {
        var content = new RunContentRegistryBuilder().Build();
        var definitions = Definitions(content);
        var map = new RunMap(new[]
        {
            new Node(new NodeId("a"), StandardRunIds.EventNode, new EventRef(new EventId("ghost"))),
            new Node(new NodeId("b"), new NodeType("mystery"), "payload"),
        });

        var ex = Assert.Throws<InvalidOperationException>(() => content.Validate(map, definitions));
        Assert.Contains("unknown event 'ghost'", ex.Message);
        Assert.Contains("no registered resolver", ex.Message);
    }

    [Fact]
    public void An_EventRef_node_resolves_through_the_run()
    {
        var content = new RunContentRegistryBuilder().RegisterEvent(Shrine, ShrineEvent()).Build();
        var definitions = Definitions(content);

        var run = new RunState(new RunId("run"), new HealthState(30, 40),
            new RunMap(new[] { new Node(new NodeId("n"), StandardRunIds.EventNode, new EventRef(Shrine)) }));

        new RunRunner(definitions, new ScriptedChoiceProvider("bless")).Run(run);

        Assert.Equal(25, run.GetResource(Gold));
    }
}
