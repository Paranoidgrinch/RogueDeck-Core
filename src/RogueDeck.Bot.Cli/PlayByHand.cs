using System.Globalization;
using System.Text.Json;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;

namespace RogueDeck.Bot.Cli;

// ── `--play`: A RUN BY HAND (TextPlay) ───────────────────────────────────────────────────────────────────────
//   roguedeck-bot --game <doc> --play <save.json> [--seed N] [--do "p 0 0; p 1; e"]
// The save is the seed, the character and every answer taken so far. A new save starts at --seed (default 1);
// an existing one ignores it. Each call replays the save, applies --do's answers in order (stopping at the first
// the game refuses, which is not saved) and prints where the run stands.
public static class PlayByHand
{
    private sealed record Save(int Seed, string? Character, List<string> Answers);

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static int Run(string[] args)
    {
        string? Value(string flag) =>
            Array.IndexOf(args, flag) is var at and >= 0 && at + 1 < args.Length ? args[at + 1] : null;
        if (Value("--game") is not { } gamePath || Value("--play") is not { } savePath)
        {
            Console.Error.WriteLine("roguedeck-bot --game <doc> --play <save.json> [--seed N] [--do \"p 0 0; e\"]");
            return 2;
        }

        var blueprint = RunJson.BlueprintFromJson(File.ReadAllText(gamePath), RunJson.CreateOptions());
        Save save;
        if (File.Exists(savePath))
            save = JsonSerializer.Deserialize<Save>(File.ReadAllText(savePath))!;
        else
        {
            var seed = int.Parse(Value("--seed") ?? "1", CultureInfo.InvariantCulture);
            var roster = MetaProgression.AvailableCharacters(blueprint, new InMemoryMetaStore().Load());
            save = new Save(seed, roster.Count > 0 ? roster[new Random(seed).Next(roster.Count)].Id : null, []);
        }

        var fresh = (Value("--do") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var outcome = TextPlay.Play(blueprint, save.Seed, save.Character, MapGenerators.Strategic, save.Answers, fresh);
        File.WriteAllText(savePath, JsonSerializer.Serialize(save with { Answers = [.. outcome.Accepted] }, Json));
        Console.WriteLine($"seed {save.Seed} · {save.Character ?? "—"} · {outcome.Accepted.Count} answers");
        Console.Write(outcome.Text);
        return outcome.Refused is null ? 0 : 1;
    }
}
