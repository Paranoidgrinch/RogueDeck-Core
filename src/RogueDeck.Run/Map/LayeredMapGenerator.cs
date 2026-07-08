using RogueDeck.Core.Combat;

namespace RogueDeck.Run;

// The role a generated map node plays — decoupled from NodeType so the generator owns topology + distribution and
// the caller owns content (which encounter/event a Combat/Shop/… node actually runs).
public enum MapNodeKind
{
    Combat,
    Elite,
    Event,
    Shop,
    Rest,
    Boss,
}

// A node's grid position in a layered act: Row 0 is the entry row, the last row is the boss.
public readonly record struct MapCoord(int Row, int Column);

// What the caller realizes a generated node as: its NodeType + data payload (e.g. combat -> EncounterRef, event ->
// EventRef). The generator supplies the kind + coordinate; the caller returns the concrete content.
public readonly record struct NodeContent(NodeType Type, object Payload);

// Shape of a layered act: `Rows` rows of branching combat/event/shop/etc. nodes, then a single boss row on top.
// Each row's width is drawn in [MinWidth, MaxWidth]. Deterministic from a seed.
public sealed record LayeredMapSpec(int Rows, int MinWidth = 2, int MaxWidth = 4)
{
    public void Validate()
    {
        if (Rows < 1)
            throw new ArgumentOutOfRangeException(nameof(Rows), Rows, "An act needs at least one row before the boss.");
        if (MinWidth < 1)
            throw new ArgumentOutOfRangeException(nameof(MinWidth), MinWidth, "Row width must be at least 1.");
        if (MaxWidth < MinWidth)
            throw new ArgumentOutOfRangeException(nameof(MaxWidth), MaxWidth, "MaxWidth must be >= MinWidth.");
    }
}

// Generates a Slay-the-Spire-style layered act as a graph RunMap: rows of nodes with forward-only edges (row r ->
// row r+1, so the result is always a DAG), every node connected (reachable from an entry, with an outgoing path),
// and a single boss leaf every last-row node feeds into. Node kinds are distributed with sensible defaults (row 0
// is Combat, the row before the boss is Rest, the boss row is Boss, middle rows are weighted-random). Deterministic
// from `seed`; the caller's `content` delegate turns each (kind, coordinate) into a concrete Node. The output
// always passes RunMapValidator.
public static class LayeredMapGenerator
{
    public static RunMap Generate(LayeredMapSpec spec, int seed, Func<MapNodeKind, MapCoord, NodeContent> content)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(content);
        spec.Validate();

        var rng = new SeededRng(seed);

        // Row widths: rows 0..Rows-1 are branching rows; row `Rows` is the single boss node.
        var widths = new int[spec.Rows + 1];
        for (var r = 0; r < spec.Rows; r++)
            widths[r] = spec.MinWidth + rng.Next(spec.MaxWidth - spec.MinWidth + 1);
        widths[spec.Rows] = 1; // boss

        var builder = new RunMapBuilder();
        for (var r = 0; r <= spec.Rows; r++)
            for (var c = 0; c < widths[r]; c++)
            {
                var coord = new MapCoord(r, c);
                var kind = KindFor(coord, spec, rng);
                var realized = content(kind, coord);
                builder.AddNode(new NodeId(Id(r, c)), realized.Type, realized.Payload);
            }

        for (var c = 0; c < widths[0]; c++)
            builder.Entry(Id(0, c)); // the player picks a starting column

        for (var r = 0; r < spec.Rows; r++)
            WireRows(builder, r, widths[r], widths[r + 1], rng);

        return builder.Build();
    }

    // Distribute node kinds: entry row is Combat, the row just below the boss is Rest (a campfire before the fight),
    // the top row is the Boss, and middle rows draw a weighted kind (Elite only from row 2 on).
    private static MapNodeKind KindFor(MapCoord coord, LayeredMapSpec spec, SeededRng rng)
    {
        if (coord.Row == spec.Rows)
            return MapNodeKind.Boss;
        if (coord.Row == 0)
            return MapNodeKind.Combat;
        if (coord.Row == spec.Rows - 1)
            return MapNodeKind.Rest;
        return WeightedMiddleKind(coord.Row, rng);
    }

    private static MapNodeKind WeightedMiddleKind(int row, SeededRng rng)
    {
        // Combat is the backbone; the rest sprinkle in variety. Elites are gated out of the earliest rows.
        var pick = rng.Next(100);
        if (pick < 55)
            return MapNodeKind.Combat;
        if (pick < 75)
            return MapNodeKind.Event;
        if (pick < 85)
            return row >= 2 ? MapNodeKind.Elite : MapNodeKind.Combat;
        if (pick < 93)
            return MapNodeKind.Shop;
        return MapNodeKind.Rest;
    }

    // Wire row r (width w) into row r+1 (width w2): each node connects to its nearest column in the next row plus
    // an occasional adjacent column (branching), then a fix-up guarantees every next-row node has an incoming edge
    // (so nothing is unreachable). Forward-only, so the whole graph stays acyclic.
    private static void WireRows(RunMapBuilder builder, int r, int w, int w2, SeededRng rng)
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

    private static string Id(int row, int col) => $"r{row}c{col}";

    // A tiny deterministic RNG over CombatRandom (the same hashing the run/combat layers use), so a seed reproduces
    // a map. Each draw advances a local step, mirroring RunState.NextRandom.
    private sealed class SeededRng
    {
        private readonly int _seed;
        private int _step;

        public SeededRng(int seed) => _seed = seed;

        public int Next(int maxExclusive) =>
            maxExclusive <= 1 ? 0 : CombatRandom.CreateShuffledIndexes(maxExclusive, _seed, _step++)[0];
    }
}
