namespace RogueDeck.Run;

// Shared topology helpers for layered-act map generators (LayeredMapGenerator + RuleBasedMapGenerator): the node-id
// scheme and forward-only row-to-row wiring. Every edge runs row r → r+1 (so the graph is always a DAG), each node
// connects to its nearest column in the next row plus an occasional adjacent branch (a real fork), and a fix-up
// guarantees every next-row node has an incoming edge (full reachability). Because the last pre-boss row wires
// forward into a width-1 boss row, the boss is the single leaf every path converges into. One source of truth so
// both generators produce the same connected, single-boss-leaf topology.
internal static class MapWiring
{
    public static string Id(int row, int col) => $"r{row}c{col}";

    // Wire row r (width w) into row r+1 (width w2).
    public static void WireRows(RunMapBuilder builder, int r, int w, int w2, MapGenRandom rng)
    {
        var incoming = new bool[w2];

        for (var i = 0; i < w; i++)
        {
            var mid = Nearest(i, w, w2);
            Connect(builder, r, i, mid, incoming);

            // Occasionally branch to an adjacent column for a real fork (never off the ends).
            if (w2 > 1 && rng.Next(3) == 0)
            {
                var side = rng.Next(2) == 0 ? mid - 1 : mid + 1;
                if (side >= 0 && side < w2)
                    Connect(builder, r, i, side, incoming);
            }
        }

        // Any next-row node still unreached gets an edge from its nearest current-row node.
        for (var j = 0; j < w2; j++)
            if (!incoming[j])
                Connect(builder, r, Nearest(j, w2, w), j, incoming);
    }

    private static void Connect(RunMapBuilder builder, int r, int from, int to, bool[] incoming)
    {
        builder.Connect(Id(r, from), Id(r + 1, to));
        incoming[to] = true;
    }

    // The column in a row of width `toWidth` that lines up with column `i` of a row of width `fromWidth`.
    private static int Nearest(int i, int fromWidth, int toWidth) =>
        toWidth == 1 ? 0 : (int)Math.Round(i * (double)(toWidth - 1) / Math.Max(1, fromWidth - 1));
}
