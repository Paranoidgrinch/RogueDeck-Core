using System.Globalization;

namespace RogueDeck.Bot.Cli;

public sealed record CliOptions
{
    public required string GamePath { get; init; }
    public int Runs { get; init; } = 1;
    public int SeedFrom { get; init; } = 1;
    public int Jobs { get; init; } = Environment.ProcessorCount;
    public int Steps { get; init; } = 40000;
    public int Timeout { get; init; } = 1800;
    public int? Health { get; init; }
    public bool Legacy { get; init; }
    public string? PolicyPath { get; init; }
    public string? OutDir { get; init; }

    public const string Usage = """
        roguedeck-bot --game <game.roguedeck.json> [options]

          --runs N           how many runs to play (default 1)
          --seed-from N      the first seed (default 1); the runs are seeds N .. N+runs-1
          --jobs N           runs at a time in this one process (default: cores)
          --steps N          the ceiling on ANSWERS per run (default 40000)
          --timeout N        seconds one run may take before the watchdog writes it off (default 1800)
          --immortal         a body that survives the whole game (9999 hp)
          --health N         a stated body
          --legacy           walk the OLD maps (v0.0.0) instead of the design's v0.0.1
          --policy <file>    a bred policy (tools/train.py); without one the runner plays at random
          --out <dir>        write one full log per run into this directory
        """;

    public static CliOptions? Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        string? game = null;
        var runs = 1;
        var seedFrom = 1;
        var jobs = Environment.ProcessorCount;
        var steps = 40000;
        var timeout = 1800;
        int? health = null;
        var legacy = false;
        string? policy = null;
        string? outDir = null;

        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : "";
            static int Number(string text) =>
                int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : -1;
            switch (args[i])
            {
                case "--game": game = Next(); break;
                case "--runs": runs = Number(Next()); break;
                case "--seed-from": seedFrom = Number(Next()); break;
                case "--jobs": jobs = Number(Next()); break;
                case "--steps": steps = Number(Next()); break;
                case "--timeout": timeout = Number(Next()); break;
                case "--immortal": health = 9999; break;
                case "--health": health = Number(Next()); break;
                case "--legacy": legacy = true; break;
                case "--policy": policy = Next(); break;
                case "--out": outDir = Next(); break;
                default: return null;
            }
        }

        if (game is null || runs < 1 || jobs < 1 || steps < 1 || timeout < 1)
            return null;

        return new CliOptions
        {
            GamePath = game,
            Runs = runs,
            SeedFrom = seedFrom,
            Jobs = jobs,
            Steps = steps,
            Timeout = timeout,
            Health = health,
            Legacy = legacy,
            PolicyPath = policy,
            OutDir = outDir,
        };
    }
}
