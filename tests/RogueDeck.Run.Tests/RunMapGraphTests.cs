using System.Text.Json;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// B0 (branching run-map, graph substrate): RunMap gains optional Edges + EntryNodeIds. Pure data — no traversal
// reads them yet (that is B1). These tests pin the additive/back-compat invariants: a map that declares neither is
// exactly today's linear map, and old serialized maps (no `edges`) still load.
public class RunMapGraphTests
{
    private static readonly JsonSerializerOptions Options = RunJson.CreateOptions();

    private static Node Fight(string id) => new(new NodeId(id), StandardRunIds.CombatNode, new EncounterRef(new EncounterId(id)));

    [Fact]
    public void A_map_declares_no_edges_or_entry_nodes_by_default()
    {
        var map = new RunMap(new[] { Fight("a"), Fight("b") });

        Assert.Empty(map.Edges);
        Assert.Empty(map.EntryNodeIds);
    }

    [Fact]
    public void A_graph_map_round_trips_its_edges_and_entry_nodes()
    {
        var map = new RunMap(new[] { Fight("start"), Fight("left"), Fight("right"), Fight("boss") })
        {
            Edges =
            [
                new MapEdge(new NodeId("start"), new NodeId("left")),
                new MapEdge(new NodeId("start"), new NodeId("right")),
                new MapEdge(new NodeId("left"), new NodeId("boss")),
                new MapEdge(new NodeId("right"), new NodeId("boss")),
            ],
            EntryNodeIds = [new NodeId("start")],
            Layout = [new NodeLayout(new NodeId("start"), 10, 20), new NodeLayout(new NodeId("boss"), 10, 200)],
        };

        var json = RunJson.ToJson(map, Options);
        var back = RunJson.FromJson<RunMap>(json, Options);

        Assert.Equal(json, RunJson.ToJson(back, Options));
        Assert.Equal(4, back.Edges.Count);
        Assert.Contains(new MapEdge(new NodeId("start"), new NodeId("right")), back.Edges);
        Assert.Equal(new NodeId("start"), Assert.Single(back.EntryNodeIds));
        Assert.Contains(new NodeLayout(new NodeId("start"), 10, 20), back.Layout); // presentational coords round-trip
    }

    [Fact]
    public void An_old_map_json_without_edges_deserializes_as_a_linear_map()
    {
        // A map serialized before B0 has no `edges` / `entryNodeIds` keys; init defaults keep it linear.
        var back = RunJson.FromJson<RunMap>("{\"Nodes\":[]}", Options);

        Assert.Empty(back.Nodes);
        Assert.Empty(back.Edges);
        Assert.Empty(back.EntryNodeIds);
    }
}
