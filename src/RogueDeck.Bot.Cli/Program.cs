using System.Collections.Concurrent;
using System.Globalization;
using RogueDeck.Bot;
using RogueDeck.Run;
using RogueDeck.Sandbox.Composition;

namespace RogueDeck.Bot.Cli;

// ── THE CONSOLE RUNNER ───────────────────────────────────────────────────────────────────────────────────
// N runs in ONE process, on M threads. What that buys is the fixed cost of a run: booting Godot, parsing an
// 11 MB document, warming the JIT and laying out a map cost about five seconds per process, and the
// process-per-run batch paid them N times. Here the document is parsed once and the JIT warms once.
//
//   roguedeck-bot --game content/game.roguedeck.json --runs 100 --immortal --jobs 12 --out ~/logs
//
// ⚠⚠ WHAT THE PROCESS-PER-RUN MODEL WAS BOUGHT FOR HAS TO BE PAID FOR AGAIN. A crash must cost ONE run, not
// the batch. So every run gets its own RunPlayback, its own RNG, its own meta profile, its own try/catch and
// its own watchdog — and a run that never comes back is written down as the absence it is rather than
// hanging the batch behind it.
//
// ⚠ A RUN THAT REPORTS NOTHING IS A FAILURE, NEVER A PASS. A crash, a timeout, a walk that never ended: each
// is printed as what it is and counts against the exit code. Silence must never read as agreement.
public static class Program
{
    public static int Main(string[] args)
    {
        var options = CliOptions.Parse(args);
        if (options is null)
        {
            Console.Error.WriteLine(CliOptions.Usage);
            return 2;
        }

        if (!File.Exists(options.GamePath))
        {
            Console.Error.WriteLine($"no game document at {options.GamePath}");
            return 2;
        }

        var documentJson = File.ReadAllText(options.GamePath);
        RunBlueprint blueprint;
        try
        {
            blueprint = RunJson.BlueprintFromJson(documentJson, RunJson.CreateOptions());
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"could not read the game document: {ex.Message}");
            return 2;
        }

        BotPolicy? policy = null;
        if (options.PolicyPath is { } policyPath)
        {
            policy = BotPolicy.Load(policyPath);
            if (policy is null)
            {
                Console.Error.WriteLine($"could not read the policy at {policyPath}");
                return 2;
            }
        }
        // Only a policy runner scores cards; the dice player never asks what a card does.
        var features = policy is null ? null : CardFeatures.FromDocument(documentJson);

        // The body, applied to the blueprint ONCE for the whole batch — every run shares this record, which
        // is immutable, and builds its own content from it.
        var played = options.Health is { } hp ? WithHealth(blueprint, hp) : blueprint;
        var generator = options.Legacy ? MapGenerators.RuleBased : MapGenerators.Strategic;
        var maps = MapGenerators.Name(generator);

        if (options.OutDir is { } dir)
            Directory.CreateDirectory(dir);

        Console.WriteLine($"roguedeck-bot: {options.Runs} runs "
            + $"(seeds {options.SeedFrom}..{options.SeedFrom + options.Runs - 1}, "
            + $"{(options.Health is { } h ? $"{h} hp" : "authored health")}, maps {maps}, "
            + $"policy {policy?.Name ?? "random"}, {options.Jobs} at a time, one process, "
            + $"{(options.Replay ? "through the replay model" : "answering the engine inline")})");

