using System.Text;

namespace RogueDeck.Run;

// ONE ACT, FROM AN AUTHORED SPEC AND A SEED (map rework S10).
//
// The four stages have been callable separately since S4, which is right for building them and wrong for using
// them: a caller that has to remember to hand the strand assigner the STRANDS stream, and the allocator the
// ROOMS stream, is a caller that will eventually hand it the same one twice. This is the pipeline — shape,
// flavour, rooms, repair — with the seeds derived in one place and the retries bounded.
//
// WHAT "FAILING" MEANS IS THE AUTHOR'S CHOICE, not the generator's. An act that promises nothing — no budget
// minimums, no floor of challenge, no fork threshold — cannot fail, so the defaults never throw. Everything that
// can go unkept here is something somebody wrote down as a promise, and the source document is explicit that a
// broken promise must be LOUD (§25: "preferable to silently degrading the map"). A map that quietly drops the
// elites it promised is a bug report nobody can write, arriving weeks later as "the run felt easy".
//
// So: up to `MaxGenerationAttempts` whole acts, each from `MapSeedStreams.Attempt(n)` — a new family of streams
// derived from the same run seed, never from ambient randomness — and if none of them keeps the act's promises,
// an exception carrying the best attempt and the six facts §25 asks for.
public static class StrategicMapGenerator
{
    public static GeneratedAct Generate(StrategicActSpec spec, int seed)
    {
        ArgumentNullException.ThrowIfNull(spec);
        spec.Validate();

        // A SPEC NO SEED CAN SATISFY IS SAID SO IMMEDIATELY, rather than discovered eight attempts later as a
        // run of bad luck. The validator's own words go into the exception: "six elites in a twelve-row act two
        // rooms wide" is the answer, and "eight attempts failed" is not.
        var impossible = StrategicActSpecValidator.Validate(spec)
            .Of(StrategicSpecSeverity.Impossible)
            .ToList();
        if (impossible.Count > 0)
            throw new StrategicMapGenerationException(seed, attempts: 0, best: null, impossible);

        GeneratedAct? best = null;
        var attempts = Math.Max(1, spec.MaxGenerationAttempts);
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var act = Attempt(spec, seed, attempt);
            if (act.Clean)
                return act;
            if (best is null || act.Defects < best.Defects)
                best = act;
        }

        throw new StrategicMapGenerationException(seed, attempts, best, []);
    }

    // One whole act from one family of streams. Public because a caller with a reason to look at an act that was
    // not good enough — a test, S13's report — should not have to provoke an exception to get one.
    public static GeneratedAct Attempt(StrategicActSpec spec, int seed, int attempt)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentOutOfRangeException.ThrowIfNegative(attempt);
        spec.Validate();

        var streams = MapSeedStreams.From(seed).Attempt(attempt);
        var topology = StrategicTopologyGenerator.Generate(
            streams.Topology, spec.Rows, spec.BossRooms, spec.MinWidth, spec.MaxWidth, spec.Topology);
        var profiles = StrategicStrandProfileAssigner.Assign(
            streams.Strands, topology, spec.LaneProfiles, spec.LaneAssignment);
        var allocated = StrategicRoomAllocator.Allocate(streams.Rooms, profiles, spec.Rooms);
        var repaired = MapRepair.Repair(allocated, spec.PathPressure, spec.ForkQuality, spec.Repair);

        return new GeneratedAct
        {
            Seed = seed,
            Attempt = attempt,
            Spec = spec,
            Plan = repaired.Plan,
            Repairs = repaired.Operations,
            Defects = repaired.After,
            Pressure = StrategicPathPressure.Measure(repaired.Plan, spec.PathPressure),
            Forks = ForkQualityEvaluator.Measure(repaired.Plan, spec.ForkQuality),
        };
    }
}

// EVERYTHING ONE SEED PRODUCED, and what it is worth. The rooms are in `Plan`; the rest is the evidence that
// they are the rooms the act asked for — which is what S13's seed report is made of, and what an exception
// carries when they are not.
public sealed record GeneratedAct
{
    public required int Seed { get; init; }

