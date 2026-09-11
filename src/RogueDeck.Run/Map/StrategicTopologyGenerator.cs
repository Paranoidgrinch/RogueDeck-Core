namespace RogueDeck.Run;

// THE ACT'S SHAPE, DRAWN AS A WALK INSTEAD OF AS A LIST OF DICE ROLLS.
//
// The rule-based generator draws each row's width independently and then invents edges that connect the two
// widths. Two things follow, and both are visible in a finished BnB act. The width jumps (4, 2, 3, 2, …) because
// nothing remembers the last row, and then FREEZES for twenty rows because the guarantee rows all borrow the
// last branch row's width — one dice roll decides two thirds of an act. And the edges are a repair job on a
// mismatch rather than a description of anything, which is how crossings get in: 30 crossing pairs in the 1 155
// edges of the fifteen maps the BnB golden samples.
//
// Here a row is not a width, it is an OPERATION on the set of live routes:
//
//   CONTINUE   every strand walks one row further            width unchanged
//   SPLIT      one strand becomes two, the child beside it    width + 1
//   MERGE      two NEIGHBOURING strands become one           width − 1
//
// The edges are produced by the operation, not fitted to it afterwards, so the graph says what happened. Three
// guarantees fall out by construction rather than by repair:
//
//   • Coherent width. Consecutive rows differ by at most one, so an act breathes 2 → 3 → 4 → 3 in stretches.
//   • No crossings. A child is inserted directly beside its parent and only neighbours merge, so left-to-right
//     order is preserved at every row and no two edges out of a row run opposite ways.
//   • Branches that matter. A split cannot be undone for `MinBranchLifeRows` rows, and no split happens so late
//     that the boss would undo it early, so picking a side always means three rooms of your own.
//
// Nothing here can fail: CONTINUE is always legal, so the walk never needs a retry and a topology is always
// produced. Whether the RESULT is acceptable is the validator's question, and from S10 on the repair's.
public static class StrategicTopologyGenerator
{
    // The act's shape for one seed.
    //
    // `rows` counts EVERY row the act has, the boss rooms included — the act's length is a structural invariant
    // of this generator (no guarantee rows are inserted later, nothing is subtracted for them), so the number
    // authored is the number of rooms a route walks. That is deliberately unlike the rule-based
    // `MapGenerationSpec.Rows`, which counts only the branching backbone and then grows.
    public static StrategicTopology Generate(
        int seed, int rows, int bossRooms, int minWidth, int maxWidth, StrategicTopologyRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
        ArgumentOutOfRangeException.ThrowIfNegative(bossRooms);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bossRooms, rows);
        ArgumentOutOfRangeException.ThrowIfLessThan(minWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxWidth, minWidth);

        // AN ACT TOO SHORT TO HONOUR ITS OWN BRANCH RULE IS REFUSED, not quietly bent. The act's OPENING WIDTH is
        // already a set of branches — that is what several entry rooms are — and the boss absorbs all of them
        // after `rows - bossRooms` rows. So a two-row act that is two rooms wide and promises three-row branches
        // is asking for two incompatible things, and the honest answer is to say which. (A gauntlet act, nothing
        // but boss rooms, has no branches and is exempt; so is a minimum width of 1, which can open as a single
        // corridor.) S7 generalizes this into a spec validator that catches impossible combinations up front.
        var rowsBeforeBoss = rows - bossRooms;
        if (minWidth > 1 && rowsBeforeBoss > 0 && rowsBeforeBoss < rules.MinBranchLifeRows)
            throw new ArgumentException(
                $"An act of {rows} rows ending in {bossRooms} boss room(s) has {rowsBeforeBoss} row(s) before the "
                + $"boss, too few for the {rules.MinBranchLifeRows} rows a branch must live when the act is at "
                + $"least {minWidth} rooms wide.",
                nameof(rows));

