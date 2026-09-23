using RogueDeck.Bot;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;

namespace RogueDeck.Sandbox.Tests;

// A RECORDED RUN PLAYS AGAIN, ANSWER FOR ANSWER. The recording holds the seed, the start and the answers and
// nothing else, so these tests are the whole claim: a run walked through the real interactive session — by the
// runner, which answers every kind of prompt a player meets — is recorded, written to TEXT, read back, and
// replayed from its seed to the same fingerprint in every room. And the same across a save and a resume, which is
// how a player's run actually spans evenings.
public class RunRecordingTests
{
    private static RunBlueprint Sample() =>
        RunJson.BlueprintFromJson(BureaucratsSample.Json(), RunJson.CreateOptions());

    private static RunRecording Begin(int seed) => RunRecorder.Begin(
        seed, character: null, mapGenerator: null, meta: null,
        new RunRecordingPlayer("Tester", "abc123"), contentHash: "test", host: "tests", DateTime.UnixEpoch);

    private static void Walk(RunPlayback play, int seed, int budget) =>
        RunBot.Play(play, new BotOptions { Seed = seed, Budget = budget }, new NullBotLog()).GetAwaiter().GetResult();

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void A_recorded_run_replays_to_the_same_state_in_every_room(int seed)
    {
        var blueprint = Sample();
        using var play = new RunPlayback(() => { });
        play.Start(blueprint, seed, interactive: true);
        var recorder = new RunRecorder(Begin(seed));
        recorder.Attach(play);
        Walk(play, seed, budget: 4000);
        recorder.Detach();

        var recording = RunRecordingJson.FromJson(RunRecordingJson.ToJson(recorder.Recording));
        Assert.True(recording.Answers.Count > 20, $"only {recording.Answers.Count} answers recorded");
        Assert.True(recording.Rooms.Count > 2, $"only {recording.Rooms.Count} rooms recorded");
        Assert.Contains(recording.Answers, a => a[0] == "c");
        Assert.Contains(recording.Answers, a => a[0] == "t");

        var outcome = RunReplayer.Replay(blueprint, recording);

        Assert.True(outcome.Error is null, outcome.Error);
        Assert.True(outcome.Divergence is null, outcome.Divergence);
        Assert.True(outcome.Reproduced);
        Assert.Equal(RunRecorder.Fingerprint(play.Session!.Run), RunRecorder.Fingerprint(outcome.Run!));
    }

    [Fact]
    public void A_run_saved_and_resumed_part_way_is_still_one_recording_that_replays()
    {
        const int seed = 3;
        var blueprint = Sample();
        var recorder = new RunRecorder(Begin(seed));

        string save;
        using (var first = new RunPlayback(() => { }))
        {
            first.Start(blueprint, seed, interactive: true);
            recorder.Attach(first);
            Walk(first, seed, budget: 60);
            recorder.Detach();
            save = first.SaveJson() ?? throw new InvalidOperationException(first.Error);
        }
        var saved = RunRecordingJson.ToJson(recorder.Recording);

        // An evening later: the save and the recording come back off the disk together.
        var resumedRecorder = new RunRecorder(RunRecordingJson.FromJson(saved));
        resumedRecorder.Recording.Resumes++;
        using var second = new RunPlayback(() => { });
        second.Resume(blueprint, RunSaveJson.FromJson(save), interactive: true);
        Assert.Null(second.Error);
        resumedRecorder.Attach(second);
        Walk(second, seed + 100, budget: 4000);
        resumedRecorder.Detach();

        var recording = RunRecordingJson.FromJson(RunRecordingJson.ToJson(resumedRecorder.Recording));
        Assert.Equal(1, recording.Resumes);
        var outcome = RunReplayer.Replay(blueprint, recording);

        Assert.True(outcome.Error is null, outcome.Error);
        Assert.True(outcome.Divergence is null, outcome.Divergence);
        Assert.Equal(RunRecorder.Fingerprint(second.Session!.Run), RunRecorder.Fingerprint(outcome.Run!));
    }

    [Fact]
    public void A_replay_that_parts_company_with_its_recording_says_where()
    {
        var blueprint = Sample();
        using var play = new RunPlayback(() => { });
        play.Start(blueprint, 1, interactive: true);
        var recorder = new RunRecorder(Begin(1));
        recorder.Attach(play);
        Walk(play, 1, budget: 4000);
        recorder.Detach();

        // Same answers, another seed: another deal, another run — and the fingerprints catch it.
        var wrong = RunRecordingJson.FromJson(RunRecordingJson.ToJson(recorder.Recording with
        {
            Start = recorder.Recording.Start with { Seed = 99 },
        }));
        var outcome = RunReplayer.Replay(blueprint, wrong);

        Assert.False(outcome.Reproduced);
        Assert.True(outcome.Divergence is not null || outcome.Error is not null);
    }

    // The host's counts ride along in the file and come back out of the TEXT; a file written before they existed
    // still reads, with none.
    [Fact]
    public void Tallies_survive_the_file_and_an_older_file_has_none()
    {
        var recording = Begin(3);
        recording.Tallies["enemies"] = 12;
        recording.Tallies["bosses"] = 1;

        var back = RunRecordingJson.FromJson(RunRecordingJson.ToJson(recording));
        Assert.Equal(12, back.Tallies["enemies"]);
        Assert.Equal(1, back.Tallies["bosses"]);

        var older = RunRecordingJson.ToJson(Begin(4)).Replace("\"Tallies\"", "\"Unused\"", StringComparison.Ordinal);
        Assert.Empty(RunRecordingJson.FromJson(older).Tallies);
    }

    [Fact]
    public void Every_kind_of_answer_survives_the_codec()
    {
        ReplayEntry[] entries =
        [
            new EventPickEntry("leave"),
            new NodePickEntry("a1-7"),
            new EntityPicksEntry([2, 0]),
            new EntityPicksEntry([]),
            new InterludeContinueEntry(),
            new ParkConsumableEntry(new ConsumableInstanceId("potion-1")),
            new CombatPlayEntry(null, new RogueDeck.Core.Combat.CardInstanceId("card-4"), new RogueDeck.Core.Combat.CombatantId("enemy-1")),
            new CombatPlayEntry(new RogueDeck.Core.Combat.CombatantId("m2"), new RogueDeck.Core.Combat.CardInstanceId("card-5"), null),
            new CombatPlayEntry(null, new RogueDeck.Core.Combat.CardInstanceId("card-6"), null),
            new CombatEndTurnEntry(null),
            new CombatEndTurnEntry(new RogueDeck.Core.Combat.CombatantId("m2")),
            new CombatConsumableEntry(new ConsumableInstanceId("potion-2")),
            new CardPicksEntry([new RogueDeck.Core.Combat.CardInstanceId("card-1"), new RogueDeck.Core.Combat.CardInstanceId("card-2")]),
            new OptionPicksEntry([1]),
        ];

        foreach (var entry in entries)
        {
            var back = RunAnswerCodec.Decode(RunAnswerCodec.Encode(entry));
            Assert.Equal(Describe(entry), Describe(back));
        }
        Assert.Equal(["t"], RunAnswerCodec.Encode(new CombatEndTurnEntry(null)));
    }

    // Records compare their list fields by reference; compare what they say instead.
    private static string Describe(ReplayEntry entry) => entry switch
    {
        EntityPicksEntry p => $"p {string.Join(",", p.Indices)}",
        CardPicksEntry k => $"k {string.Join(",", k.Picks)}",
        OptionPicksEntry o => $"o {string.Join(",", o.Indices)}",
        _ => entry.ToString(),
    };
}
