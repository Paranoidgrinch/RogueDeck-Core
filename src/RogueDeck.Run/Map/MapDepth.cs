namespace RogueDeck.Run;

// HOW DEEP INTO AN ACT A ROW SITS, as the percentage every depth gate is authored against
// (MapGenerationSpec.RoleMinimumDepthPercent / NodeRefMinimumDepthPercent / EncounterMinimumDepthPercent).
//
// Row 0 is the entry and the last row is the boss, so the deepest row a DOOR can stand on is `rows - 2`, and
// that row is 100 %. A map with no room between those two is entirely "deep": nothing is gated out of a map
// that short, because there is nowhere else to put it.
//
// It lives here rather than inside a generator because two things have to agree on it exactly: the generator
// that PLACES a room against a gate, and the diagnostics that later say whether the gate held. A second copy of
// this formula would make the diagnostics lie in precisely the case they are consulted for.
public static class MapDepth
{
    public static int Percent(int row, int rows) =>
        rows <= 2 ? 100 : Math.Clamp(row * 100 / (rows - 2), 0, 100);
}