        var rng = new MapGenRandom(seed);
        var preBossRows = rowsBeforeBoss;
        var lives = new List<Strand>();
        var active = new List<Strand>();
        var built = new List<StrategicRow>();
        var edges = new List<MapEdge>();

        if (preBossRows > 0)
        {
            // How wide the act opens: its entry count, and the only width nothing walked into.
            var width = minWidth + rng.Next(maxWidth - minWidth + 1);
            for (var column = 0; column < width; column++)
                active.Add(Born(lives, parent: null, row: 0));
            built.Add(RowOf(0, active, boss: false));

            for (var row = 1; row < preBossRows; row++)
            {
                Advance(row, active, lives, edges, built, minWidth, maxWidth, preBossRows, rules, rng);
                built.Add(RowOf(row, active, boss: false));
            }
        }

        // THE BOSS ROOMS, and the act's final convergence. Every room of the last pre-boss row leads into the
        // first boss room — which is a merge of everything left, so the strand that continues is the one a merge
        // would have kept: the oldest, leftmost on a tie. Nothing reads a boss room's strand for its ROLE (that
        // is fixed), it is there so a strand's life is continuous from row 0 to the end of the act.
        if (bossRooms > 0)
        {
            var continuing = active.Count > 0 ? Oldest(active) : Born(lives, parent: null, row: preBossRows);
            for (var index = 0; index < bossRooms; index++)
            {
                var row = preBossRows + index;
                if (index > 0)
                    edges.Add(new MapEdge(new NodeId(MapWiring.Id(row - 1, 0)), new NodeId(MapWiring.Id(row, 0))));
                else if (built.Count > 0)
                    foreach (var slot in built[^1].Slots)
                        edges.Add(new MapEdge(slot.Id, new NodeId(MapWiring.Id(row, 0))));

                continuing.LastRow = row;
                built.Add(RowOf(row, [continuing], boss: true));
            }
        }

