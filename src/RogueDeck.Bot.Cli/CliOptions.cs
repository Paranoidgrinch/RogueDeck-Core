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

    // ⚠ DRIVE THE RUN THROUGH THE REPLAY MODEL the UI needs, instead of walking it once with the bot in the
    // seat the engine asks (R5). It is the same brain, the same answers and the same report either way —
    // `golden.sh --console` proves that by playing the whole set both ways — but the replay model re-executes
    // the run from its baseline behind EVERY answer, which is about twice the work. Kept because that second
    // driver is what the proof is made of, and because it is the driver the frontend actually uses.
    public bool Replay { get; init; }

    // ⚠ THE ORACLE IS NOT A RUNNER AND COSTS NOTHING A RUNNER COSTS. It reads the maps a seed lays out and
    // says what the doors on them are worth; `--oracle-only` never starts a run at all, which is how a
    // thousand-seed map sweep fits into seconds.
    public bool Oracle { get; init; }
    public bool OracleOnly { get; init; }
    public bool Lanes { get; init; }

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
          --replay           drive through the replay model instead of answering the engine inline (slower;
                             it is the driver the frontend uses, and half of what golden.sh checks)
          --oracle           after the run, read the MAPS it walked: every path through every act, what
                             the lightest and heaviest of them are worth in authored threat (spread), and
                             where the runner's own doors ranked in that field. A wide spread the runner
                             keeps ranking badly in is a statement about the RUNNER; a narrow one says the
                             doors are not where it is losing. ⚠ Threat is a proxy for difficulty, never a
                             measure of it — the spread and the rank are what survive that
          --oracle-only      survey the maps and play NOTHING. Seconds for a sweep a batch of runs would
                             spend days on
          --lanes            lay the maps out (play nothing) and say what each act's paths hold: how many
                             ways, the best and worst lane, and how often a lane has no rest
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
        var oracle = false;
        var oracleOnly = false;
        var lanes = false;
        int? health = null;
        var replay = false;
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
                case "--replay": replay = true; break;
                case "--oracle": oracle = true; break;
                case "--oracle-only": oracleOnly = oracle = true; break;
                case "--lanes": lanes = true; break;
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
            Replay = replay,
            Oracle = oracle,
            OracleOnly = oracleOnly,
            Lanes = lanes,
            OutDir = outDir,
        };
    }
}
