using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// "Once per Act, ignore path connections to reach a legal node in the next row." These maps are layered, but
// nothing records the rows — the layout is presentational and the generators do not even emit it — so the row
// has to be read back out of the edges as distance from the start.
public class UnrestrictedStepTests
{
    // a → b → d
    // a → c → e   (b and c are one row; d and e the next)
    private static RunMap Forked() =>
        new RunMapBuilder()
            .AddNode(new NodeId("a"), StandardRunIds.EventNode, new EventRef(new EventId("e")))
            .AddNode(new NodeId("b"), StandardRunIds.EventNode, new EventRef(new EventId("e")))
            .AddNode(new NodeId("c"), StandardRunIds.EventNode, new EventRef(new EventId("e")))
            .AddNode(new NodeId("d"), StandardRunIds.EventNode, new EventRef(new EventId("e")))
            .AddNode(new NodeId("e"), StandardRunIds.EventNode, new EventRef(new EventId("e")))
            .Connect(new NodeId("a"), new NodeId("b"))
            .Connect(new NodeId("a"), new NodeId("c"))
            .Connect(new NodeId("b"), new NodeId("d"))
            .Connect(new NodeId("c"), new NodeId("e"))
            .Build();

    [Fact]
    public void The_rows_are_read_out_of_the_edges()
    {
        var depths = Forked().Depths();

        Assert.Equal(0, depths[new NodeId("a")]);
        Assert.Equal(1, depths[new NodeId("b")]);
        Assert.Equal(1, depths[new NodeId("c")]);
        Assert.Equal(2, depths[new NodeId("d")]);
        Assert.Equal(2, depths[new NodeId("e")]);
    }

    [Fact]
    public void Without_a_step_only_the_paths_are_walkable()
    {
        var run = NewRun();
        run.AdvanceToNode(new NodeId("b"));

        Assert.Equal(["d"], run.CurrentReachableNodes().Select(n => n.Id.Value));
    }

    // Standing on b, the walker could only reach d. With a step in hand, e — the other node in that row —
    // opens up too.
    [Fact]
    public void A_step_opens_the_whole_next_row()
    {
        var run = NewRun();
        run.AdvanceToNode(new NodeId("b"));
        run.GrantUnrestrictedStep();

        Assert.Equal(["d", "e"], run.CurrentReachableNodes().Select(n => n.Id.Value).Order());
    }

    // Walking an ordinary edge keeps the step for later — which is what "once per Act" has to mean, or the
    // charge would evaporate on the first fork the player walks normally.
    [Fact]
    public void Walking_a_path_does_not_spend_the_step()
    {
        var run = NewRun();
        run.GrantUnrestrictedStep();
        run.AdvanceToNode(new NodeId("b"));
        run.AdvanceToNode(new NodeId("d"));

        Assert.Equal(1, run.UnrestrictedSteps);
    }

    [Fact]
    public void Crossing_off_the_paths_spends_it()
    {
        var run = NewRun();
        run.GrantUnrestrictedStep();
        run.AdvanceToNode(new NodeId("b"));
        run.AdvanceToNode(new NodeId("e")); // no edge b → e

        Assert.Equal(0, run.UnrestrictedSteps);
        // Spent: e is a leaf, and with no charge left nothing widens the walk beyond its (empty) successors.
        Assert.Empty(run.CurrentReachableNodes());
    }

    [Fact]
    public void A_visited_node_is_not_reopened_by_a_step()
    {
        var run = NewRun();
        run.AdvanceToNode(new NodeId("c"));
        run.AdvanceToNode(new NodeId("e"));
        run.GrantUnrestrictedStep();

        // The row after e is empty, and e's own row holds nothing unvisited.
        Assert.Empty(run.CurrentReachableNodes());
    }

    [Fact]
    public void A_step_survives_a_save()
    {
        var run = NewRun();
        run.GrantUnrestrictedStep(2);
        run.AdvanceToNode(new NodeId("b"));

        var restored = RunState.Restore(RunSaveJson.FromJson(RunSaveJson.ToJson(run.Snapshot())), run.Map, null);

        Assert.Equal(2, restored.UnrestrictedSteps);
    }

    [Fact]
    public void A_save_without_one_writes_nothing()
    {
        var json = RunSaveJson.ToJson(NewRun().Snapshot());

        Assert.DoesNotContain("UnrestrictedSteps", json, StringComparison.Ordinal);
    }

    private static RunState NewRun() =>
        new(new RunId("run"), new HealthState(30, 40), Forked());
}
