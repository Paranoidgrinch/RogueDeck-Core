namespace RogueDeck.Run;

// WHAT AN ACT'S RULES PRODUCE OVER A THOUSAND SEEDS, as opposed to over the one seed somebody happened to look
// at (map rework S13).
//
// Every number this arc has authored — a budget, a depth band, a pressure floor, a fork threshold — is a claim
// about a DISTRIBUTION, and up to here each has been checked one act at a time. That is enough to catch a rule
// that never holds and useless against the ones that hold on nine seeds in ten: a floor the repair only just
// reaches, a band that quietly empties on wide acts, a contrast threshold that costs a whole-act retry once in
// fifty. Those are tuning facts, they are invisible on any single map, and they are what this measures.
//
// TWO KINDS OF NUMBER, AND THEY ARE NOT READ THE SAME WAY (source document §50). There is exactly one HARD
// constraint and it is the act's own promise: the seed produced an act with no defect left on it. That may fail
// a build. Every other number here is QUALITY — how wide, how forked, how much trouble, how much repairing —
// and none of it may fail anything, because a quality number that fails a build is a number nobody dares tune.
// It is printed, and a human decides.
//
// The hard constraint is one bit rather than two because the generator makes it one: an act it cannot get clean
// is never handed back, it is REFUSED, with the least unsatisfactory attempt attached (§25). So a seed either
// produced a clean act or produced nothing, and what a refusal is worth reading for is how close it came.
//
// AND EVERY EXTREME CARRIES ITS SEED. "The contrast fell to zero somewhere in a thousand acts" is not something
// anyone can act on; "it fell to zero on seed 8231" is a map you can open with the dump probe.
public sealed class ActMeasure(string name)
{
    private long _total;

    public string Name { get; } = name;
    public int Count { get; private set; }
    public int Least { get; private set; } = int.MaxValue;
    public int Most { get; private set; } = int.MinValue;

    // The seed at each end — the whole point of measuring rather than averaging.
    public int LeastSeed { get; private set; }
    public int MostSeed { get; private set; }

    // The mean in TENTHS, and integer throughout. A report is read next to the numbers it was produced from,
    // and a mean whose last digit depends on the order the doubles were added is a number two machines disagree
    // about for no reason anybody can see (the same argument StrategicRoomRules makes about percentages).
    public int MeanTenths => Count == 0 ? 0 : (int)((_total * 10 + Count / 2) / Count);

    public void Add(int seed, int value)
    {
        Count++;
        _total += value;
        if (value < Least)
        {
            Least = value;
            LeastSeed = seed;
        }
        if (value > Most)
        {
            Most = value;
            MostSeed = seed;
        }
    }

    public string Render() => Count == 0
        ? $"{Name,-22} —"
        : $"{Name,-22} {Least,6}..{Most,-6} mean {MeanTenths / 10,4}.{MeanTenths % 10}"
            + $"   (least on seed {LeastSeed}, most on seed {MostSeed})";

    public override string ToString() => Render();
}

// One act's rules, measured. `Sound` is the only bit a build may be failed on.
public sealed record ActStatistics
{
    public required StrategicActSpec Spec { get; init; }
    public required int FirstSeed { get; init; }
    public required int Seeds { get; init; }

    // THE HARD CONSTRAINT, as the seeds that broke it rather than as a count — a count tells you there is a
    // problem and a seed tells you what it is, and the defects say whether the spec is slightly tight or wrong.
    public required IReadOnlyList<RefusedAct> Refused { get; init; }

    // Not a defect: a whole-act retry is the mechanism working. It is here because a spec that needs one often
    // is a spec that is tighter than its author thinks (see StrategicActSpec.MaxGenerationAttempts).
    public required IReadOnlyList<int> Regenerated { get; init; }

    public required IReadOnlyList<ActMeasure> Measures { get; init; }
    public required IReadOnlyDictionary<MapNodeKind, ActMeasure> Rooms { get; init; }

    public bool Sound => Refused.Count == 0;

    public string Render()
    {
        var lines = new List<string>
        {
            $"{Seeds} acts from seed {FirstSeed} — {Spec.Rows} rows, boss {Spec.BossRooms}, "
            + $"width {Spec.MinWidth}..{Spec.MaxWidth}, {Spec.LaneProfiles.Count} lanes",
            Sound
                ? $"  sound · all {Seeds} generated clean"
                : $"  NOT SOUND · {Refused.Count} of {Seeds} acts could not be generated clean",
        };
        foreach (var refusal in Refused.Take(3))
            lines.Add($"  refused: {refusal}");
        if (Refused.Count > 3)
            lines.Add($"  …and {Refused.Count - 3} more refusal(s)");
        lines.Add(Regenerated.Count == 0
            ? "  no act needed a second attempt"
            : $"  {Regenerated.Count} act(s) needed a whole-act retry (first: seed {Regenerated[0]})");

        lines.Add("  — shape and quality, for reading, never for failing —");
        foreach (var measure in Measures)
            lines.Add($"    {measure.Render()}{Coverage(measure)}");
        lines.Add("  — rooms —");
        foreach (var (kind, measure) in Rooms.OrderBy(entry => entry.Key.ToString(), StringComparer.Ordinal))
            lines.Add($"    {measure.Render()}  (budget {Budget(kind)})");
        return string.Join('\n', lines) + '\n';
    }

    // A measure that saw fewer acts than were generated says so. An act with no fork at all has nothing to say
    // about its forks, and averaging that silence in as a zero is how a report comes to claim an act's weakest
    // fork is worthless when the act simply has none.
    private string Coverage(ActMeasure measure) =>
        measure.Count == Seeds - Refused.Count ? "" : $"  [over {measure.Count} act(s)]";