    // Which whole-act retry this is. Zero is the act the seed asks for; anything else means the earlier ones
    // could not keep the act's promises, and that is a tuning fact about the SPEC, not about the seed.
    public required int Attempt { get; init; }

    public required StrategicActSpec Spec { get; init; }
    public required StrategicRoomPlan Plan { get; init; }
    public required IReadOnlyList<RepairOperation> Repairs { get; init; }
    public required MapDefects Defects { get; init; }
    public required PathPressureReport Pressure { get; init; }
    public required ForkQualityReport Forks { get; init; }

    public StrategicTopology Topology => Plan.Topology;
    public StrategicStrandProfiles Profiles => Plan.Profiles;

    public bool Clean => Defects.None;

    // The act in eight lines: what it is, what it holds, what it asks of a route, what its forks are worth, and
    // what had to be moved to get there.
    public string Render()
    {
        var text = new StringBuilder();
        text.Append("seed ").Append(Seed)
            .Append(" · attempt ").Append(Attempt)
            .Append(" · rows ").Append(Spec.Rows)
            .Append(" · rooms ").Append(Plan.Kinds.Count)
            .Append(" · repairs ").Append(Repairs.Count)
            .Append(" · ").AppendLine(Clean ? "clean" : Defects.ToString());
        text.AppendLine(Plan.Budgets());
        foreach (var shortfall in Plan.Shortfalls)
            text.Append("shortfall ").AppendLine(shortfall.ToString());
        foreach (var forced in Plan.Forced)
            text.Append("forced ").AppendLine(forced.ToString());
        text.Append(Pressure.Render());
        text.Append("forks ").Append(Forks.Count)
            .Append(" · contrast ").Append(Forks.Minimum).Append("..").Append(Forks.Maximum)
            .Append(" (mean ").Append(Forks.Mean).Append(") · hollow ").Append(Forks.Hollow).AppendLine();
        foreach (var repair in Repairs)
            text.Append("repair ").AppendLine(repair.ToString());
        return text.ToString();
    }

    public override string ToString() => Render().TrimEnd();
}

// AN ACT THAT COULD NOT BE BUILT AS AUTHORED, with everything needed to say why (source document §25): the
// seed, how many whole acts were tried, which promises went unkept, what the act held, what its thinnest route
// was worth and what its weakest fork was.
public sealed class StrategicMapGenerationException : Exception
{
    public StrategicMapGenerationException(
        int seed, int attempts, GeneratedAct? best, IReadOnlyList<StrategicSpecProblem> impossible)
        : base(Describe(seed, attempts, best, impossible))
    {
        Seed = seed;
        Attempts = attempts;
        Best = best;
        Impossible = impossible;
    }

    public int Seed { get; }
    public int Attempts { get; }

    // The least unsatisfactory act of the lot — null when the spec was refused before any was built. Carried so
    // a caller that would rather ship a flawed map than no map at all can make that choice knowingly.
    public GeneratedAct? Best { get; }

    public IReadOnlyList<StrategicSpecProblem> Impossible { get; }

    private static string Describe(
        int seed, int attempts, GeneratedAct? best, IReadOnlyList<StrategicSpecProblem> impossible)
    {
        var text = new StringBuilder();
        if (impossible.Count > 0)
        {
            text.AppendLine($"No seed can build this act as authored (seed {seed} was asked for):");
            foreach (var problem in impossible)
                text.Append("  ").AppendLine(problem.ToString());
            return text.ToString().TrimEnd();
        }

        text.AppendLine($"Seed {seed} could not build this act in {attempts} attempt(s) without breaking a "
            + "promise it was authored with. The closest attempt:");
        foreach (var line in (best?.Render() ?? "nothing was built").TrimEnd().Split(Environment.NewLine))
            text.Append("  ").AppendLine(line);
        return text.ToString().TrimEnd();
    }
}
