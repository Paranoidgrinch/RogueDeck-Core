using RogueDeck.Run;

namespace RogueDeck.Bot;

// ── WHAT A DOOR LEADS TO ─────────────────────────────────────────────────────────────────────────────────
// Until now a runner chose its door by the ROOM BEHIND IT and nothing else (BotMind.PathWeight): one role,
// one weight, and whatever lay past that room did not exist. The map oracle measured what that costs — over
// twenty-four champion runs the door choice ranked at the 50th percentile of the routes available, with the
// lightest route taken seven times out of twenty-six and the heaviest five. That is a coin.
//
// This is the fix, and it is a graph walk rather than a simulation: from each door, the best total the next
// `horizon` rooms can be worth, so a door is judged by the ROUTE it opens instead of by its first step. A
// rest two rows down is now a reason to turn left.
//
// ⚠⚠ IT KNOWS WHAT A PLAYER LOOKING AT THE MAP KNOWS, AND NOT ONE THING MORE. This is the line that makes
// the runner evidence rather than a cheat, and it is drawn in exactly one place: the weight it walks is a
// function of `MapRole`, the same reading the map screen draws its icons from. So:
//
//     A MIMIC IS A TREASURE HERE. `MapRole.Of` disguises it on purpose, and the foresight inherits the
//     disguise — a runner that steered around mimics would be steering by something no player can see, and
//     every act it then cleared would prove nothing about the act.
//
//     WHICH FIGHT a combat node holds is not read. The map says "combat"; the oracle may look inside that
//     node because it is an instrument, but a player cannot, so neither may this.
//
// The omniscient reading lives in MapOracle and stays there. This file is deliberately the weaker one.
//
// ⚠ A ROUTE'S WORTH IS A SUM, SO IT COUNTS ROOMS AS WELL AS KINDS. On these maps every path through an act
// is the same length (the oracle measures 23, 24, 25 and 35 rooms, each with no variation), so summing is a
// fair comparison between doors. On a map whose routes differed in length it would not be, and the longer
// way round would win or lose on its length alone — which is a real limit of this, not a hidden one.
internal sealed class MapForesight
{
    private readonly Dictionary<string, IReadOnlyList<NodeId>> _onward;
    private readonly Dictionary<string, double> _weight;
    private readonly Dictionary<(string Node, int Depth), double> _best = [];
    private readonly int _horizon;

    public MapForesight(RunMap map, Func<Node, double> weigh, int horizon)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(weigh);
        _horizon = Math.Max(1, horizon);
        _onward = new Dictionary<string, IReadOnlyList<NodeId>>(StringComparer.Ordinal);
        _weight = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var node in map.Nodes)
        {
            _onward[node.Id.Value] = map.SuccessorIds(node.Id);
            _weight[node.Id.Value] = weigh(node);
        }
    }

    // What the best route through this door is worth, counting this room and the next horizon-1 after it.
    public double Of(Node door)
    {
        ArgumentNullException.ThrowIfNull(door);
        return Best(door.Id.Value, _horizon);
    }

    private double Best(string id, int depth)
    {
        if (!_weight.TryGetValue(id, out var here))
            return 0;
        if (depth <= 1)
            return here;
        if (_best.TryGetValue((id, depth), out var known))
            return known;

        // ⚠ THE MEMO IS KEYED ON DEPTH AS WELL AS ROOM, because the same room seen from two doors is a
        // different question when one of them has three rooms of horizon left and the other has five.
        var onward = _onward.GetValueOrDefault(id, []);
        var rest = 0.0;
        var first = true;
        foreach (var next in onward)
        {
            var value = Best(next.Value, depth - 1);
            if (first || value > rest)
                rest = value;
            first = false;
        }

        var total = here + rest;
        _best[(id, depth)] = total;
        return total;
    }
}
