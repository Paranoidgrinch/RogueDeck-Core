using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// A ROUTE'S FLAVOUR FOLLOWS THE ROUTE (map rework S5).
//
// The rule-based generator hands a lane profile to `column % LaneProfiles.Count`, so flavour belongs to the
// screen: a route that shifts sideways changes character for no reason, and two rooms that share a column share
// a "lane" while sharing no route at all. Here the profile belongs to the STRAND, and the tests below are the
// difference between those two sentences — a profile may only change where something happened to the route
// (a fork, a convergence), and never where the drawing moved.
public class StrategicStrandProfileTests
{
    private const int Rows = 23;

    // Four authored flavours, in a deliberate order: the list's neighbours are related, which is what
    // AdjacentProfileWeight means. One kind each keeps the tests about assignment, not about weights.
    private static readonly IReadOnlyList<MapLaneProfile> Lanes =
    [
        Lane("gauntlet", MapNodeKind.Combat),
        Lane("wilds", MapNodeKind.Event),
        Lane("errands", MapNodeKind.Shop),
        Lane("hoard", MapNodeKind.Treasure),
    ];

    private static MapLaneProfile Lane(string name, MapNodeKind kind) =>
        new(name, new Dictionary<MapNodeKind, int> { [kind] = 7 });

    [Fact]
    public void Every_room_knows_which_flavour_its_route_carries()
    {
        var topology = StrategicTopologyGenerator.Generate(seed: 1, Rows);
        var profiles = StrategicStrandProfileAssigner.Assign(seed: 1, topology, Lanes);

        foreach (var slot in topology.Slots)
        {
            var index = profiles.IndexOf(slot.Id);
            Assert.InRange(index, 0, Lanes.Count - 1);
            Assert.Same(Lanes[index], profiles.ProfileOf(slot.Id));
            Assert.Equal(index, profiles.IndexOf(slot.Strand, slot.Row));
        }

        // Every strand is flavoured from its birth row, and a strand is flavoured once unless a merge changed it.
        Assert.Equal(topology.Strands.Count, profiles.Spans.Select(span => span.Strand).Distinct().Count());
        Assert.All(topology.Strands, strand =>
            Assert.Contains(profiles.Spans, span => span.Strand == strand.Id && span.FromRow == strand.BornRow));
    }

    // THE POINT OF THE WHOLE STEP. A profile may begin at exactly two kinds of row: the row the strand was born
    // in, and a row where a second route arrived in its room. Anywhere else — a CONTINUE, a sideways shift
    // because a neighbour split or merged — it inherits, because nothing happened to the route.
    [Fact]
    public void A_flavour_changes_where_routes_meet_and_nowhere_else()
    {
        for (var seed = 1; seed <= 200; seed++)
        {
            var topology = StrategicTopologyGenerator.Generate(seed, Rows);
            var profiles = StrategicStrandProfileAssigner.Assign(seed, topology, Lanes);
            var born = topology.Strands.ToDictionary(strand => strand.Id, strand => strand.BornRow);

            foreach (var span in profiles.Spans)
            {
                if (span.FromRow == born[span.Strand])
                    continue;

                var room = topology.Rows[span.FromRow].Slots.Single(slot => slot.Strand == span.Strand);
                var arriving = topology.PredecessorsOf(room.Id).Select(id => topology.Slot(id).Strand).Distinct();
                Assert.True(arriving.Count() > 1,
                    $"seed {seed}: strand {span.Strand} changed flavour in row {span.FromRow}, where no second "
                    + "route arrived");
            }
        }
    }

    // The same statement read the other way round: a strand that walks from column 2 to column 1 is the same
    // route, so it keeps its flavour — which is precisely what `column % count` could not do.
    [Fact]
    public void A_route_that_moves_sideways_keeps_its_flavour()
    {
        var shifts = 0;
        for (var seed = 1; seed <= 200 && shifts < 50; seed++)
        {
            var topology = StrategicTopologyGenerator.Generate(seed, Rows);
            var profiles = StrategicStrandProfileAssigner.Assign(seed, topology, Lanes);

            foreach (var row in topology.Rows.Skip(1))
            {
                var previous = topology.Rows[row.Index - 1];
                foreach (var slot in row.Slots)
                {
                    var before = previous.Slots.FirstOrDefault(candidate => candidate.Strand == slot.Strand);
                    if (before is null || before.Column == slot.Column)
                        continue;
                    if (topology.PredecessorsOf(slot.Id).Select(id => topology.Slot(id).Strand).Distinct().Count() > 1)
                        continue;   // a convergence is allowed to change it; a sideways step is not

                    shifts++;
                    Assert.Equal(
                        profiles.IndexOf(slot.Strand, previous.Index),
                        profiles.IndexOf(slot.Strand, row.Index));
                }
            }
        }

        Assert.True(shifts >= 50, $"only {shifts} sideways steps to check");
    }

