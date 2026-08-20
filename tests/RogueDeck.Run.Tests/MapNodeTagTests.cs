using System.Text.Json;
using RogueDeck.Run;

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
