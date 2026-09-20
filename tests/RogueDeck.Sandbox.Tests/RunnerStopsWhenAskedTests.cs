using RogueDeck.Bot;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;

namespace RogueDeck.Sandbox.Tests;

// ── THE RUN THAT WAS ASKED TO STOP (T0) ──────────────────────────────────────────────────────────────────
// A breeding question is "did it clear act N?", and everything the runner does after that answer is paid for
// and never read — up to 4571 s of tail on the run that reached act IV. `--stop-after-act N` ends the walk at
// the gates of act N+1.
//
// ⚠⚠ THE WHOLE CLAIM IS THAT NOTHING ELSE MOVES. What a bounded run reports for acts 1..N has to be what the
// same seed's unbounded run reports for acts 1..N, or the bound is not a bound but a different experiment.
// These tests hold the two places that could break it: a room of the act nobody asked about being written
// down anyway, and the walk answering one more question on its way out — the answer that would move the run
// and leave the two seats in different states. (The end-to-end half of the gate is four immortal seeds of the
// shipped game played both ways: identical act lines, identical per-act receipts, and the bounded run's log
// byte-equal to the unbounded one up to the line where it stops.)
public class RunnerStopsWhenAskedTests
{
    [Fact]
    public void A_runner_asked_for_one_act_stops_at_the_gates_of_the_next_one()
    {
        var mind = Mind(stopAfterAct: 1);
        var run = TwoActs();

        run.AdvanceToNode(new NodeId("one"));
        mind.Observe(run, null);

        run.BeginNextAct();
        Assert.Throws<BotStopException>(() => mind.Observe(run, null));
        Assert.True(mind.Stopped);
        Assert.True(mind.AskedToStop);
    }

    // ⚠ THE ROOM IT NEVER WALKED MUST NOT BE IN THE TALLY. The act tally and `rooms=` are what a bounded run
    // is compared on, and a single room of act N+1 in them is the difference between "the same run, cut
    // short" and "a different measurement".
    [Fact]
    public void The_act_it_was_not_asked_about_leaves_no_room_in_the_tally()
    {
        var mind = Mind(stopAfterAct: 1);
        var run = TwoActs();

        run.AdvanceToNode(new NodeId("one"));
        mind.Observe(run, null);
        run.BeginNextAct();
        run.AdvanceToNode(new NodeId("two"));
        Assert.Throws<BotStopException>(() => mind.Observe(run, null));

        Assert.Equal(["1:combat"], mind.Rooms);
    }

    // …and the same walk, unasked, does what it always did. The flag is off by default, and the golden set is
    // recorded with it off.
    [Fact]
    public void A_runner_nobody_asked_walks_into_the_next_act_as_it_always_did()
    {
        var mind = Mind(stopAfterAct: 0);
        var run = TwoActs();

        run.AdvanceToNode(new NodeId("one"));
        mind.Observe(run, null);
        run.BeginNextAct();
        run.AdvanceToNode(new NodeId("two"));
        mind.Observe(run, null);

        Assert.False(mind.Stopped);
        Assert.False(mind.AskedToStop);
        Assert.Equal(["1:combat", "2:combat"], mind.Rooms);
    }

    // ⚠⚠ THE ACT BELOW WAS CLEARED, and the report has to say so. An act counts as cleared because the run is
    // standing in the NEXT one (BotResult.ClearedActs), so a walk that stops at those gates without moving
    // `Acts` up would report the very thing it was asked about as a failure.
    [Fact]
    public void The_act_it_was_asked_about_is_reported_as_cleared()
    {
        var mind = Mind(stopAfterAct: 1);
        var run = TwoActs();

        run.AdvanceToNode(new NodeId("one"));
        mind.Observe(run, null);
        run.BeginNextAct();
        Assert.Throws<BotStopException>(() => mind.Observe(run, null));

        var result = mind.Finish(run, error: null, complete: false);
        Assert.Equal(2, result.Acts);
        Assert.Equal(1, result.ClearedActs);
        Assert.True(result.AskedToStop);
        // An incomplete run is normally a fault worth a batch's attention; one that was asked to stop is not.
        Assert.True(result.Clean);
        Assert.Contains("calledOff=asked", BotReport.Clearance(result), StringComparison.Ordinal);
    }

    // Deeper than it was asked about is the only thing that stops it — inside the act, it walks on.
    [Fact]
    public void Being_in_the_act_it_was_asked_about_stops_nothing()
    {
        var mind = Mind(stopAfterAct: 2);
        var run = TwoActs();

        run.AdvanceToNode(new NodeId("one"));
        mind.Observe(run, null);
        run.BeginNextAct();
        run.AdvanceToNode(new NodeId("two"));
        mind.Observe(run, null);

        Assert.False(mind.Stopped);
        Assert.Equal(["1:combat", "2:combat"], mind.Rooms);
    }

    private static BotMind Mind(int stopAfterAct) =>
        new(new RunPlayback(() => { }, new InMemoryMetaStore()),
            new BotOptions { Seed = 1, StopAfterAct = stopAfterAct },
            NullBotLog.Instance);

    // Two acts of one room each, so that "the act changed" is the only thing happening between two answers.
    private static RunState TwoActs()
    {
        var run = SampleProject.Build().CreateInitialRun(new RunId("stop"), randomSeed: 7);
        run.SetActPlan([new RunActPlan("one", OneRoom("one")), new RunActPlan("two", OneRoom("two"))]);
        return run;
    }

    private static RunMap OneRoom(string id)
    {
        var node = new Node(
            new NodeId(id), StandardRunIds.CombatNode,
            new EncounterRef(new EncounterId("fight")), [MapNodeTags.Combat]);
        return new RunMap([node]) { EntryNodeIds = [node.Id] };
    }
}
