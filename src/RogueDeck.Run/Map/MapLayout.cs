namespace RogueDeck.Run;

// THE GRID A MAP IS DRAWN ON, in one place, because two sides have to agree about it exactly.
//
// `RunMap.Layout` carries a 2D SCREEN COORDINATE per room (NodeLayout) — it always has, and an authored map's
// coordinates are pixels the author dragged a node to. A generator that knows where its rooms belong therefore
// cannot write "row 3, column 1": it has to write that in the same units, on the same axes, that whoever draws
// the map reads. Get either wrong and nothing errors — the map simply comes out wrong, which is the worst way
// for a coordinate to be wrong.
//
// It was wrong. The strategic generator wrote the raw indices (0, 1, 2) with the COLUMN on X, while the
// frontends read depth off X in units of a cell — so every room in an act divided down to cell (0, 0) and
// landed on one heap, in a build where every test passed. Hence this file: the two constants and the two
// conversions, named, and used by both sides.
//
// THE AXES: X is DEPTH, running left to right — the direction the layered auto-layout has always used
// (MapGraphLayout) and the one a map UI decodes. Y is the LANE, the room's place across its own row. A
// top-down map view turns that quarter-turn when it draws; that is the view's business, not the data's.
public static class MapLayout
{
    // One cell of the drawing grid. The numbers are the auto-layout's, which existed first — what matters is
    // not their value but that a generated coordinate and an auto-placed one land on the same lattice, so a map
    // that has both does not draw half of itself somewhere else.
    public const int CellWidth = 170;
    public const int CellHeight = 84;
    public const int Margin = 12;

    // A room at (depth, lane) as the screen coordinate `RunMap.Layout` wants. Integer, like everything else a
    // seed decides: a map is a contract with a seed, and a position that rounds differently on another machine
    // is a map that draws differently on it.
    public static (int X, int Y) Cell(int depth, int lane) =>
        (depth * CellWidth + Margin, lane * CellHeight + Margin);

    // …and back again, which is what a view does to lay the grid out in its own spacing. The margin divides
    // away, because it is smaller than a cell — that is the only thing it has to be.
    public static int DepthOf(int x) => x / CellWidth;

    public static int LaneOf(int y) => y / CellHeight;
}