        return new StrategicTopology(
            seed,
            built,
            edges,
            lives
                .OrderBy(strand => strand.Id.Value)
                .Select(strand => new StrategicStrand
                {
                    Id = strand.Id,
                    ParentId = strand.ParentId,
                    BornRow = strand.BornRow,
                    LastRow = strand.LastRow,
                })
                .ToList());
    }

    // The same, on the defaults the BnB acts use: one boss room, two to four rooms wide.
    public static StrategicTopology Generate(int seed, int rows) =>
        Generate(seed, rows, bossRooms: 1, minWidth: 2, maxWidth: 4, new StrategicTopologyRules());

    // One row of the walk: choose the operation, apply it to the live strands, and emit the edges it means.
    private static void Advance(
        int row,
        List<Strand> active,
        List<Strand> lives,
        List<MapEdge> edges,
        List<StrategicRow> built,
        int minWidth,
        int maxWidth,
        int preBossRows,
        StrategicTopologyRules rules,
        MapGenRandom rng)
    {
        var previous = built[^1];

        // A SPLIT is only legal while its child can still live out its minimum: the act's own end absorbs every
        // strand into the boss, and a branch the boss closes after one row is exactly the detour the rule
        // forbids. So the last `MinBranchLifeRows` rows of an act never fork.
        var canSplit = active.Count < maxWidth
            && row + Math.Max(0, rules.MinBranchLifeRows) <= preBossRows;

        // A MERGE is only legal between NEIGHBOURS (that is where planarity comes from) and only once both
        // strands have lived their minimum. `row - BornRow` is how many rows the strand has held a room of its
        // own before this one.
        var pairs = new List<int>();
        if (active.Count > minWidth)
            for (var index = 0; index + 1 < active.Count; index++)
                if (row - active[index].BornRow >= rules.MinBranchLifeRows
                    && row - active[index + 1].BornRow >= rules.MinBranchLifeRows)
                    pairs.Add(index);

        switch (Choose(canSplit, pairs.Count > 0, rules, rng))
        {
            case Operation.Split:
                {
                    var index = rng.Next(active.Count);
                    active.Insert(index + 1, Born(lives, active[index].Id, row));
                    for (var column = 0; column < previous.Width; column++)
                    {
                        var from = previous.Slots[column].Id;
                        var landing = column <= index ? column : column + 1;
                        edges.Add(new MapEdge(from, new NodeId(MapWiring.Id(row, landing))));
                        if (column == index)
                            edges.Add(new MapEdge(from, new NodeId(MapWiring.Id(row, landing + 1))));
                    }
                    break;
                }

            case Operation.Merge:
                {
                    var index = pairs[rng.Next(pairs.Count)];
                    // Identity is the OLDER strand's — a route keeps its name through a convergence, which is what
                    // makes it traceable for a whole act. Which lane FLAVOUR the merged strand carries is a separate
                    // question with its own weighting, and it belongs to S5; this file only decides who survives.
                    active[index] = Oldest([active[index], active[index + 1]]);
                    active.RemoveAt(index + 1);
                    for (var column = 0; column < previous.Width; column++)
                    {
                        var landing = column <= index ? column : column - 1;
                        edges.Add(new MapEdge(previous.Slots[column].Id, new NodeId(MapWiring.Id(row, landing))));
                    }
                    break;
                }

            default:
                for (var column = 0; column < previous.Width; column++)
                    edges.Add(new MapEdge(previous.Slots[column].Id, new NodeId(MapWiring.Id(row, column))));
                break;
        }

        foreach (var strand in active)
            strand.LastRow = row;
    }

    // The weighted draw over the operations that are legal here. CONTINUE always is, so there is always an
    // answer; a configuration that weights everything at zero continues rather than throwing.
    private static Operation Choose(bool canSplit, bool canMerge, StrategicTopologyRules rules, MapGenRandom rng)
    {
        var options = new List<(Operation Operation, int Weight)>
        {
            (Operation.Continue, Math.Max(0, rules.ContinueWeight)),
        };
        if (canSplit)
            options.Add((Operation.Split, Math.Max(0, rules.SplitWeight)));
        if (canMerge)
            options.Add((Operation.Merge, Math.Max(0, rules.MergeWeight)));

        var total = options.Sum(option => option.Weight);
        if (total <= 0)
            return Operation.Continue;

        var roll = rng.Next(total);
        foreach (var option in options)
        {
            roll -= option.Weight;
            if (roll < 0)
                return option.Operation;
        }
        return Operation.Continue;
    }

    // The oldest strand, the leftmost winning a tie. Deterministic and readable: a player tracing a route sees
    // the older thread swallow the younger, and an RNG draw here would spend topology entropy on a question
    // nobody can see the answer to.
    private static Strand Oldest(IReadOnlyList<Strand> candidates)
    {
        var oldest = candidates[0];
        foreach (var candidate in candidates.Skip(1))
            if (candidate.BornRow < oldest.BornRow)
                oldest = candidate;
        return oldest;
    }

    private static Strand Born(List<Strand> lives, StrandId? parent, int row)
    {
        var strand = new Strand
        {
            Id = new StrandId(lives.Count),
            ParentId = parent,
            BornRow = row,
            LastRow = row,
        };
        lives.Add(strand);
        return strand;
    }

    private static StrategicRow RowOf(int index, IReadOnlyList<Strand> active, bool boss) => new()
    {
        Index = index,
        Slots = active
            .Select((strand, column) => new StrategicSlot
            {
                Id = new NodeId(MapWiring.Id(index, column)),
                Row = index,
                Column = column,
                Strand = strand.Id,
                IsBoss = boss,
            })
            .ToList(),
    };

    private enum Operation
    {
        Continue,
        Split,
        Merge,
    }

    // The walk's own bookkeeping: a strand while it is still alive. Mutable and private on purpose — the
    // topology hands out the immutable StrategicStrand record instead.
    private sealed class Strand
    {
        public required StrandId Id { get; init; }
        public StrandId? ParentId { get; init; }
        public required int BornRow { get; init; }
        public int LastRow { get; set; }
    }
}
