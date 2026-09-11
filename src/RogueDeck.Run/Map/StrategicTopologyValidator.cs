namespace RogueDeck.Run;

// WHAT THE WALK PROMISED, CHECKED AGAINST WHAT IT PRODUCED.
//
// The topology generator makes its guarantees by construction, which is the right way to make them and the worst
// way to be believed: "by construction" is an argument about code, and the code will be changed. So every one of
// them is also a measurement here, and the fuzz test runs this over 10 000 seeds per act length. A clean result
// is the licence for S5–S10 to build on the shape without re-deriving whether it is sound.
//
// Every check is O(V + E) or an O(E log E) sort, so validating is cheap enough to run on every generated map in
// a debug build and not only in the tests.
public static class StrategicTopologyValidator
{
    // Every way `topology` fails to be the act it was asked for. Empty = sound. Human-readable on purpose: from
    // S10 these strings are what the generator's diagnostic exception carries, and a seed that cannot be shaped
    // has to say why in a bug report.
    public static IReadOnlyList<string> Validate(
        StrategicTopology topology, int rows, int bossRooms, int minWidth, int maxWidth,
        StrategicTopologyRules rules)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(rules);

        var problems = new List<string>();

        // ── The act is the length it was authored to be. The whole point of §12's "act length is a structural
        // invariant": nothing inserts a row for a guarantee and nothing subtracts one, so this is not a sanity
        // check but the invariant itself.
        if (topology.Rows.Count != rows)
            problems.Add($"The act should have {rows} rows, but has {topology.Rows.Count}.");

        for (var index = 0; index < topology.Rows.Count; index++)
            if (topology.Rows[index].Index != index)
                problems.Add($"Row at position {index} calls itself row {topology.Rows[index].Index}.");

        // ── Slots: unique ids, and a slot that agrees with where it sits.
        var seen = new HashSet<NodeId>();
        foreach (var row in topology.Rows)
            for (var column = 0; column < row.Width; column++)
            {
                var slot = row.Slots[column];
                if (!seen.Add(slot.Id))
                    problems.Add($"Two rooms share the id {slot.Id.Value}.");
                if (slot.Row != row.Index || slot.Column != column)
                    problems.Add($"Room {slot.Id.Value} sits at r{row.Index}c{column} but says r{slot.Row}c{slot.Column}.");
            }

        // ── Width. Pre-boss rows stay inside the configured bounds and move by at most one row to row (that is
        // the state machine's promise: coherent stretches, not a fresh dice roll per row). Boss rows are one
        // room wide and are the LAST rows — an act that ends anywhere else is not an act.
        var preBossRows = rows - bossRooms;
        foreach (var row in topology.Rows)
        {
            var shouldBeBoss = row.Index >= preBossRows;
            if (shouldBeBoss && (row.Width != 1 || !row.Slots[0].IsBoss))
                problems.Add($"Row {row.Index} should be a single boss room, but is {row.Width} wide.");
            if (!shouldBeBoss && row.Slots.Any(slot => slot.IsBoss))
                problems.Add($"Row {row.Index} is not a boss row but holds a boss room.");
            if (!shouldBeBoss && (row.Width < minWidth || row.Width > maxWidth))
                problems.Add($"Row {row.Index} is {row.Width} wide, outside the configured {minWidth}..{maxWidth}.");
        }

        for (var index = 1; index < preBossRows && index < topology.Rows.Count; index++)
        {
            var step = Math.Abs(topology.Rows[index].Width - topology.Rows[index - 1].Width);
            if (step > 1)
                problems.Add($"Row {index} changes the width by {step}; one row is one operation, so at most 1.");
        }

        // ── Edges. Each one steps exactly one row forward, which makes the graph layered and therefore acyclic
        // without a cycle search.
        foreach (var edge in topology.Edges)
        {
            if (!topology.TryGetSlot(edge.From, out var from) || !topology.TryGetSlot(edge.To, out var to))
            {
                problems.Add($"Edge {edge.From.Value} → {edge.To.Value} names a room the act does not have.");
                continue;
            }
            if (to!.Row != from!.Row + 1)
                problems.Add($"Edge {edge.From.Value} → {edge.To.Value} spans r{from.Row} → r{to.Row}; rows must be consecutive.");
        }

        // ── Reachability, both ways, by induction over the layering: every room below row 0 is walked into, and
        // every room above the last is walked out of. Together with consecutive rows that means every room is
        // reachable from an entry AND reaches the boss — the two properties the acceptance criteria ask for,
        // without a traversal.
        foreach (var row in topology.Rows)
            foreach (var slot in row.Slots)
            {
                if (row.Index > 0 && topology.PredecessorsOf(slot.Id).Count == 0)
                    problems.Add($"Room {slot.Id.Value} cannot be reached: nothing leads into it.");
                if (row.Index < topology.Rows.Count - 1 && topology.SuccessorsOf(slot.Id).Count == 0)
                    problems.Add($"Room {slot.Id.Value} is a dead end: it cannot reach the boss.");
            }

        // ── Planarity. Drawn in column order, no two edges out of a row may run opposite ways. Checked twice
        // over: the pairwise count (the same rule MapDiagnostics applies to a realized map) and the monotonicity
        // that makes it true — sort a row's edges by source column, and the target columns may never go back.
        if (topology.Crossings > 0)
            problems.Add($"The act has {topology.Crossings} crossing edge pair(s); merges between neighbours only should make that impossible.");

