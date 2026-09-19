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
        //
        // ⚠ THE CHAMPION NEEDS THEM TOO, and leaving them out was a measured mistake: it decides what to PLAY
        // by forking the fight, but what to TAKE — a reward, a relic, a slot in a shop — is still scored, and
        // a champion without features scored every offer at nothing and built its deck by the tie-break.
        // A lookahead over a random deck is a careful player holding a bad hand.
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
            + $"policy {policy?.Name ?? "random"}{(options.Champion ? " (champion: one ply of lookahead)" : "")}, "
            + $"{options.Jobs} at a time, one process, "
            + $"{(options.Replay ? "through the replay model" : "answering the engine inline")})");

        if (options.OracleOnly)
            return SurveyOnly(played, options, maps, generator);

        if (options.Routes > 0)
            return WalkEveryRoute(played, options, maps, generator, policy, features);

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

                var (result, log, oracle) = await one.ConfigureAwait(false);
                if (options.OutDir is { } into)
                    File.WriteAllText(Path.Combine(into, $"run-{seed:0000}.log"), log);
                if (!result.Clean)
                    Interlocked.Increment(ref failures);
                lines[seed] = string.Join(Environment.NewLine,
                    [Report(seed, result.Clean ? 0 : 1, BotReport.Result(result)), .. oracle.Select(Indent)]);
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

    // ⚠ THE SAME ROLL GODOT MAKES, IN THE SAME ORDER. Boot picks the character with a Random of its own,
    // seeded the same and then thrown away; the bot's answers come from a second, independent Random on the
    // same seed. Get this wrong and the two hosts play different games from the same number.
    //
    // ⚠⚠ THE ORACLE HAS TO MAKE THE SAME ROLL. Who is walking decides the starting loadout, the loadout
    // decides how the maps are balanced, and a survey of a DIFFERENT character's maps would be a careful
    // measurement of a game nobody played.
    private static string? RollCharacter(RunBlueprint blueprint, int seed)
    {
        var roster = MetaProgression.AvailableCharacters(blueprint, new InMemoryMetaStore().Load());
        return roster.Count > 0 ? roster[new Random(seed).Next(roster.Count)].Id : null;
    }

    private static string Indent(string line) => $"           {line}";

    private static string Report(int seed, int exit, string line) =>
        string.Format(CultureInfo.InvariantCulture, "seed {0,-5} exit {1,-3} {2}", seed, exit, line);

    // ONE RUN, WHOLE AND ON ITS OWN: its own playback, its own profile, its own RNG.
    private static async Task<(BotResult Result, string Log, IReadOnlyList<string> Oracle)> PlayOne(
        RunBlueprint blueprint, int seed, string maps, string generator,
        BotPolicy? policy, CardFeatures? features, CliOptions options,
        IReadOnlyList<string>? route = null)
    {
        var text = new System.Text.StringBuilder();
        var log = new DelegateBotLog(line => text.AppendLine(line));

        var meta = new InMemoryMetaStore();
        var character = RollCharacter(blueprint, seed);

        using var play = new RunPlayback(() => { }, meta);
        var how = new BotOptions
        {
            Seed = seed,
            Budget = options.Steps,
            Maps = maps,
            Character = character,
            Policy = policy,
            Features = features,
            Route = route,
            Champion = options.Champion,
            Autopsy = options.Autopsy,
            AutopsySeconds = options.AutopsySeconds,
            AutopsyPositions = options.AutopsyPositions,
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
        if (BotReport.Autopsy(result) is { } autopsy)
            text.AppendLine(autopsy);
        text.AppendLine(BotReport.Result(result));
        var survey = Survey(blueprint, seed, maps, character, generator, result.Walked, options);
        foreach (var line in survey)
            text.AppendLine(line);
        return (result, text.ToString(), survey);
    }

    // ── EVERY ROUTE THROUGH ONE ACT (O3) ─────────────────────────────────────────────────────────────────
    // One run per route, with the route TOLD rather than chosen. That takes navigation off the table, and
    // what is left is the question V-7 actually asks: is there a way through this act for this player?
    //
    // ⚠⚠ ONLY ONE OF THE TWO ANSWERS IS A FINDING. "Cleared on 3 of 9" is constructive — three walks got
    // through and their rooms are named. "Cleared on 0 of 9" is NOT "this act is impossible": it is this
    // player, on this seed, finding no way. The same honesty the autopsy keeps, and for the same reason.
    //
    // ⚠ AN ACT PAST THE FIRST HAS TO BE REACHED BEFORE ITS ROUTE MEANS ANYTHING. The route names act N's
    // rooms; a run that dies in act one never sees them and is reported as what it is — not reaching the
    // act is a different outcome from failing inside it, and the line says which.
    private static int WalkEveryRoute(
        RunBlueprint blueprint, CliOptions options, string maps, string generator,
        BotPolicy? policy, CardFeatures? features)
    {
        var act = options.Routes;
        var lines = new ConcurrentDictionary<int, string>();
        var unreadable = 0;

        for (var seed = options.SeedFrom; seed < options.SeedFrom + options.Runs; seed++)
        {
            var character = RollCharacter(blueprint, seed);
            var routes = MapOracle.RoutesOfAct(blueprint, seed, act, character, generator);
            if (routes.Count == 0)
            {
                unreadable++;
                lines[seed] = $"sim-clearable: seed={seed} maps={maps} act={act} NO ROUTES — "
                    + $"this run has no act {act}";
                continue;
            }

            var told = new string[routes.Count];
            using var slots = new SemaphoreSlim(options.Jobs);
            var work = routes.Select((route, index) => Task.Run(async () =>
            {
                await slots.WaitAsync().ConfigureAwait(false);
                try
                {
                    var (result, log, _) =
                        await PlayOne(blueprint, seed, maps, generator, policy, features, options, route)
                            .ConfigureAwait(false);
                    if (options.OutDir is { } into)
                        File.WriteAllText(Path.Combine(into, $"route-{seed:0000}-{index:00}.log"), log);
                    told[index] = $"  sim-route: seed={seed} act={act} route={index + 1}/{routes.Count} "
                        + $"result={result.Result} reached={result.Acts} cleared={result.ClearedActs} "
                        + $"rooms={result.Rooms.Count} hp={result.Health}/{result.MaxHealth} "
                        + $"stopped={result.WhereRole} at={result.Where}";
                }
                catch (Exception ex)
                {
                    told[index] = $"  sim-route: seed={seed} act={act} route={index + 1}/{routes.Count} "
                        + $"NO RESULT — {ex.GetType().Name}: {ex.Message}";
                }
                finally
                {
                    slots.Release();
                }
            })).ToArray();
            Task.WaitAll(work);

            // CLEARED means the act's boss went down, which the result reports as `cleared` reaching it.
            // REACHED only means the run arrived — the two differ by exactly the act this is asking about.
            var through = told.Count(line => Cleared(line) >= act);
            var arrived = told.Count(line => Reached(line) >= act);
            lines[seed] = $"sim-clearable: seed={seed} maps={maps} act={act} routes={routes.Count} "
                + $"reached={arrived}/{routes.Count} cleared={through}/{routes.Count}"
                + Environment.NewLine + string.Join(Environment.NewLine, told);
        }

        foreach (var seed in lines.Keys.OrderBy(k => k))
            Console.WriteLine(lines[seed]);

        Console.WriteLine();
        Console.WriteLine(unreadable == 0
            ? $"roguedeck-bot: walked every route through act {act} of {options.Runs} seeds"
            : $"roguedeck-bot: {unreadable} of {options.Runs} seeds have no act {act}");
        return unreadable == 0 ? 0 : 1;
    }

    private static int Cleared(string line) => Field(line, "cleared=");

    private static int Reached(string line) => Field(line, "reached=");

    private static int Field(string line, string name)
    {
        var at = line.IndexOf(name, StringComparison.Ordinal);
        if (at < 0)
            return -1;
        var rest = line[(at + name.Length)..];
        var end = rest.IndexOf(' ', StringComparison.Ordinal);
        return int.TryParse(end < 0 ? rest : rest[..end], NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var value) ? value : -1;
    }

    // ── THE SWEEP THAT PLAYS NOTHING ─────────────────────────────────────────────────────────────────────
    // What a seed's maps are, without a body walking them. A run costs seconds to minutes; this costs the
    // time to lay the maps out, which is why the question "do these maps hand out one game or several?" can
    // be asked of a thousand seeds in the time one run takes.
    //
    // ⚠ IT CANNOT FAIL THE WAY A RUN FAILS, so it does not pretend to: there is nothing here to crash in a
    // fight, no wall to walk into, no verdict on whether anything is beatable. It reports what the maps ARE
    // and exits 0 unless a map could not be built at all.
    private static int SurveyOnly(RunBlueprint blueprint, CliOptions options, string maps, string generator)
    {
        var broken = 0;
        for (var seed = options.SeedFrom; seed < options.SeedFrom + options.Runs; seed++)
        {
            try
            {
                foreach (var act in MapOracle.SurveyRun(blueprint, seed, RollCharacter(blueprint, seed), generator))
                    Console.WriteLine(BotReport.Oracle(seed, maps, act));
            }
            catch (Exception ex)
            {
                broken++;
                Console.WriteLine($"sim-oracle: seed={seed} maps={maps} NO MAP — {ex.GetType().Name}: {ex.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine(broken == 0
            ? $"roguedeck-bot: surveyed {options.Runs} seeds, played none"
            : $"roguedeck-bot: {broken} of {options.Runs} seeds could not lay a map out");
        return broken == 0 ? 0 : 1;
    }

    // The maps this seed lays out, and — when a run walked them — where its doors ranked. Empty unless asked.
    private static IReadOnlyList<string> Survey(
        RunBlueprint blueprint, int seed, string maps, string? character, string? generator,
        IReadOnlyList<string>? walked, CliOptions options) =>
        !options.Oracle
            ? []
            : [.. MapOracle.SurveyRun(blueprint, seed, character, generator, walked)
                .Select(act => BotReport.Oracle(seed, maps, act))];

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
