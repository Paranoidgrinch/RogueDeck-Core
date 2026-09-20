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

    // ⚠ DRIVE THE RUN THROUGH THE REPLAY MODEL the UI needs, instead of walking it once with the bot in the
    // seat the engine asks (R5). It is the same brain, the same answers and the same report either way —
    // `golden.sh --console` proves that by playing the whole set both ways — but the replay model re-executes
    // the run from its baseline behind EVERY answer, which is about twice the work. Kept because that second
    // driver is what the proof is made of, and because it is the driver the frontend actually uses.
    public bool Replay { get; init; }
    public string? PolicyPath { get; init; }
    public bool Champion { get; init; }
    public bool Autopsy { get; init; }
    public int AutopsySeconds { get; init; } = 60;
    public int AutopsyPositions { get; init; } = 60_000;

    // ⚠ THE ORACLE IS NOT A RUNNER AND COSTS NOTHING A RUNNER COSTS. It reads the maps a seed lays out and
    // says what the doors on them are worth; `--oracle-only` never starts a run at all, which is how a
    // thousand-seed map sweep fits into seconds.
    public bool Oracle { get; init; }
    public bool OracleOnly { get; init; }

    // ⚠ WALK EVERY ROUTE THROUGH ONE ACT, one run each (O3). Not a better runner — a different question:
    // "how far does this player get?" mixes navigation with difficulty, while "does a way through this act
    // exist for this player?" is the one V-7 asks. 0 = off.
    public int Routes { get; init; }

    // ⚠ END THE RUN THE MOMENT ACT N IS CLEARED (T0). The tail past the question is paid for and never read,
    // and it is the EXPENSIVE part: with the fair champion an act-I death costs 423 s and the run that
    // reached act IV cost 4571 s, so a breeding generation gets dearer exactly as the population gets
    // better. 0 = play the game out.
    public int StopAfterAct { get; init; }

    // ⚠ SIT THE CHAMPION AN EXAM (C0): of the positions a proof calls survivable, how many did it survive?
    // A grade for the FIGHTING alone -- no rooms, no doors, no variance of five acts.
    public bool Exam { get; init; }
    public int ExamTurns { get; init; } = 3;
    public int ExamSeconds { get; init; } = 5;
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
          --replay           drive through the replay model instead of answering the engine inline (slower;
                             it is the driver the frontend uses, and half of what golden.sh checks)
          --policy <file>    a bred policy (tools/train.py); without one the runner plays at random
          --autopsy          when a run DIES, play the fight it died in again -- every way it could have
                             gone -- and say whether any of them wins. UNWINNABLE is a proof (the searcher
                             can see the deck, so it is stronger than any fair player); WINNABLE is not a
                             claim that a fair player would find the line
          --autopsy-seconds N  how long one autopsy may search (default 60)
          --autopsy-positions N  how many positions one autopsy may open (default 60000). Raised far
                             past that, a verdict stops saying "the search ran out" and starts being
                             a statement about the FIGHT -- which is how you find out what an
                             exhaustive answer costs here instead of guessing
          --champion         decide each play by FORKING the fight and looking: the card is played on a copy,
                             the enemies answer, and what is left is what decides. Costs a fight-clone per
                             candidate and buys the one thing scoring cannot -- it sees what is coming. The
                             policy's Aggression is its only knob (0 survive, 1 empty the enemy)
          --oracle           after the run, read the MAPS it walked: every path through every act, what
                             the lightest and heaviest of them are worth in authored threat (spread), and
                             where the runner's own doors ranked in that field. A wide spread the runner
                             keeps ranking badly in is a statement about the RUNNER; a narrow one says the
                             doors are not where it is losing. ⚠ Threat is a proxy for difficulty, never a
                             measure of it — the spread and the rank are what survive that
          --oracle-only      survey the maps and play NOTHING. Seconds for a sweep a batch of runs would
                             spend days on
          --routes N         walk EVERY route through act N, one run per route, and say how many of them
                             got through it. Navigation is off the table, so what comes back is about the
                             ACT rather than about the runner's doors. ⚠ Cleared on some route is a
                             constructive yes; cleared on none is "this player found none", never "none
                             exists"
          --exam             grade the FIGHTING on its own: every hero-turn the run stood in is put to a
                             proof (is a line that survives N turns available from here?) and to the
                             champion (does it survive them?). A position the proof calls survivable and
                             the player dies in is a MISS, and a miss is not an opinion. ⚠ `held` alone is
                             not a grade -- a player that blocks forever holds everything and wins nothing,
                             so `dealt` is reported beside it
          --exam-turns N     how many hero-turns both are asked to get through (default 3)
          --exam-seconds N   how long ONE position's proof may search (default 5)
          --stop-after-act N  end each run the moment act N is cleared, i.e. at the gates of act N+1.
                             The run reports acts 1..N exactly as it would have without the flag; what is
                             dropped is the tail nobody was measuring. ⚠ Those runs end as `Ongoing` and
                             `calledOff=asked` on the clearance line -- a run CALLED OFF is not a run that
                             stood still, and anything scoring them must tell the two apart
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
        var champion = false;
        var autopsy = false;
        var autopsySeconds = 60;
        var autopsyPositions = 60_000;
        var oracle = false;
        var oracleOnly = false;
        var routes = 0;
        var stopAfterAct = 0;
        var exam = false;
        var examTurns = 3;
        var examSeconds = 5;
        int? health = null;
        var legacy = false;
        var replay = false;
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
                case "--replay": replay = true; break;
                case "--policy": policy = Next(); break;
                case "--champion": champion = true; break;
                case "--autopsy": autopsy = true; break;
                case "--autopsy-seconds": autopsySeconds = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--autopsy-positions": autopsyPositions = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--oracle": oracle = true; break;
                case "--oracle-only": oracleOnly = oracle = true; break;
                case "--routes": routes = Number(Next()); break;
                case "--stop-after-act": stopAfterAct = Number(Next()); break;
                case "--exam": exam = true; break;
                case "--exam-turns": examTurns = Number(Next()); break;
                case "--exam-seconds": examSeconds = Number(Next()); break;
                case "--out": outDir = Next(); break;
                default: return null;
            }
        }

        if (game is null || runs < 1 || jobs < 1 || steps < 1 || timeout < 1 || stopAfterAct < 0)
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
            Replay = replay,
            PolicyPath = policy,
            Champion = champion,
            Autopsy = autopsy,
            AutopsySeconds = autopsySeconds,
            AutopsyPositions = autopsyPositions,
            Oracle = oracle,
            OracleOnly = oracleOnly,
            Routes = routes,
            StopAfterAct = stopAfterAct,
            Exam = exam,
            ExamTurns = examTurns,
            ExamSeconds = examSeconds,
            OutDir = outDir,
        };
    }
}
