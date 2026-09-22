using System.Text.Json;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;

namespace RogueDeck.Bot.Cli;

// ── A PLAYER'S RUN, PLAYED AGAIN ─────────────────────────────────────────────────────────────────────────
//   roguedeck-bot --game content/game.roguedeck.json --replay-run run.bnbrun.json [--decisions out.jsonl]
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

        var (outcome, decisions) = RunDecisions.Read(blueprint, recording);

        if (Value("--decisions") is { } decisionsPath)
        {
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            File.WriteAllLines(decisionsPath, decisions.Select(d => JsonSerializer.Serialize(d, options)));
            Console.WriteLine($"decisions: {decisions.Count} rows -> {decisionsPath}");
        }

        if (outcome.Reproduced)
        {
            Console.WriteLine($"REPRODUCED — all {outcome.Answered} answers, every room as recorded"
                + (outcome.Run is { } run ? $" · ends {RunRecorder.Fingerprint(run)}" : ""));
            return 0;
        }
        Console.WriteLine($"NOT REPRODUCED after {outcome.Answered} answers: {outcome.Divergence ?? outcome.Error}");
        return 1;
    }
}