    // …and the falsifiable half: an assignment that agreed with `column % count` everywhere would pass every
    // test above while changing nothing. Over a handful of acts, it must visibly disagree.
    [Fact]
    public void A_rooms_flavour_is_not_its_column()
    {
        var disagreements = 0;
        for (var seed = 1; seed <= 20; seed++)
        {
            var topology = StrategicTopologyGenerator.Generate(seed, Rows);
            var profiles = StrategicStrandProfileAssigner.Assign(seed, topology, Lanes);
            disagreements += topology.Slots.Count(slot => profiles.IndexOf(slot.Id) != slot.Column % Lanes.Count);
        }

        Assert.True(disagreements > 100, $"only {disagreements} rooms disagreed with column % count");
    }

    [Fact]
    public void The_entrances_open_on_flavours_of_their_own()
    {
        var openings = new HashSet<int>();
        for (var seed = 1; seed <= 50; seed++)
        {
            var topology = StrategicTopologyGenerator.Generate(seed, Rows);
            var profiles = StrategicStrandProfileAssigner.Assign(seed, topology, Lanes);
            var entrances = topology.Rows[0].Slots.Select(slot => profiles.IndexOf(slot.Id)).ToList();

            // As many distinct flavours as the act has entrances, since it never opens wider than four rooms.
            Assert.Equal(entrances.Count, entrances.Distinct().Count());
            openings.Add(entrances[0]);
        }

        // The seed decides which flavour is on the left, so "the left lane is always the gauntlet" is not baked in.
        Assert.True(openings.Count > 1, "every act opened on the same flavour on the left");
    }

    [Fact]
    public void A_forks_two_sides_can_be_made_to_differ_or_to_agree()
    {
        var never = new StrategicStrandProfileRules { SameProfileWeight = 0 };
        var always = new StrategicStrandProfileRules { AdjacentProfileWeight = 0, DistantProfileWeight = 0 };
        var splits = 0;

        for (var seed = 1; seed <= 100; seed++)
        {
            var topology = StrategicTopologyGenerator.Generate(seed, Rows);
            var differing = StrategicStrandProfileAssigner.Assign(seed, topology, Lanes, never);
            var agreeing = StrategicStrandProfileAssigner.Assign(seed, topology, Lanes, always);

            foreach (var branch in topology.Strands.Where(strand => strand.ParentId is not null))
            {
                splits++;
                Assert.NotEqual(
                    differing.IndexOf(branch.ParentId!.Value, branch.BornRow),
                    differing.IndexOf(branch.Id, branch.BornRow));
                Assert.Equal(
                    agreeing.IndexOf(branch.ParentId!.Value, branch.BornRow),
                    agreeing.IndexOf(branch.Id, branch.BornRow));
            }
        }

        Assert.True(splits > 100, $"only {splits} forks to check");
    }

    // The shape of the default draw: a new branch usually lands next door to its parent, sometimes further away,
    // and least often on the parent's own flavour. This is the document's conservative v1, stated as a ranking
    // rather than as a distribution, so retuning the weights does not mean rewriting the test.
    [Fact]
    public void A_new_branch_usually_lands_next_door_to_its_parent()
    {
        var same = 0;
        var adjacent = 0;
        var distant = 0;

        for (var seed = 1; seed <= 400; seed++)
        {
            var topology = StrategicTopologyGenerator.Generate(seed, Rows);
            var profiles = StrategicStrandProfileAssigner.Assign(seed, topology, Lanes);

            foreach (var branch in topology.Strands.Where(strand => strand.ParentId is not null))
            {
                var parent = profiles.IndexOf(branch.ParentId!.Value, branch.BornRow);
                var child = profiles.IndexOf(branch.Id, branch.BornRow);
                if (child == parent)
                    same++;
                else if (Math.Abs(child - parent) == 1)
                    adjacent++;
                else
                    distant++;
            }
        }

        Assert.True(adjacent > distant, $"same {same} · adjacent {adjacent} · distant {distant}");
        Assert.True(distant > same, $"same {same} · adjacent {adjacent} · distant {distant}");
    }

    [Fact]
    public void A_convergence_is_decided_by_age_and_can_be_made_to_go_either_way()
    {
        var older = new StrategicStrandProfileRules { YoungerProfileWeight = 0 };
        var younger = new StrategicStrandProfileRules { OlderProfileWeight = 0 };
        var handovers = 0;

        for (var seed = 1; seed <= 100; seed++)
        {
            var topology = StrategicTopologyGenerator.Generate(seed, Rows);
            var kept = StrategicStrandProfileAssigner.Assign(seed, topology, Lanes, older);
            var taken = StrategicStrandProfileAssigner.Assign(seed, topology, Lanes, younger);

            // The surviving strand is the older one, so favouring age absolutely means no flavour ever changes.
            Assert.Equal(0, kept.Inherited);

            foreach (var (room, absorbed) in Convergences(topology))
            {
                handovers++;
                // …and favouring youth absolutely means the swallowed branch's flavour always wins.
                Assert.Equal(taken.IndexOf(absorbed, room.Row - 1), taken.IndexOf(room.Id));
            }
        }

        Assert.True(handovers > 100, $"only {handovers} convergences to check");
    }