        var lines = new ConcurrentDictionary<int, string>();
        var failures = 0;
        using var slots = new SemaphoreSlim(options.Jobs);
        var work = Enumerable.Range(options.SeedFrom, options.Runs).Select(seed => Task.Run(async () =>
        {
            await slots.WaitAsync().ConfigureAwait(false);
            try
            {
                var one = PlayOne(played, seed, maps, generator, policy, features, options);
                // ⚠ THE WATCHDOG CANNOT RECLAIM A THREAD, AND SAYS SO RATHER THAN PRETENDING. A run that is
                // still going after the timeout is reported as an absence and the batch carries on without
                // it; the thread it left behind keeps a core until the process ends. That is the honest
                // trade for staying in one process, and it is loud.
                var finished = await Task.WhenAny(one, Task.Delay(TimeSpan.FromSeconds(options.Timeout)))
                    .ConfigureAwait(false);
                if (finished != one)
                {
                    Interlocked.Increment(ref failures);
                    lines[seed] = Report(seed, 1,
                        $"sim-result: seed={seed} maps={maps} THE RUN WAS CUT OFF BY THE WATCHDOG "
                        + $"AFTER {options.Timeout} s AND PRODUCED NO RESULT LINE");
                    return;
                }

                var (result, log) = await one.ConfigureAwait(false);
                if (options.OutDir is { } into)
                    File.WriteAllText(Path.Combine(into, $"run-{seed:0000}.log"), log);
                if (!result.Clean)
                    Interlocked.Increment(ref failures);
                lines[seed] = Report(seed, result.Clean ? 0 : 1, BotReport.Result(result));
            }
            catch (Exception ex)
            {
                // A crash costs this run and nothing else.
                Interlocked.Increment(ref failures);
                lines[seed] = Report(seed, 1,
                    $"sim-result: seed={seed} maps={maps} THE RUN PRODUCED NO RESULT LINE — "
                    + $"{ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                slots.Release();
            }
        })).ToArray();

        Task.WaitAll(work);

        foreach (var seed in lines.Keys.OrderBy(k => k))
            Console.WriteLine(lines[seed]);

        Console.WriteLine();
        Console.WriteLine(failures == 0
            ? $"roguedeck-bot: all {options.Runs} runs came back clean"
            : $"roguedeck-bot: {failures} of {options.Runs} runs are worth reading");
        return failures == 0 ? 0 : 1;
    }

    private static string Report(int seed, int exit, string line) =>
        string.Format(CultureInfo.InvariantCulture, "seed {0,-5} exit {1,-3} {2}", seed, exit, line);

    // ONE RUN, WHOLE AND ON ITS OWN: its own playback, its own profile, its own RNG.
    private static async Task<(BotResult Result, string Log)> PlayOne(
        RunBlueprint blueprint, int seed, string maps, string generator,
        BotPolicy? policy, CardFeatures? features, CliOptions options)
    {
        var text = new System.Text.StringBuilder();
        var log = new DelegateBotLog(line => text.AppendLine(line));

        // ⚠ THE SAME ROLL GODOT MAKES, IN THE SAME ORDER. Boot picks the character with a Random of its own,
        // seeded the same and then thrown away; the bot's answers come from a second, independent Random on
        // the same seed. Get this wrong and the two hosts play different games from the same number.
        var meta = new InMemoryMetaStore();
        var roster = MetaProgression.AvailableCharacters(blueprint, meta.Load());
        var character = roster.Count > 0 ? roster[new Random(seed).Next(roster.Count)].Id : null;

        using var play = new RunPlayback(() => { }, meta);
        var how = new BotOptions
        {
            Seed = seed,
            Budget = options.Steps,
            Maps = maps,
            Character = character,
            Policy = policy,
            Features = features,
        };

        // ⚠⚠ TWO SEATS, ONE BRAIN (R5). By default the run is walked ONCE, with the bot answering the engine
        // where it stands. `--replay` drives it through the replay model the UI needs instead, which
        // re-executes the run from its baseline behind every single answer. The same log has to come out of
        // both, and `golden.sh --console` plays the whole set each way to say so — which is the strongest
        // statement this project has made that replay and direct play are the same game.
        BotResult result;
        if (options.Replay)
        {
            play.Start(blueprint, seed, interactive: true, character, generator);
            result = await RunBot.Play(play, how, log).ConfigureAwait(false);
        }
        else
            result = RunBot.PlayDirect(play, blueprint, character, generator, how, log);

        foreach (var line in BotReport.ActLines(result))
            text.AppendLine(line);
        text.AppendLine(BotReport.Fitness(result));
        text.AppendLine(BotReport.Clearance(result));
        text.AppendLine(BotReport.Result(result));
        return (result, text.ToString());
    }

    // A body that survives the whole game, for coverage runs: a walk that dies in the first act never reaches
    // the second one. The same transform Godot's `--sim-immortal` applies.
    private static RunBlueprint WithHealth(RunBlueprint blueprint, int health)
    {
        RunStart Raise(RunStart start) => start with { MaxHealth = health, StartingHealth = health };
        return blueprint with
        {
            Start = Raise(blueprint.Start),
            Characters = [.. blueprint.Characters.Select(c => c with { Start = Raise(c.Start) })],
        };
    }
}
