using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// THE ACT'S SHAPE, HELD TO ITS PROMISES (map rework S4).
//
// The strategic generator settles topology before any room exists, and it claims four things the rule-based
// generator does not: the act is exactly as long as it was authored, the width moves coherently, no two edges
// cross, and a branch lives long enough that choosing a side is a commitment. Each of those is made "by
// construction" — which is an argument about code, and code gets changed — so each one is measured here, and
// measured again over 10 000 seeds per act length in StrategicTopologyFuzzTests.
public class StrategicTopologyTests
{
    private const int Rows = 23;

    [Fact]
    public void An_act_is_exactly_as_long_as_it_was_authored()
    {
        var topology = StrategicTopologyGenerator.Generate(seed: 1, Rows);

        // No guarantee rows are inserted and nothing is subtracted for them: the number authored is the number
        // of rooms a route walks. This is the invariant the whole rework rests on, because every budget in S6 is
        // stated against an act of a known length.
        Assert.Equal(Rows, topology.Rows.Count);
        Assert.Equal(Rows, topology.Widths.Count);
        Assert.Empty(StrategicTopologyValidator.Validate(topology, Rows));
    }

    [Fact]
    public void Every_route_ends_in_the_one_boss_room()
    {
        var topology = StrategicTopologyGenerator.Generate(seed: 7, Rows);
        var boss = topology.Rows[^1];
        var lastBefore = topology.Rows[^2];

        Assert.Equal(1, boss.Width);
        Assert.True(boss.Slots[0].IsBoss);
        Assert.DoesNotContain(topology.Rows.SkipLast(1).SelectMany(row => row.Slots), slot => slot.IsBoss);

        // The act's final convergence: every room of the last ordinary row leads into the boss, and nothing else
        // does, so there is no route that misses it and none that reaches it early.
        Assert.Equal(lastBefore.Width, topology.PredecessorsOf(boss.Slots[0].Id).Count);
        foreach (var slot in lastBefore.Slots)
            Assert.Contains(boss.Slots[0].Id, topology.SuccessorsOf(slot.Id));
    }

    [Fact]
    public void The_boss_rooms_continue_the_oldest_surviving_strand()
    {
        var topology = StrategicTopologyGenerator.Generate(seed: 4242, rows: 25, bossRooms: 3, 2, 4, new());
        var strands = topology.Strands.ToDictionary(strand => strand.Id);
        var lastOrdinary = topology.Rows[^4];

        // A convergence keeps the older route's name — the boss is that rule applied to everything left — so a
        // strand really is traceable from row 0 to the end of the act.
        var expected = lastOrdinary.Slots.Select(slot => strands[slot.Strand]).MinBy(strand => strand.BornRow)!;
        foreach (var boss in topology.Rows.TakeLast(3))
        {
            Assert.Equal(1, boss.Width);
            Assert.Equal(expected.Id, boss.Slots[0].Strand);
        }
        Assert.Equal(24, strands[expected.Id].LastRow);
        Assert.Empty(StrategicTopologyValidator.Validate(topology, 25, 3, 2, 4, new()));
    }

    [Fact]
    public void The_same_seed_lays_out_the_same_act_and_a_different_one_does_not()
    {
        Assert.Equal(
            StrategicTopologyGenerator.Generate(seed: 20260911, Rows).Render(),
            StrategicTopologyGenerator.Generate(seed: 20260911, Rows).Render());
        Assert.NotEqual(
            StrategicTopologyGenerator.Generate(seed: 20260911, Rows).Render(),
            StrategicTopologyGenerator.Generate(seed: 20260912, Rows).Render());
    }

    [Fact]
    public void A_row_is_one_operation_so_the_width_moves_by_at_most_one()
    {
        // The defect this replaces: the rule-based generator draws each row's width on its own (4, 2, 3, 2 …)
        // and then freezes at whatever the last branch row rolled for the rest of the act. Here the width is a
        // walk, so an act breathes in stretches.
        foreach (var seed in Enumerable.Range(1, 200))
        {
            var widths = StrategicTopologyGenerator.Generate(seed, Rows).Widths;
            for (var row = 1; row < widths.Count - 1; row++)
                Assert.True(Math.Abs(widths[row] - widths[row - 1]) <= 1,
                    $"seed {seed} row {row}: width {widths[row - 1]} → {widths[row]}");
            Assert.All(widths.SkipLast(1), width => Assert.InRange(width, 2, 4));
        }
    }

    [Fact]
    public void No_two_edges_cross()
    {
        // Measured by the same rule MapDiagnostics applies to a finished map, so the guarantee is checked on both
        // sides of the pipeline. v0.0.0, for comparison, emits 30 crossing pairs in the 1 155 edges of the
        // fifteen maps the BnB golden samples.
        foreach (var seed in Enumerable.Range(1, 500))
            Assert.Equal(0, StrategicTopologyGenerator.Generate(seed, Rows).Crossings);
    }

