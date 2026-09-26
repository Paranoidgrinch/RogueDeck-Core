using System.Text.Json;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;
using RogueDeck.Sandbox.Run;

namespace RogueDeck.Bot.Cli;

// ── A PLAYER'S RUN, PLAYED AGAIN ─────────────────────────────────────────────────────────────────────────
//   roguedeck-bot --game content/game.roguedeck.json --replay-run run.bnbrun.json [--decisions out.jsonl] [--log N] [--log-from A] [--resume-every N]
//
// --resume-every N saves the run to JSON at every Nth interlude and resumes a fresh playback from it — the title
// screen's "Continue run" — and says whether the recording still holds: a check on save/resume fidelity.
// --log N prints the last N lines of the replayed run's own log where the replay stopped — at the end, or at the
// room where it parted company. --log-from A prints every log line the run writes from answer A on, answer by
// answer: the run's log is cut at every checkpoint, so the room BEFORE a divergence is only readable live.
//
// Replays a recorded run (RunRecording, as the game uploads it) from its seed, answer by answer, and says
// whether it reproduced — every room's fingerprint the same — or where it parted company. With --decisions it
// also writes what was chosen from what, one JSON object per answer: the view to read a run by, or to train on.
// Exit 0 = reproduced, 1 = did not, 2 = could not be read.
public static class ReplayRun
{
    public static int Run(string[] args)
    {
        string? Value(string flag) =>
            Array.IndexOf(args, flag) is var at and >= 0 && at + 1 < args.Length ? args[at + 1] : null;

        if (Value("--game") is not { } gamePath || Value("--replay-run") is not { } runPath)
        {
            Console.Error.WriteLine(
                "roguedeck-bot --game <game.roguedeck.json> --replay-run <run.bnbrun.json> [--decisions <out.jsonl>]");
            return 2;
        }

        string documentJson;
        RunBlueprint blueprint;
        RunRecording recording;
        try
        {
            documentJson = File.ReadAllText(gamePath);
            blueprint = RunJson.BlueprintFromJson(documentJson, RunJson.CreateOptions());
            recording = RunRecordingJson.FromJson(File.ReadAllText(runPath));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"could not read: {ex.Message}");
            return 2;
        }

        Console.WriteLine($"run: {recording.Player?.Name ?? "?"} ({recording.Player?.Id ?? "?"}) · seed {recording.Start.Seed}"
            + $" · {recording.Start.Character ?? "default character"} · {recording.Answers.Count} answers"
            + $" · {recording.Rooms.Count} room checks · result {recording.Result ?? "open"} · resumed {recording.Resumes}x");
        var hash = RunRecorder.ContentHash(documentJson);
        if (recording.Game.Content is { } recorded && recorded != hash)
            Console.WriteLine($"⚠ content differs: the run was played on {recorded}, this document is {hash}"
                + " — a replay against other content is another game");
        if (recording.Game.Engine is { } engine && engine != RunRecorder.EngineVersion)
            Console.WriteLine($"⚠ engine differs: recorded {engine}, this build {RunRecorder.EngineVersion}");

        if (Value("--log-from") is { } fromText && int.TryParse(fromText, out var from))
        {
            var seen = 0;
            RunReplayer.Replay(blueprint, recording, beforeAnswer: (index, answer, play) =>
            {
                var log = play.Session!.Run.Log;
                if (log.Count < seen)
                    seen = 0; // a checkpoint cut the log
                if (index >= from)
                    foreach (var entry in log.Skip(seen))
                        Console.WriteLine($"  [{index}] log · {entry.Message}");
                if (index >= from)
                    Console.WriteLine($"  [{index}] answer · {string.Join(" ", answer)}");
                seen = log.Count;
            });
        }

        if (Value("--resume-every") is { } everyText && int.TryParse(everyText, out var every) && every > 0)
            return ResumeEvery(blueprint, recording, every);

        var (outcome, decisions) = RunDecisions.Read(blueprint, recording);

        if (Value("--decisions") is { } decisionsPath)
        {
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            File.WriteAllLines(decisionsPath, decisions.Select(d => JsonSerializer.Serialize(d, options)));
            Console.WriteLine($"decisions: {decisions.Count} rows -> {decisionsPath}");
        }

        if (Value("--log") is { } logLines && int.TryParse(logLines, out var tail) && outcome.Run is { } logged)
            foreach (var entry in logged.Log.TakeLast(tail))
                Console.WriteLine($"  log · {entry.Message}");

        if (outcome.Reproduced)
        {
            Console.WriteLine($"REPRODUCED — all {outcome.Answered} answers, every room as recorded"
                + (outcome.Run is { } run ? $" · ends {RunRecorder.Fingerprint(run)}" : ""));
            return 0;
        }
        Console.WriteLine($"NOT REPRODUCED after {outcome.Answered} answers: {outcome.Divergence ?? outcome.Error}");
        return 1;
    }

    // Walk the answers the way a player who keeps quitting would: at every Nth interlude, save to JSON and resume
    // a fresh playback from it — the title screen's "Continue run" — checking every room fingerprint on the way.
    private static int ResumeEvery(RunBlueprint blueprint, RunRecording recording, int every)
    {
        var meta = new MemoryMetaStore();
        if (recording.Start.Meta is { } data)
            meta.Save(MetaState.FromSnapshot(data));
        var play = new RunPlayback(() => { }, meta);
        play.Start(blueprint, recording.Start.Seed, interactive: true, recording.Start.Character,
            recording.Start.MapGenerator);
        var checks = recording.Rooms.ToLookup(c => c.After);
        var interludes = 0;
        try
        {
            for (var i = 0; i < recording.Answers.Count; i++)
            {
                var entry = RunAnswerCodec.Decode(recording.Answers[i]);
                if (entry is InterludeContinueEntry && play.Session is { IsAwaitingInterlude: true } session)
                {
                    if (++interludes % every == 0)
                    {
                        var json = play.SaveJson()!;
                        play.Dispose();
                        play = new RunPlayback(() => { }, meta);
                        play.Resume(blueprint, RunSaveJson.FromJson(json), interactive: true);
                        if (play.Session is { IsAwaitingInterlude: true } resumed)
                            resumed.Continue();
                    }
                    else
                        session.Continue();
                }
                else
                    play.Script!.Advance(entry);
                foreach (var check in checks[i + 1])
                    if (RunRecorder.Fingerprint(play.Session!.Run) is var now && now != check.State)
                    {
                        Console.WriteLine($"RESUMED EVERY {every}: parts at answer {i + 1}: recorded «{check.State}», got «{now}»");
                        return 1;
                    }
            }
        }
        finally
        {
            play.Dispose();
        }
        Console.WriteLine($"RESUMED EVERY {every}: reproduced all {recording.Answers.Count} answers");
        return 0;
    }
}
