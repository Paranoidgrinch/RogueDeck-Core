using RogueDeck.Bot;
using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Sandbox.Tests;

// ⚠⚠ A DOOR USED TO BE WORTH WHAT WAS DIRECTLY BEHIND IT AND NOTHING ELSE. The map oracle measured the
// price: over twenty-four champion runs the door choice ranked at the 50th percentile of the routes on
// offer — the lightest route taken seven times of twenty-six, the heaviest five. A coin.
//
// MapForesight judges a door by the ROUTE it opens instead. These tests hold it to that, and to the line
// that makes it evidence rather than a cheat: it reads the map the way a PLAYER reads it.
public class MapForesightTests
{
    private static readonly NodeType Room = new("run.event");

    private static Node At(string id, string tag) =>
        new(new NodeId(id), Room, new EventRef(new EventId("nothing")), [tag]);

    // A room is worth 1 if it is a rest, -1 if it is a fight, 0 otherwise — the shape of a role weighting,
    // read through MapRole exactly as the runner reads it.
    private static double Weigh(Node node) => MapRole.Of(node) switch
    {
        MapNodeTags.Rest => 1,
        MapNodeTags.Combat => -1,
        _ => 0,
    };

    // Two doors. The LEFT one is the nicer room but leads into two fights; the RIGHT one is a fight that
    // opens onto two rests. A runner that can only see the next room takes the left every time.
    private static RunMap Trap()
    {
        var nodes = new List<Node>
        {
            At("left", MapNodeTags.Event), At("l1", MapNodeTags.Combat), At("l2", MapNodeTags.Combat),
            At("right", MapNodeTags.Combat), At("r1", MapNodeTags.Rest), At("r2", MapNodeTags.Rest),
        };
        return new RunMap(nodes)
        {
            EntryNodeIds = [new NodeId("left"), new NodeId("right")],
            Edges =
            [
                new MapEdge(new NodeId("left"), new NodeId("l1")),
                new MapEdge(new NodeId("l1"), new NodeId("l2")),
                new MapEdge(new NodeId("right"), new NodeId("r1")),
                new MapEdge(new NodeId("r1"), new NodeId("r2")),
            ],
        };
    }

    [Fact]
    public void A_door_seen_one_room_deep_is_worth_only_that_room()
    {
        var map = Trap();
        var seeing = new MapForesight(map, Weigh, horizon: 1);

        Assert.Equal(0, seeing.Of(map.Nodes[0]));   // left: a quiet room
        Assert.Equal(-1, seeing.Of(map.Nodes[3]));  // right: a fight
    }

    // ⚠ THE WHOLE POINT, IN ONE ASSERT. The same two doors, judged three rooms deep, come out the other way
    // round: the fight that opens onto two rests is worth more than the quiet room that opens onto two
    // fights. Without the lookahead this test reads exactly like the one above it.
    [Fact]
    public void A_door_seen_further_is_judged_by_the_route_it_opens()
    {
        var map = Trap();
        var seeing = new MapForesight(map, Weigh, horizon: 3);

        Assert.Equal(-2, seeing.Of(map.Nodes[0]));  // left: quiet, then two fights
        Assert.Equal(1, seeing.Of(map.Nodes[3]));   // right: one fight, then two rests
    }

    // ⚠ A ROUTE IS WORTH ITS BEST CONTINUATION, not its average and not its worst. The runner will be at
    // the next fork too, and it will choose again there.
    [Fact]
    public void A_route_is_worth_the_best_way_through_it()
    {
        var nodes = new List<Node>
        {
            At("door", MapNodeTags.Event),
            At("good", MapNodeTags.Rest), At("bad", MapNodeTags.Combat),
        };
        var map = new RunMap(nodes)
        {
            EntryNodeIds = [new NodeId("door")],
            Edges =
            [
                new MapEdge(new NodeId("door"), new NodeId("good")),
                new MapEdge(new NodeId("door"), new NodeId("bad")),
            ],
        };

        Assert.Equal(1, new MapForesight(map, Weigh, horizon: 2).Of(nodes[0]));
    }

    // ⚠⚠ THE LINE THAT MAKES THE RUNNER EVIDENCE RATHER THAN A CHEAT. A mimic is a treasure on the map
    // screen, and the foresight inherits the disguise — a runner that steered around mimics would be
    // steering by something no player can see, and every act it then cleared would prove nothing.
    [Fact]
    public void A_mimic_is_still_a_treasure_to_the_runner()
    {
        var mimic = At("box", MapNodeTags.Mimic);
        var map = new RunMap([mimic]) { EntryNodeIds = [mimic.Id] };

        // Weighed as a treasure (0), not as the fight it really is (-1).
        Assert.Equal(0, new MapForesight(map, Weigh, horizon: 4).Of(mimic));
        Assert.Equal(MapNodeTags.Treasure, MapRole.Of(mimic));
    }

    // A horizon longer than the map left is simply the map left — no room is counted twice and a dead end
    // is not punished for ending.
    [Fact]
    public void A_horizon_past_the_end_of_the_map_stops_at_the_end()
    {
        var map = Trap();

        Assert.Equal(
            new MapForesight(map, Weigh, horizon: 3).Of(map.Nodes[3]),
            new MapForesight(map, Weigh, horizon: 40).Of(map.Nodes[3]));
    }
}
