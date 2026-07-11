using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.Sandbox.Tests;

// Host wiring for save/resume (save/resume follow-up): RunPlayback can snapshot the live run to a save file and
// resume a run from one, over the same InteractiveRunSession machinery the playtest UI drives. This exercises that
// wiring headlessly (a non-interactive run that completes on its own), so the Save/Load buttons rest on tested code.
[Xunit.Collection("Threaded")]
public class RunPlaybackSaveResumeTests
{
    private static bool WaitFor(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            Thread.Sleep(10);
        }
        return false;
    }

    // The smallest run that completes on its own without parking for input: an empty map ends immediately (Victory),
    // reaching a clean save point. (Event nodes park for a UI pick even in a non-interactive run — only fights
    // auto-resolve — so they can't reach completion headlessly.)
    private static RunBlueprint TinyRun() => new(
        Deck: Array.Empty<RogueDeck.Core.Combat.CardDefinitionId>(),
        Events: new Dictionary<string, EventScript>(),
        Encounters: Array.Empty<EncounterDefinition>(),
        Cards: Array.Empty<CardData>(),
        EnemyActions: Array.Empty<EnemyActionData>(),
        Map: new RunMap(Array.Empty<Node>()));

    [Fact]
    public void Saves_the_live_run_and_resumes_it()
    {
        var blueprint = TinyRun();
        using var play = new RunPlayback(() => { });

        play.Start(blueprint, seed: 1, interactive: false);
        Assert.True(WaitFor(() => play.Session?.IsComplete == true, TimeSpan.FromSeconds(30)), play.Error);

        var json = play.SaveJson();
        Assert.NotNull(json);                     // a completed run snapshots cleanly (no scheduled state)
        Assert.Null(play.Error);

        // Resume from the save against the same blueprint — a fresh session is built without throwing.
        play.Resume(blueprint, RunSaveJson.FromJson(json!), interactive: false);
        Assert.Null(play.Error);
        Assert.NotNull(play.Session);
        Assert.True(WaitFor(() => play.Session?.IsComplete == true, TimeSpan.FromSeconds(30)), play.Error);
    }
}