    [Fact]
    public void A_long_route_sometimes_takes_on_what_it_swallowed()
    {
        var acts = Enumerable.Range(1, 200)
            .Select(seed => StrategicStrandProfileAssigner.Assign(
                seed, StrategicTopologyGenerator.Generate(seed, Rows), Lanes))
            .ToList();

        // With the defaults a convergence mostly keeps the older character, but not always — the absorbed branch
        // has to be able to leave a mark, or a merge would be invisible in the finished act.
        Assert.Contains(acts, act => act.Inherited > 0);
        Assert.Contains(acts, act => act.Inherited == 0);
    }

    [Fact]
    public void The_boss_rooms_carry_the_route_that_reached_them()
    {
        var topology = StrategicTopologyGenerator.Generate(seed: 3, rows: 25, bossRooms: 3, minWidth: 2, maxWidth: 4,
            new StrategicTopologyRules());
        var profiles = StrategicStrandProfileAssigner.Assign(seed: 3, topology, Lanes);
        var boss = topology.Rows.Where(row => row.Slots[0].IsBoss).ToList();

        Assert.Equal(3, boss.Count);
        // A boss room's content is fixed and nothing downstream reads its flavour, so the act's final
        // convergence draws nothing: the surviving route walks in with the character it had.
        Assert.DoesNotContain(profiles.Spans, span => boss.Any(row => row.Index == span.FromRow));
        var reached = profiles.IndexOf(boss[0].Slots[0].Strand, boss[0].Index - 1);
        Assert.All(boss, row => Assert.Equal(reached, profiles.IndexOf(row.Slots[0].Id)));
    }

    [Fact]
    public void The_same_seed_flavours_the_same_act_the_same_way()
    {
        var topology = StrategicTopologyGenerator.Generate(seed: 11, Rows);

        Assert.Equal(
            StrategicStrandProfileAssigner.Assign(seed: 11, topology, Lanes).Render(),
            StrategicStrandProfileAssigner.Assign(seed: 11, topology, Lanes).Render());

        // A different strand stream reflavours the same shape — which is the reason the streams are separate.
        var flavours = Enumerable.Range(1, 20)
            .Select(seed => StrategicStrandProfileAssigner.Assign(seed, topology, Lanes).Render())
            .Distinct()
            .Count();
        Assert.True(flavours > 10, $"twenty strand seeds produced only {flavours} different flavourings");
    }

    [Fact]
    public void An_act_with_one_authored_profile_has_no_lanes_to_inherit()
    {
        var topology = StrategicTopologyGenerator.Generate(seed: 5, Rows);
        var profiles = StrategicStrandProfileAssigner.Assign(seed: 5, topology, [Lanes[0]]);

        Assert.All(topology.Slots, slot => Assert.Equal(0, profiles.IndexOf(slot.Id)));
        Assert.Equal(topology.Strands.Count, profiles.Spans.Count);
        Assert.Equal(0, profiles.Inherited);
    }

    // With two authored flavours nothing is "distant" — the empty bucket is skipped rather than drawn from.
    [Fact]
    public void Two_authored_profiles_have_no_distant_cousin()
    {
        var two = Lanes.Take(2).ToList();
        for (var seed = 1; seed <= 100; seed++)
        {
            var topology = StrategicTopologyGenerator.Generate(seed, Rows);
            var profiles = StrategicStrandProfileAssigner.Assign(seed, topology, two,
                new StrategicStrandProfileRules { SameProfileWeight = 0 });

            Assert.All(topology.Slots, slot => Assert.InRange(profiles.IndexOf(slot.Id), 0, 1));
            foreach (var branch in topology.Strands.Where(strand => strand.ParentId is not null))
                Assert.NotEqual(
                    profiles.IndexOf(branch.ParentId!.Value, branch.BornRow),
                    profiles.IndexOf(branch.Id, branch.BornRow));
        }
    }

    [Fact]
    public void An_act_needs_something_to_flavour_its_routes_with()
    {
        var topology = StrategicTopologyGenerator.Generate(seed: 1, Rows);

        Assert.Throws<ArgumentException>(() => StrategicStrandProfileAssigner.Assign(1, topology, []));
        Assert.Throws<ArgumentException>(() => StrategicStrandProfileAssigner.Assign(
            1, topology, [new MapLaneProfile("hollow", new Dictionary<MapNodeKind, int>())]));
        Assert.Throws<ArgumentNullException>(() => StrategicStrandProfileAssigner.Assign(1, topology, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            StrategicStrandProfileAssigner.Assign(1, topology, Lanes).IndexOf(new StrandId(99), 0));
    }

    // Every room two routes arrive in, with the strand that was swallowed there.
    private static List<(StrategicSlot Room, StrandId Absorbed)> Convergences(StrategicTopology topology)
    {
        var found = new List<(StrategicSlot, StrandId)>();
        foreach (var slot in topology.Slots.Where(slot => !slot.IsBoss))
        {
            var arriving = topology.PredecessorsOf(slot.Id).Select(id => topology.Slot(id).Strand).Distinct().ToList();
            if (arriving.Count > 1)
                found.Add((slot, arriving.Single(strand => strand != slot.Strand)));
        }
        return found;
    }
}
