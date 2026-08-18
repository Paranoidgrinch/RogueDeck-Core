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

    // Added for rule-based generation (RuleBasedMapGenerator): a reward/treasure node and a crafting workbench
    // node. Appended (not inserted) so the existing members keep their ordinal values. The content delegate maps
    // each role to a concrete NodeType + payload — Workbench → WorkbenchRef, Treasure → the game's reward node.
    Treasure,
    Workbench,

    // A Mimic is NOT a placed topology kind — it is never drawn by the varied rows or gate funnels. It exists
    // only as a realization outcome + encounter-pool key: a Treasure node flips into a Mimic combat with a
    // per-act chance (MapGenerationSpec.TreasureMimicChancePercent), drawing its fight from Encounters[Mimic]
    // (tuned ≈ a weak elite of that act). Appended so existing ordinals are unchanged.
    Mimic,
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

        var rng = new MapGenRandom(seed);

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
                builder.AddNode(new NodeId(MapWiring.Id(r, c)), realized.Type, realized.Payload);
            }

        for (var c = 0; c < widths[0]; c++)
            builder.Entry(MapWiring.Id(0, c)); // the player picks a starting column

        for (var r = 0; r < spec.Rows; r++)
            MapWiring.WireRows(builder, r, widths[r], widths[r + 1], rng);

        return builder.Build();
    }

    // Distribute node kinds: entry row is Combat, the row just below the boss is Rest (a campfire before the fight),
    // the top row is the Boss, and middle rows draw a weighted kind (Elite only from row 2 on).
    private static MapNodeKind KindFor(MapCoord coord, LayeredMapSpec spec, MapGenRandom rng)
    {
        if (coord.Row == spec.Rows)
            return MapNodeKind.Boss;
        if (coord.Row == 0)
            return MapNodeKind.Combat;
        if (coord.Row == spec.Rows - 1)
            return MapNodeKind.Rest;
        return WeightedMiddleKind(coord.Row, rng);
    }

    private static MapNodeKind WeightedMiddleKind(int row, MapGenRandom rng)
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
}
