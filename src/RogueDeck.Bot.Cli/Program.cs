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
        if (args.Contains("--replay-run"))
            return ReplayRun.Run(args);
        if (args.Contains("--play"))
            return PlayByHand.Run(args);
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

        // The body, applied to the blueprint ONCE for the whole batch — every run shares this record, which
        // is immutable, and builds its own content from it.
        var played = options.Health is { } hp ? WithHealth(blueprint, hp) : blueprint;
        var generator = MapGenerators.Strategic;
        var maps = MapGenerators.Name(generator);

        if (options.OutDir is { } dir)
            Directory.CreateDirectory(dir);

        Console.WriteLine($"roguedeck-bot: {options.Runs} runs "
            + $"(seeds {options.SeedFrom}..{options.SeedFrom + options.Runs - 1}, "
            + $"{(options.Health is { } h ? $"{h} hp" : "authored health")}, maps {maps}, "
            + $"{options.Jobs} at a time, one process, "
            + $"{(options.Replay ? "through the replay model" : "answering the engine inline")})");

        if (options.OracleOnly)
            return SurveyOnly(played, options, maps, generator);
        if (options.Lanes)
            return Lanes(played, options, generator);

        var lines = new ConcurrentDictionary<int, string>();
        var failures = 0;
        using var slots = new SemaphoreSlim(options.Jobs);
        var work = Enumerable.Range(options.SeedFrom, options.Runs).Select(seed => Task.Run(async () =>
        {
            await slots.WaitAsync().ConfigureAwait(false);
            try
            {
                var one = PlayOne(played, seed, maps, generator, options);
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
        RunBlueprint blueprint, int seed, string maps, string generator, CliOptions options)
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
        var survey = Survey(blueprint, seed, maps, character, generator, result.Walked, options);
        foreach (var line in survey)
            text.AppendLine(line);
        return (result, text.ToString(), survey);
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

    // ── WHAT A DOOR COMMITS YOU TO (playtest 2026-09-27) ─────────────────────────────────────────────────
    // An act's paths hardly ever cross after the first rows, so choosing a door early is choosing every room
    // to the boss. This lays the maps out (nothing is played) and reads every full path through each act as
    // a tally of room kinds: how many ways there are, how far apart the best and the worst are, and how often
    // a seed hands out a lane with many elites and nowhere to rest.
    private static int Lanes(RunBlueprint blueprint, CliOptions options, string generator)
    {
        var perAct = new Dictionary<int, List<(int Seed, List<Dictionary<string, int>> Paths)>>();
        var forks = new Dictionary<(int Act, int Depth), int>();
        // PER ROUTE (playtest feedback 2, D2/D3): how many REAL decisions a walk through the act offers — a room
        // with two or more ways on whose next rooms are not all of one kind — and whether its last room before
        // the boss is a campfire.
        var realForks = new Dictionary<int, List<int>>();
        var restBeforeBoss = new Dictionary<int, (int Yes, int All)>();
        // Every placed fight that has a band, and how far outside it it stood (0 = inside).
        var placed = new Dictionary<int, List<(bool Elite, int Outside)>>();
        for (var seed = options.SeedFrom; seed < options.SeedFrom + options.Runs; seed++)
        {
            var character = RollCharacter(blueprint, seed);
            var loadout = new BalanceCalculator(blueprint.Balance, blueprint.Encounters)
                .LoadoutStrength(blueprint.ResolveStart(character), blueprint.Deck, character);
            var acts = blueprint.BuildActPlan(seed, loadout, generator);
            for (var i = 0; i < acts.Count; i++)
            {
                var map = acts[i].Map;
                var byId = map.Nodes.ToDictionary(n => n.Id.Value, StringComparer.Ordinal);
                var paths = MapOracle.Routes(map, 20_000)
                    .Select(route => route.GroupBy(id => MapRole.Of(byId[id]), StringComparer.Ordinal)
                        .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal))
                    .ToList();
                (perAct.TryGetValue(i + 1, out var list) ? list : perAct[i + 1] = []).Add((seed, paths));
                foreach (var route in MapOracle.Routes(map, 20_000))
                {
                    var real = 0;
                    foreach (var id in route)
                    {
                        var next = map.SuccessorIds(new NodeId(id));
                        // Two fights are one kind of door, whatever their size.
                        if (next.Count >= 2 && next.Select(n => MapRole.Of(byId[n.Value]) is var role && role == MapNodeTags.MultiCombat
                                ? MapNodeTags.Combat : role).Distinct(StringComparer.Ordinal).Count() >= 2)
                            real++;
                    }
                    (realForks.TryGetValue(i + 1, out var reals) ? reals : realForks[i + 1] = []).Add(real);
                    var last = route.Count >= 2 && MapRole.Of(byId[route[^1]]) == "boss" ? route[^2] : null;
                    var (yes, all) = restBeforeBoss.GetValueOrDefault(i + 1);
                    if (last is not null)
                        restBeforeBoss[i + 1] = (yes + (MapRole.Of(byId[last]) == "rest" ? 1 : 0), all + 1);
                }
                // WHERE A PLAYER CAN STILL CHANGE LANE: nodes with two or more ways on, by depth.
                var depths = map.Depths();
                if (i < blueprint.Acts.Count && blueprint.Acts[i].MapGeneration is { } spec)
                {
                    var rows = depths.Values.DefaultIfEmpty(0).Max() + 1;
                    foreach (var node in map.Nodes)
                        if (node.Payload is EncounterRef fight
                            && spec.EncounterMaximumDepthPercent.TryGetValue(fight.Id.Value, out var ceiling))
                        {
                            var depth = MapDepth.Percent(depths.GetValueOrDefault(node.Id), rows);
                            var floor = spec.EncounterMinimumDepthPercent.GetValueOrDefault(fight.Id.Value);
                            (placed.TryGetValue(i + 1, out var outs) ? outs : placed[i + 1] = [])
                                .Add((node.HasTag(MapNodeTags.Elite),
                                    Math.Max(0, floor - depth) + Math.Max(0, depth - ceiling)));
                        }
                }
                foreach (var node in map.Nodes)
                    if (map.SuccessorIds(node.Id).Count >= 2)
                    {
                        var key = (i + 1, depths.GetValueOrDefault(node.Id));
                        forks[key] = forks.GetValueOrDefault(key) + 1;
                    }
            }
        }

        static int Of(Dictionary<string, int> path, string role) => path.GetValueOrDefault(role);
        static string Median(IEnumerable<int> values)
        {
            var sorted = values.Order().ToList();
            return sorted.Count == 0 ? "—" : sorted[sorted.Count / 2].ToString(CultureInfo.InvariantCulture);
        }

        foreach (var (act, seeds) in perAct.OrderBy(kv => kv.Key))
        {
            var withPaths = seeds.Where(s => s.Paths.Count > 0).ToList();
            var n = withPaths.Count;
            var eliteSpread = withPaths.Select(s => s.Paths.Max(p => Of(p, "elite")) - s.Paths.Min(p => Of(p, "elite")));
            var restSpread = withPaths.Select(s => s.Paths.Max(p => Of(p, "rest")) - s.Paths.Min(p => Of(p, "rest")));
            // A HARD LANE: at least as many elites as the act's most, and no rest on it at all.
            var hard = withPaths.Count(s => s.Paths.Any(p => Of(p, "elite") >= 3 && Of(p, "rest") == 0));
            var restless = withPaths.Count(s => s.Paths.Any(p => Of(p, "rest") == 0));
            Console.WriteLine($"act {act}: {n} seeds · paths per map median {Median(withPaths.Select(s => s.Paths.Count))} "
                + $"(min {withPaths.Min(s => s.Paths.Count)}, max {withPaths.Max(s => s.Paths.Count)})");
            Console.WriteLine($"  elites on a path: median {Median(withPaths.SelectMany(s => s.Paths.Select(p => Of(p, "elite"))))}"
                + $" · best-vs-worst lane spread median {Median(eliteSpread)} (max {eliteSpread.Max()})");
            Console.WriteLine($"  rests on a path:  median {Median(withPaths.SelectMany(s => s.Paths.Select(p => Of(p, "rest"))))}"
                + $" · spread median {Median(restSpread)} (max {restSpread.Max()})");
            Console.WriteLine($"  seeds with a lane that has NO rest: {restless}/{n} · with 3+ elites and no rest: {hard}/{n}");
            var all = withPaths.SelectMany(s => s.Paths).ToList();
            string Share(Func<Dictionary<string, int>, bool> test) =>
                (100.0 * all.Count(test) / Math.Max(1, all.Count)).ToString("0", CultureInfo.InvariantCulture) + "%";
            Console.WriteLine($"  of ALL {all.Count} paths: no rest {Share(p => Of(p, "rest") == 0)} · no shop "
                + $"{Share(p => Of(p, "shop") == 0)} · 3+ elites {Share(p => Of(p, "elite") >= 3)} · "
                + $"3+ elites and no rest {Share(p => Of(p, "elite") >= 3 && Of(p, "rest") == 0)}");
            Console.WriteLine($"  shops on a path: median {Median(withPaths.SelectMany(s => s.Paths.Select(p => Of(p, "shop"))))}"
                + $" · seeds with a shopless lane: {withPaths.Count(s => s.Paths.Any(p => Of(p, "shop") == 0))}/{n}");
            if (placed.TryGetValue(act, out var outside) && outside.Count > 0)
                foreach (var (label, elite) in new[] { ("standard", false), ("elite", true) })
                {
                    var these = outside.Where(p => p.Elite == elite).Select(p => p.Outside).ToList();
                    if (these.Count > 0)
                        Console.WriteLine($"  staged {label} fights placed: {these.Count} · outside their stage: "
                            + $"{these.Count(d => d > 0)} (farthest {these.Max()} % of the act)");
                }
            if (realForks.TryGetValue(act, out var perRoute) && perRoute.Count > 0)
                Console.WriteLine($"  REAL DECISIONS per route (2+ ways on into different kinds of room): min {perRoute.Min()}"
                    + $" · median {Median(perRoute)} · routes under 4: {100.0 * perRoute.Count(r => r < 4) / perRoute.Count:0.0}%");
            if (restBeforeBoss.TryGetValue(act, out var rests) && rests.All > 0)
                Console.WriteLine($"  routes with a campfire right before the boss: {100.0 * rests.Yes / rests.All:0.0}%");
            Console.WriteLine($"  DECISIONS per map (rooms with 2+ ways on, the entry choice not counted): "
                + $"{forks.Where(f => f.Key.Act == act).Sum(f => f.Value) / (double)Math.Max(1, n):0.0}");
            Console.WriteLine("  forks per map by row (rooms with 2+ ways on): " + string.Join(" ",
                forks.Where(f => f.Key.Act == act).OrderBy(f => f.Key.Depth)
                    .Select(f => $"{f.Key.Depth}:{(double)f.Value / n:0.0}")));
        }
        return 0;
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