    private string Budget(MapNodeKind kind) => Spec.Rooms.BudgetOf(kind) is { } budget
        ? $"{budget.Min}..{(budget.Max == int.MaxValue ? "∞" : budget.Max.ToString())}, aims at {budget.Target}"
        : "none — the filler";

    public override string ToString() => Render().TrimEnd();
}

// A seed the generator would not produce a clean act for, and how close it came: the defects are the least
// unsatisfactory attempt's, so "missing 5 points of pressure" and "three rooms short of the shop budget" are
// different tuning problems and read as different problems here.
public sealed record RefusedAct(int Seed, int Attempts, MapDefects? Best, string Why)
{
    public override string ToString() => Attempts == 0
        ? $"seed {Seed} — the spec itself: {Why}"
        : $"seed {Seed} after {Attempts} attempt(s) — {Why}"
            + (Best is { } defects ? $", best was {defects}" : "");
}

public static class StrategicActStatistics
{
    // Generate `seeds` consecutive acts from one spec and report what they came out like. Deliberately a plain
    // loop and not a parallel one: the generator is deterministic per seed, so the only thing threads would buy
    // is a report whose extremes depend on which thread won a race.
    public static ActStatistics Measure(StrategicActSpec spec, int firstSeed = 1, int seeds = 1000)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seeds);

        var refused = new List<RefusedAct>();
        var regenerated = new List<int>();

        var rooms = new ActMeasure("rooms");
        var widest = new ActMeasure("widest row");
        var narrowest = new ActMeasure("narrowest row");
        var chosenRows = new Func<StrategicTopology, IEnumerable<StrategicRow>>(topology =>
            topology.Rows.Where(row => !row.Slots.Any(slot => slot.IsBoss)));
        var forks = new ActMeasure("forks");
        var merges = new ActMeasure("merges");
        var crossings = new ActMeasure("crossing edges");
        var strands = new ActMeasure("strands");
        var branchLife = new ActMeasure("shortest branch");
        var repairs = new ActMeasure("repair operations");
        var attempts = new ActMeasure("attempts");
        var thin = new ActMeasure("thinnest route");
        var rich = new ActMeasure("richest route");
        var spread = new ActMeasure("route spread %");
        var weakestFork = new ActMeasure("weakest fork");
        var meanFork = new ActMeasure("mean fork");
        var hollow = new ActMeasure("hollow forks");
        // EVERY ROLE THE ACT COULD PLACE, decided before the first seed rather than discovered along the way: a
        // role first seen on the five-hundredth act would have a "least" taken over half the sample and would
        // quietly never report the zero it had on the other half.
        var kinds = new SortedSet<MapNodeKind>(
            spec.Rooms.KindWeights.Keys
                .Concat(spec.Rooms.RoomBudgets.Keys)
                .Concat(spec.Rooms.DepthBands.SelectMany(band => band.Budgets.Keys))
                .Append(MapNodeKind.Boss));
        var byKind = kinds.ToDictionary(kind => kind, kind => new ActMeasure(kind.ToString()));

        for (var index = 0; index < seeds; index++)
        {
            var seed = firstSeed + index;
            GeneratedAct act;
            try
            {
                act = StrategicMapGenerator.Generate(spec, seed);
            }
            catch (StrategicMapGenerationException refusal)
            {
                refused.Add(new RefusedAct(
                    seed, refusal.Attempts, refusal.Best?.Defects,
                    refusal.Impossible.Count > 0
                        ? string.Join("; ", refusal.Impossible.Select(problem => problem.ToString()))
                        : "no attempt came out clean"));
                continue;
            }

            if (act.Attempt > 0)
                regenerated.Add(seed);

            var topology = act.Topology;
            rooms.Add(seed, topology.NodeCount);
            // THE BOSS ROWS ARE NOT PART OF THE ACT'S WIDTH. An act ends on one room however wide it ran, so a
            // narrowest-row measure that counted them read 1..1 on every act ever generated — a line that is
            // always true, says nothing, and hides the number it was written to show.
            var widths = chosenRows(topology).Select(row => row.Width).ToList();
            widest.Add(seed, widths.Count == 0 ? 0 : widths.Max());
            narrowest.Add(seed, widths.Count == 0 ? 0 : widths.Min());
            forks.Add(seed, topology.Forks);
            merges.Add(seed, topology.Merges);
            crossings.Add(seed, topology.Crossings);
            strands.Add(seed, topology.Strands.Count);
            // The SHORTEST branch in the act, not the average one: a topology rule that says a split must live
            // three rows is kept or broken by its worst case, and an average would hide the one that broke it.
            branchLife.Add(seed, topology.Strands.Min(strand => strand.LifeRows));
            repairs.Add(seed, act.Repairs.Count);
            attempts.Add(seed, act.Attempt + 1);
            thin.Add(seed, act.Pressure.Thinnest.Pressure);
            rich.Add(seed, act.Pressure.Richest.Pressure);
            spread.Add(seed, act.Pressure.SpreadPercent);
            if (act.Forks.Count > 0)
            {
                weakestFork.Add(seed, act.Forks.Minimum);
                meanFork.Add(seed, act.Forks.Mean);
                hollow.Add(seed, act.Forks.Hollow);
            }

            foreach (var (kind, measure) in byKind)
                measure.Add(seed, act.Plan.Count(kind));
        }

        return new ActStatistics
        {
            Spec = spec,
            FirstSeed = firstSeed,
            Seeds = seeds,
            Refused = refused,
            Regenerated = regenerated,
            Measures =
            [
                rooms, widest, narrowest, forks, merges, crossings, strands, branchLife,
                attempts, repairs, thin, rich, spread, weakestFork, meanFork, hollow,
            ],
            Rooms = byKind,
        };
    }
}