    [Fact]
    public void A_split_keeps_its_parent_and_names_exactly_one_new_strand()
    {
        var topology = StrategicTopologyGenerator.Generate(seed: 1, Rows);
        var strands = topology.Strands.ToDictionary(strand => strand.Id);
        var forks = 0;

        foreach (var parent in topology.Slots.Where(slot => topology.SuccessorsOf(slot.Id).Count > 1))
        {
            var children = topology.SuccessorsOf(parent.Id).Select(topology.Slot).OrderBy(slot => slot.Column).ToList();
            Assert.Equal(2, children.Count);

            // One child IS the parent strand walking on; the other is born here, out of it.
            var continuing = Assert.Single(children, child => child.Strand == parent.Strand);
            var born = Assert.Single(children, child => child.Strand != parent.Strand);
            Assert.Equal(parent.Strand, strands[born.Strand].ParentId);
            Assert.Equal(born.Row, strands[born.Strand].BornRow);

            // And it is inserted BESIDE its parent. That is where planarity comes from: a child never has to
            // reach across a stranger to be drawn.
            Assert.Equal(1, Math.Abs(born.Column - continuing.Column));
            forks++;
        }

        Assert.Equal(topology.Forks, forks);
        Assert.True(forks > 0, "a 23-row act with split weight should fork at least once");
    }

    [Fact]
    public void A_merge_keeps_the_older_route_and_only_ever_joins_neighbours()
    {
        var merges = 0;
        foreach (var seed in Enumerable.Range(1, 50))
        {
            var topology = StrategicTopologyGenerator.Generate(seed, Rows);
            var strands = topology.Strands.ToDictionary(strand => strand.Id);

            foreach (var slot in topology.Slots.Where(slot => !slot.IsBoss))
            {
                var arriving = topology.PredecessorsOf(slot.Id).Select(topology.Slot).ToList();
                if (arriving.Count < 2)
                    continue;

                Assert.Equal(2, arriving.Count);
                Assert.Equal(1, Math.Abs(arriving[0].Column - arriving[1].Column));

                var older = arriving.MinBy(room => strands[room.Strand].BornRow)!;
                Assert.Equal(older.Strand, slot.Strand);
                merges++;
            }
        }
        Assert.True(merges > 20, $"50 acts should hold plenty of merges, found {merges}");
    }

    [Fact]
    public void A_branch_lives_its_minimum_before_anything_absorbs_it()
    {
        // The player-visible promise: picking a side is a commitment, not a one-room detour. Read off the
        // finished graph — every room two routes arrive in, including the boss.
        var shortest = int.MaxValue;
        foreach (var seed in Enumerable.Range(1, 500))
        {
            var topology = StrategicTopologyGenerator.Generate(seed, Rows);
            var strands = topology.Strands.ToDictionary(strand => strand.Id);

            foreach (var slot in topology.Slots.Where(slot => topology.PredecessorsOf(slot.Id).Count > 1))
                foreach (var arriving in topology.PredecessorsOf(slot.Id)
                    .Select(id => topology.Slot(id).Strand)
                    .Distinct()
                    .Where(strand => strand != slot.Strand))
                    shortest = Math.Min(shortest, slot.Row - strands[arriving].BornRow);
        }
        Assert.Equal(3, shortest);
    }

    [Fact]
    public void A_branch_may_close_at_once_when_the_minimum_is_switched_off()
    {
        // The inverse, so the test above is known to be measuring the rule and not an accident of the weights:
        // with no minimum lifetime, one-row detours appear — and the shape is still a sound act.
        var rules = new StrategicTopologyRules { MinBranchLifeRows = 0, ContinueWeight = 1 };
        var oneRowBranches = 0;

        foreach (var seed in Enumerable.Range(1, 200))
        {
            var topology = StrategicTopologyGenerator.Generate(seed, Rows, 1, 2, 4, rules);
            Assert.Empty(StrategicTopologyValidator.Validate(topology, Rows, 1, 2, 4, rules));
            oneRowBranches += topology.Strands.Count(strand => strand.LifeRows == 1);
        }
        Assert.True(oneRowBranches > 0, "without a minimum lifetime some branch should collapse immediately");
    }

    [Fact]
    public void A_strand_keeps_its_identity_when_it_moves_sideways()
    {
        // A route's name is not its column. This is the whole reason strands exist: the rule-based generator's
        // lane flavour is `column % LaneProfiles.Count`, so a route that shifts one column over silently becomes
        // a different kind of route.
        var shifts = 0;
        foreach (var seed in Enumerable.Range(1, 100))
        {
            var topology = StrategicTopologyGenerator.Generate(seed, Rows);
            foreach (var group in topology.Slots.GroupBy(slot => slot.Strand))
            {
                var walked = group.OrderBy(slot => slot.Row).ToList();
                foreach (var (before, after) in walked.Zip(walked.Skip(1)))
                {
                    Assert.Equal(before.Row + 1, after.Row);
                    Assert.Contains(after.Id, topology.SuccessorsOf(before.Id));
                    if (before.Column != after.Column)
                        shifts++;
                }
            }
        }
        Assert.True(shifts > 0, "100 acts should contain a strand that shifts column");
    }