        foreach (var group in topology.Edges
            .Where(edge => topology.TryGetSlot(edge.From, out _) && topology.TryGetSlot(edge.To, out _))
            .GroupBy(edge => topology.Slot(edge.From).Row))
        {
            var ordered = group
                .Select(edge => (From: topology.Slot(edge.From).Column, To: topology.Slot(edge.To).Column))
                .OrderBy(pair => pair.From).ThenBy(pair => pair.To)
                .ToList();
            for (var index = 1; index < ordered.Count; index++)
                if (ordered[index].To < ordered[index - 1].To)
                    problems.Add($"Row {group.Key} wires c{ordered[index - 1].From}→c{ordered[index - 1].To} and c{ordered[index].From}→c{ordered[index].To}, which cross.");
        }

        problems.AddRange(StrandProblems(topology, preBossRows, rules));
        return problems;
    }

    // The same, on the generator's default shape.
    public static IReadOnlyList<string> Validate(StrategicTopology topology, int rows) =>
        Validate(topology, rows, bossRooms: 1, minWidth: 2, maxWidth: 4, new StrategicTopologyRules());

    // The strand rules: identity is continuous, it never teleports sideways, and a branch lives long enough to
    // have been worth choosing.
    private static IEnumerable<string> StrandProblems(
        StrategicTopology topology, int preBossRows, StrategicTopologyRules rules)
    {
        var problems = new List<string>();
        var declared = new Dictionary<StrandId, StrategicStrand>();
        foreach (var strand in topology.Strands)
            if (!declared.TryAdd(strand.Id, strand))
                problems.Add($"Strand {strand.Id} is declared twice.");

        var occupied = new Dictionary<StrandId, List<StrategicSlot>>();
        foreach (var slot in topology.Slots)
        {
            if (!declared.ContainsKey(slot.Strand))
                problems.Add($"Room {slot.Id.Value} belongs to strand {slot.Strand}, which the act does not declare.");
            (occupied.TryGetValue(slot.Strand, out var slots) ? slots : occupied[slot.Strand] = []).Add(slot);
        }

        foreach (var (id, slots) in occupied)
        {
            // One room per row: a strand is a single thread, not a set of rooms.
            var rowsHeld = slots.Select(slot => slot.Row).ToList();
            if (rowsHeld.Distinct().Count() != rowsHeld.Count)
                problems.Add($"Strand {id} holds two rooms in one row.");

            var first = rowsHeld.Min();
            var last = rowsHeld.Max();
            if (last - first + 1 != rowsHeld.Distinct().Count())
                problems.Add($"Strand {id} has a gap: it holds rows {first}..{last} but only {rowsHeld.Distinct().Count()} of them.");

            if (declared.TryGetValue(id, out var strand) && (strand.BornRow != first || strand.LastRow != last))
                problems.Add($"Strand {id} is declared as rows {strand.BornRow}..{strand.LastRow} but holds {first}..{last}.");

            // A strand moves sideways only because something was inserted or removed beside it, and one row is
            // one operation — so a route never jumps more than one column, and its own edge is always there.
            foreach (var pair in slots.OrderBy(slot => slot.Row).Zip(slots.OrderBy(slot => slot.Row).Skip(1)))
            {
                if (Math.Abs(pair.Second.Column - pair.First.Column) > 1)
                    problems.Add($"Strand {id} jumps from c{pair.First.Column} to c{pair.Second.Column} at row {pair.Second.Row}.");
                if (!topology.SuccessorsOf(pair.First.Id).Contains(pair.Second.Id))
                    problems.Add($"Strand {id} is not connected from {pair.First.Id.Value} to {pair.Second.Id.Value}.");
            }
        }

        foreach (var strand in topology.Strands)
        {
            if (strand.ParentId is not { } parent)
                continue;
            if (!declared.TryGetValue(parent, out var ancestor))
                problems.Add($"Strand {strand.Id} claims parent {parent}, which the act does not declare.");
            else if (ancestor.BornRow >= strand.BornRow)
                problems.Add($"Strand {strand.Id} (row {strand.BornRow}) claims parent {parent}, born no earlier (row {ancestor.BornRow}).");
        }

        // ── MINIMUM BRANCH LIFETIME, read off the finished graph rather than trusted from the walk. Wherever two
        // routes meet, both of them must already have held a room of their own for `MinBranchLifeRows` rows.
        // This covers the act's last convergence too: the boss absorbs every remaining strand, so the rule only
        // holds there because the generator forbids a split in the act's final rows — which is exactly the kind
        // of second-order promise that needs measuring rather than asserting.
        foreach (var slot in topology.Slots)
        {
            var arriving = topology.PredecessorsOf(slot.Id)
                .Select(id => topology.Slot(id).Strand)
                .Distinct()
                .ToList();
            if (arriving.Count < 2)
                continue;
            foreach (var id in arriving.Where(id => id != slot.Strand))
                if (declared.TryGetValue(id, out var strand) && slot.Row - strand.BornRow < rules.MinBranchLifeRows)
                    problems.Add(
                        $"Strand {id} was born in row {strand.BornRow} and is already absorbed in row {slot.Row}, "
                        + $"short of the {rules.MinBranchLifeRows} rows a branch must live.");

            // One row is one operation, so away from the act's final convergence exactly two routes can meet.
            if (slot.Row < preBossRows && topology.PredecessorsOf(slot.Id).Count > 2)
                problems.Add($"Room {slot.Id.Value} is reached from {topology.PredecessorsOf(slot.Id).Count} rooms; one merge joins two.");
        }

        return problems;
    }
}
