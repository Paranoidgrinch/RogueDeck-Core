using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;

namespace RogueDeck.Sandbox.Tests;

// The Studio map-screen's auto-layout: authored coords win; the rest are placed by graph depth (column) and order
// within a depth (row), so the SVG draws a readable left-to-right layered graph.
public class MapGraphLayoutTests
{
    private static readonly NodeType Mark = new("event");
    private static Node N(string id) => new(new NodeId(id), Mark, new EventRef(new EventId(id)));
    private static MapEdge E(string from, string to) => new(new NodeId(from), new NodeId(to));

    [Fact]
    public void A_linear_map_stacks_every_node_in_one_column()
    {
        var map = new RunMap(new[] { N("a"), N("b"), N("c") }); // no edges

        var pos = MapGraphLayout.Resolve(map);

        Assert.True(pos["a".Id()].X == pos["b".Id()].X && pos["b".Id()].X == pos["c".Id()].X); // same column
        Assert.True(pos["a".Id()].Y < pos["b".Id()].Y && pos["b".Id()].Y < pos["c".Id()].Y);   // stacked down
    }

    [Fact]
    public void Depth_drives_the_column_so_later_nodes_sit_further_right()
    {
        var map = new RunMap(new[] { N("start"), N("left"), N("right"), N("boss") })
        {
            Edges = [E("start", "left"), E("start", "right"), E("left", "boss"), E("right", "boss")],
        };

        var pos = MapGraphLayout.Resolve(map);

        Assert.True(pos["start".Id()].X < pos["left".Id()].X);   // depth 0 → 1
        Assert.True(pos["left".Id()].X < pos["boss".Id()].X);    // depth 1 → 2
        Assert.Equal(pos["left".Id()].X, pos["right".Id()].X);   // same depth, same column
    }

    [Fact]
    public void An_authored_coordinate_overrides_the_auto_layout()
    {
        var map = new RunMap(new[] { N("a"), N("b") })
        {
            Edges = [E("a", "b")],
            Layout = [new NodeLayout(new NodeId("b"), 555, 42)],
        };

        var pos = MapGraphLayout.Resolve(map);

        Assert.Equal((555, 42), pos["b".Id()]);
    }

    [Fact]
    public void CanvasSize_fits_every_node()
    {
        var map = new RunMap(new[] { N("a") }) { Layout = [new NodeLayout(new NodeId("a"), 300, 200)] };

        var (w, h) = MapGraphLayout.CanvasSize(MapGraphLayout.Resolve(map));

        Assert.True(w >= 300 + MapGraphLayout.NodeWidth);
        Assert.True(h >= 200 + MapGraphLayout.NodeHeight);
    }
}

internal static class NodeIdTestExtensions
{
    public static NodeId Id(this string value) => new(value);
}