    [Fact]
    public void An_act_with_no_room_to_branch_is_parallel_corridors()
    {
        // Degenerate but legal, and worth pinning: when MinWidth equals MaxWidth neither a split nor a merge can
        // ever be legal, so the walk continues every row. No exception, no stuck state — the shape is simply
        // as dull as it was configured to be.
        var topology = StrategicTopologyGenerator.Generate(seed: 9, Rows, 1, 2, 2, new());

        Assert.All(topology.Widths.SkipLast(1), width => Assert.Equal(2, width));
        Assert.Equal(0, topology.Forks);
        Assert.Equal(2, topology.Strands.Count);
        Assert.Equal(1, topology.Merges);   // only the boss
        Assert.Empty(StrategicTopologyValidator.Validate(topology, Rows, 1, 2, 2, new()));
    }

    [Fact]
    public void A_minimum_width_of_one_lets_an_act_funnel()
    {
        // Width 1 mid-act is the rule-based generator's gate row, and the strategic topology can still express
        // it — a funnel is just a merge that ran out of neighbours.
        var narrowed = 0;
        foreach (var seed in Enumerable.Range(1, 100))
        {
            var topology = StrategicTopologyGenerator.Generate(seed, Rows, 1, 1, 3, new());
            Assert.Empty(StrategicTopologyValidator.Validate(topology, Rows, 1, 1, 3, new()));
            narrowed += topology.Widths.SkipLast(1).Count(width => width == 1);
        }
        Assert.True(narrowed > 0, "a minimum width of 1 should actually be reached");
    }

    [Fact]
    public void A_gauntlet_act_is_nothing_but_its_boss_rooms()
    {
        // What BnB's Act V is: three gods back to back, no rooms between them. The topology walk has no pre-boss
        // rows to take at all, which must be a shape and not an edge case.
        var topology = StrategicTopologyGenerator.Generate(seed: 3, rows: 3, bossRooms: 3, 2, 4, new());

        Assert.Equal([1, 1, 1], topology.Widths);
        Assert.Equal(3, topology.NodeCount);
        Assert.Equal(2, topology.Edges.Count);
        Assert.Single(topology.Strands);
        Assert.All(topology.Slots, slot => Assert.True(slot.IsBoss));
        Assert.Empty(StrategicTopologyValidator.Validate(topology, 3, 3, 2, 4, new()));
    }

    [Fact]
    public void A_shape_that_cannot_be_an_act_is_refused()
    {
        var rules = new StrategicTopologyRules();
        Assert.Throws<ArgumentOutOfRangeException>(() => StrategicTopologyGenerator.Generate(1, 0, 1, 2, 4, rules));
        Assert.Throws<ArgumentOutOfRangeException>(() => StrategicTopologyGenerator.Generate(1, 10, -1, 2, 4, rules));
        Assert.Throws<ArgumentOutOfRangeException>(() => StrategicTopologyGenerator.Generate(1, 10, 11, 2, 4, rules));
        Assert.Throws<ArgumentOutOfRangeException>(() => StrategicTopologyGenerator.Generate(1, 10, 1, 0, 4, rules));
        Assert.Throws<ArgumentOutOfRangeException>(() => StrategicTopologyGenerator.Generate(1, 10, 1, 4, 3, rules));
        Assert.Throws<ArgumentNullException>(() => StrategicTopologyGenerator.Generate(1, 10, 1, 2, 4, null!));
    }

    [Fact]
    public void The_validator_reports_a_shape_that_was_tampered_with()
    {
        // The validator has to be able to FAIL, or the fuzz test's clean run proves nothing. Three hand-broken
        // topologies, one per class of promise.
        var sound = StrategicTopologyGenerator.Generate(seed: 1, Rows);

        var shortened = new StrategicTopology(1, sound.Rows.SkipLast(1).ToList(), sound.Edges, sound.Strands);
        Assert.Contains("rows", string.Join(" ", StrategicTopologyValidator.Validate(shortened, Rows)));

        var crossed = Crossed(sound);
        Assert.Contains("cross", string.Join(" ", StrategicTopologyValidator.Validate(crossed, Rows)));

        var orphaned = new StrategicTopology(1, sound.Rows, sound.Edges.Where(edge => edge.To != sound.Rows[1].Slots[0].Id).ToList(), sound.Strands);
        Assert.Contains("cannot be reached", string.Join(" ", StrategicTopologyValidator.Validate(orphaned, Rows)));
    }

    // The same act with one row's edges swapped end for end, which is exactly what a crossing is.
    private static StrategicTopology Crossed(StrategicTopology sound)
    {
        var row = sound.Rows.First(candidate => candidate.Width > 1
            && candidate.Slots.All(slot => sound.SuccessorsOf(slot.Id).Count == 1));
        var left = row.Slots[0];
        var right = row.Slots[1];
        var edges = sound.Edges
            .Select(edge =>
                edge.From == left.Id ? new MapEdge(left.Id, sound.SuccessorsOf(right.Id)[0])
                : edge.From == right.Id ? new MapEdge(right.Id, sound.SuccessorsOf(left.Id)[0])
                : edge)
            .ToList();
        return new StrategicTopology(sound.Seed, sound.Rows, edges, sound.Strands);
    }
}
