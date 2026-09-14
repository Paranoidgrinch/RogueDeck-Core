using System.Text;

namespace RogueDeck.Run;

// WHETHER AN ACT CAN HOLD WHAT IT WAS AUTHORED TO HOLD — answered before a seed is spent finding out.
//
// S6's allocator reports what it could not do: a minimum it ran out of legal rooms for, a room it had to break a
// rule to fill. That is the right behaviour for bad LUCK, and the wrong place to discover bad ARITHMETIC. "Six
// elites, none before 60 % of the act's depth, in a twelve-row act two rooms wide" fails for every seed that will
// ever exist, and an author who learns this from a shortfall on seed 4 711 learns it as a mystery.
//
// So every check here is arithmetic on the authored numbers, and its two answers mean two different things:
//
//   IMPOSSIBLE  the WIDEST act this spec permits cannot satisfy it, so no seed can. A defect, not a risk.
//   TIGHT       the widest can and the NARROWEST cannot, so some seeds will report a shortfall and some will not.
//
// That is why a width is a range here rather than a number: the walk draws its opening width per seed and stays
// between `MinWidth` and `MaxWidth` for the whole act (see StrategicTopologyGenerator), so `rows × MaxWidth` and
// `rows × MinWidth` are both genuinely reachable and both honest bounds. Everything called impossible is measured
// against the generous end, everything called tight against the narrow one.
//
// Nothing here throws over a spec it dislikes — a report is the useful artefact, the same argument as
// StrategicTopology.Crossings and RoomShortfall — but ThrowIfImpossible is there for callers that want the gate,
// and S11's export validation is one.
public enum StrategicSpecSeverity
{
    // No seed can satisfy this.
    Impossible,

    // Some seeds will, some will not.
    Tight,
}

public sealed record StrategicSpecProblem
{
    public required StrategicSpecSeverity Severity { get; init; }

    // What the problem is ABOUT — a role, a band, or the act itself — so a report can be grouped by the number
    // the author would have to change.
    public required string Subject { get; init; }

    public required string Message { get; init; }

    public override string ToString() =>
        $"{(Severity is StrategicSpecSeverity.Impossible ? "impossible" : "tight")} · {Subject}: {Message}";
}

public sealed class StrategicSpecReport
{
    public StrategicSpecReport(StrategicActSpec spec, IReadOnlyList<StrategicSpecProblem> problems)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(problems);
        Spec = spec;
        Problems = problems;
    }

    public StrategicActSpec Spec { get; }
    public IReadOnlyList<StrategicSpecProblem> Problems { get; }

    // Whether a seed exists that could satisfy this act. The bit a generator should refuse to run on.
    public bool Possible => !Problems.Any(problem => problem.Severity is StrategicSpecSeverity.Impossible);

    // Whether EVERY act this spec can produce satisfies it. The bit S13's report wants, and the stronger claim.
    public bool Clean => Problems.Count == 0;

    public IEnumerable<StrategicSpecProblem> Of(StrategicSpecSeverity severity) =>
        Problems.Where(problem => problem.Severity == severity);

    public string Render()
    {
        var text = new StringBuilder();
        text.Append("rows ").Append(Spec.Rows)
            .Append(" · boss ").Append(Spec.BossRooms)
            .Append(" · width ").Append(Spec.MinWidth).Append("..").Append(Spec.MaxWidth)
            .Append(" · lanes ").Append(Spec.LaneProfiles.Count)
            .Append(" · rooms ").Append(Spec.RowsBeforeBoss * Spec.MinWidth)
            .Append("..").Append(Spec.RowsBeforeBoss * Spec.MaxWidth)
            .Append(Spec.PathPressure.Promises ? $" · pressure ≥ {Spec.PathPressure.Minimum}" : "")
            .Append(Spec.ForkQuality.MinimumContrast > 0 ? $" · forks ≥ {Spec.ForkQuality.MinimumContrast}" : "")
            .Append(" · ").AppendLine(Possible ? Clean ? "clean" : "possible" : "IMPOSSIBLE");
        foreach (var problem in Problems)
            text.AppendLine(problem.ToString());
        return text.ToString();
    }
}

public static class StrategicActSpecValidator
{
    public static StrategicSpecReport Validate(StrategicActSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var problems = new List<StrategicSpecProblem>();

        // A MALFORMED SPEC IS REPORTED AS AN IMPOSSIBLE ONE rather than allowed to throw past the caller. The
        // distinction the types draw — a negative width is a bug, six elites in a four-room act is a design
        // problem — is right, and it is not the caller's problem: a UI asking "is this act buildable" wants one
        // list of reasons, not one list plus an exception to catch.
        try
        {
            spec.Validate();
        }
        catch (ArgumentException problem)
        {
            problems.Add(new StrategicSpecProblem
            {
                Severity = StrategicSpecSeverity.Impossible,
                Subject = "the spec",
                Message = problem.Message.Split(" (Parameter")[0],
            });
            return new StrategicSpecReport(spec, problems);
        }

        Shape(spec, problems);
        Roles(spec, problems);
        ActBudgets(spec, problems);
        Bands(spec, problems);
        Pressure(spec, problems);
        Forks(spec, problems);

        return new StrategicSpecReport(spec, problems);
    }

    // The report's `Possible` bit as a gate. For callers — an export, a document validator — that have nowhere to
    // put a list of reasons and only need to refuse.
    public static void ThrowIfImpossible(StrategicActSpec spec)
    {
        var report = Validate(spec);
        if (report.Possible)
            return;
        throw new ArgumentException(
            "This act cannot be generated as authored:" + Environment.NewLine
            + string.Join(Environment.NewLine, report.Of(StrategicSpecSeverity.Impossible)),
            nameof(spec));
    }

    // THE ACT'S SHAPE. S4 already refuses the one case that cannot produce a topology at all; said here too,
    // because the point of a validator is that an author does not have to spend a seed to hear it.
    private static void Shape(StrategicActSpec spec, List<StrategicSpecProblem> problems)
    {
        var life = Math.Max(0, spec.Topology.MinBranchLifeRows);
        var rows = spec.RowsBeforeBoss;

        if (spec.MinWidth > 1 && rows > 0 && rows < life)
            problems.Add(Impossible("the act's length",
                $"{spec.Rows} rows ending in {spec.BossRooms} boss room(s) leave {rows} row(s) before the boss, "
                + $"too few for the {life} rows a branch must live when the act is always at least "
                + $"{spec.MinWidth} rooms wide."));

        // NO ROW OF THIS ACT MAY EVER FORK. A legal split needs `row + MinBranchLifeRows <= rowsBeforeBoss` and
        // the earliest split is row 1, so a short act is parallel corridors that only its boss joins — which is
        // exactly what S5 measured on 4-row acts. Not a defect, and not something to find out by reading a
        // picture either.
        if (spec.MaxWidth > spec.MinWidth && 1 + life > rows)
            problems.Add(Tight("the act's length",
                $"no row of a {spec.Rows}-row act can fork while a branch must live {life} rows, so the act is "
                + $"{spec.MinWidth}..{spec.MaxWidth} parallel corridors its boss joins and every fork weight is "
                + "inert."));

        if (rows == 0)
            problems.Add(Tight("the act's length",
                $"all {spec.Rows} row(s) are boss rooms, so the act holds no room the allocator chooses."));
    }

    // WHETHER A ROLE CAN BE PLACED AT ALL. Three traps, all of them silent in a generated act: nothing is
    // placeable, a role every route refuses, and a role nothing weights.
    private static void Roles(StrategicActSpec spec, List<StrategicSpecProblem> problems)
    {
        var rooms = spec.Rooms;
        var asked = Asked(spec).ToList();

        var placeable = asked.Count > 0
            || rooms.KindWeights.Any(entry => entry.Value > 0 && Placeable(entry.Key))
            || spec.LaneProfiles.Any(lane => lane.KindWeights.Any(entry => entry.Value > 0 && Placeable(entry.Key)));
        if (!placeable)
            problems.Add(Impossible("the act's roles",
                "no role carries a positive weight, a target or a minimum, so there is nothing to fill the act's "
                + "rooms with."));

        foreach (var kind in asked)
        {
            // REFUSED BY EVERY ROUTE. An authored 0 in a lane profile means "never on this route"; silence does
            // not. So a role every single profile authors at 0 cannot stand anywhere in the act, whichever
            // flavours a seed happens to use.
            var refused = spec.LaneProfiles.Count > 0 && spec.LaneProfiles.All(lane =>
                lane.KindWeights.TryGetValue(kind, out var weight) && weight <= 0);
            if (refused)
            {
                if (Minimums(spec, kind) > 0)
                    problems.Add(Impossible(kind.ToString(),
                        $"every one of the {spec.LaneProfiles.Count} lane profiles weights it 0, which means "
                        + "\"never on this route\", so no room of the act can hold the minimum it is promised."));
                else
                    problems.Add(Tight(kind.ToString(),
                        $"every one of the {spec.LaneProfiles.Count} lane profiles weights it 0, so its target "
                        + "can never be reached."));
                continue;
            }

            // WEIGHTED BY NOTHING. A target only bends a draw; it cannot create one. A role no lane names and the
            // act weights 0 is legal nowhere in the second pass, so only its MINIMUM ever places one — which
            // makes a target without a minimum a number that does nothing at all.
            if (Targets(spec, kind) > 0 && Minimums(spec, kind) == 0 && rooms.WeightOf(kind) == 0
                && spec.LaneProfiles.All(lane => !lane.KindWeights.TryGetValue(kind, out var weight) || weight <= 0))
                problems.Add(Impossible(kind.ToString(),
                    "nothing weights it — not the act, not one lane profile — so no room is ever drawn for it and "
                    + "its target cannot be approached. Give it a weight, or a minimum."));
        }

        // A role that may both repeat freely and never repeat. The ban wins (it is a filter, the other is a
        // penalty that is skipped), so this is confusing rather than broken — and worth saying once.
        foreach (var kind in spec.Rooms.Rules.RepeatFreelyKinds.Intersect(spec.Rooms.Rules.NoRepeatKinds))
            problems.Add(Tight(kind.ToString(),
                "it is listed both as repeating freely and as never repeating. The ban wins, because it is a "
                + "filter and the other is a penalty that is then never consulted."));
    }

    // THE ACT'S OWN COUNTS, against the rooms an act of this shape can have and the rooms a depth gate leaves.
    private static void ActBudgets(StrategicActSpec spec, List<StrategicSpecProblem> problems)
    {
        var depths = spec.RowDepthPercents;
        var minimums = 0;

        foreach (var (kind, budget) in spec.Rooms.RoomBudgets.OrderBy(entry => (int)entry.Key))
        {
            minimums += budget.Min;
            if (budget.Min == 0 && budget.Target == 0)
                continue;

            var gate = spec.Rooms.EarliestDepthOf(kind);
            var rows = depths.Count(depth => depth >= gate);
            if (rows == 0)
            {
                problems.Add(Problem(budget.Min > 0, kind.ToString(),
                    $"it may not stand before {gate} % of the act's depth, and no row of a {spec.Rows}-row act is "
                    + $"that deep{Depths(depths)}."));
                continue;
            }

            Capacity(problems, kind.ToString(), budget.Min, rows, spec,
                $"only {{0}} room(s) of a {spec.Rows}-row act are as deep as {gate} %");
        }

        if (minimums > 0)
            Capacity(problems, "the act's minimums", minimums, depths.Count, spec,
                $"a {spec.Rows}-row act has {{0}} room(s) that hold a chosen role");
    }

    // THE BANDS. Everything here is a statement about one slice of the act's depth, measured against the rows
    // that actually fall in it — which, for a short act or a coarse band, can be none.
    private static void Bands(StrategicActSpec spec, List<StrategicSpecProblem> problems)
    {
        var rooms = spec.Rooms;
        var depths = spec.RowDepthPercents;
        var banded = 0;

        foreach (var band in rooms.DepthBands)
        {
            var subject = $"the {band.Label} band";
            var rows = depths.Count(band.Contains);
            banded += rows;

            var asks = band.Budgets.Any(entry => entry.Value.Min > 0 || entry.Value.Target > 0);
            if (rows == 0)
            {
                if (asks)
                    problems.Add(Problem(band.Budgets.Any(entry => entry.Value.Min > 0), subject,
                        $"no row of a {spec.Rows}-row act sits at a depth inside it{Depths(depths)}."));
                continue;
            }

            var minimums = 0;
            foreach (var (kind, budget) in band.Budgets.OrderBy(entry => (int)entry.Key))
            {
                minimums += budget.Min;
                var act = rooms.BudgetOf(kind);

                // A BAND NEVER OVERRIDES A DEPTH GATE (source document §14), which is the document's own example:
                // a role earliest at 35 % cannot stand in a 0-25 % band however loudly that band asks.
                var gate = rooms.EarliestDepthOf(kind);
                var eligible = depths.Count(depth => band.Contains(depth) && depth >= gate);
                if (eligible == 0 && (budget.Min > 0 || budget.Target > 0))
                {
                    problems.Add(Problem(budget.Min > 0, $"{kind} in {subject}",
                        $"it may not stand before {gate} % of the act's depth, so no room of this band can hold "
                        + "one — a band asks within the gates, it does not lift them."));
                    continue;
                }

                if (budget.Min > 0 && eligible > 0)
                    Capacity(problems, $"{kind} in {subject}", budget.Min, eligible, spec,
                        $"only {{0}} row(s) of this band are as deep as {gate} %");

                // A band that demands more than the act allows in total. Two separate contradictions: this one
                // band against the act's ceiling, and all the bands together against it.
                if (act is not null && budget.Min > act.Max)
                    problems.Add(Impossible($"{kind} in {subject}",
                        $"this band demands at least {budget.Min} while the act allows at most {act.Max} in all."));
            }

            if (minimums > 0)
                Capacity(problems, subject, minimums, rows, spec,
                    $"this band covers {rows} row(s) of a {spec.Rows}-row act");
        }

        foreach (var kind in Asked(spec))
        {
            var act = rooms.BudgetOf(kind);
            if (act is null)
                continue;

            var demanded = rooms.DepthBands.Sum(band => band.BudgetOf(kind)?.Min ?? 0);
            if (demanded > act.Max)
                problems.Add(Impossible(kind.ToString(),
                    $"the depth bands together demand at least {demanded} while the act allows at most {act.Max}."));

            // THE BANDS AS A CEILING ON THE ACT — only once they cover every row, because a room in no band is a
            // room no band limits, and then the bands' ceilings say nothing about the act's total.
            if (banded < depths.Count || act.Min == 0)
                continue;
            var ceilings = rooms.DepthBands
                .Select(band => band.BudgetOf(kind)?.Max ?? int.MaxValue)
                .ToList();
            if (ceilings.Any(ceiling => ceiling == int.MaxValue))
                continue;
            var allowed = ceilings.Sum();
            if (allowed < act.Min)
                problems.Add(Impossible(kind.ToString(),
                    $"the depth bands cover the whole act and allow at most {allowed} between them, while the act "
                    + $"demands at least {act.Min}."));
        }
    }

    // THE FLOOR OF CHALLENGE EVERY ROUTE IS PROMISED, against the very best route the spec could ever permit.
    //
    // A route walks exactly one room per row, so the most it can possibly carry is: the highest-pressure role
    // that is legal in each row, with no role used more often than the act's own ceiling allows it to exist at
    // all. Both of those are hard filters in the allocator, so this really is an upper bound, and a floor above
    // it is a floor no seed can reach — IMPOSSIBLE, by the same standard as every other check here.
    //
    // There is deliberately no TIGHT companion. A LOWER bound on a route's pressure is not arithmetic on the
    // authored numbers: whether the thin route through an act clears the floor depends on where the seed put the
    // elites, which is precisely why the floor is checked on the finished plan (StrategicPathPressure) and
    // repaired there (S10). Guessing at it here would be a warning an author could neither trust nor act on.
    private static void Pressure(StrategicActSpec spec, List<StrategicSpecProblem> problems)
    {
        var rules = spec.PathPressure;
        if (!rules.Promises)
            return;

        var bossRooms = Math.Max(0, spec.BossRooms);
        var best = bossRooms * rules.PressureOf(MapNodeKind.Boss);
        var free = spec.RowDepthPercents.Order().ToList();

        // Every copy of every role that could stand in this act, richest first, each carrying the depth it may
        // first appear at. A role nothing weights and no minimum demands is not among them: it is never drawn,
        // so counting on it would make this bound worthless.
        var copies = Placements(spec, rules, free.Count)
            .OrderByDescending(copy => copy.Pressure)
            .ThenByDescending(copy => copy.Gate)
            .ThenBy(copy => (int)copy.Kind)
            .ToList();

        // Each copy takes the SHALLOWEST row it is allowed in, which keeps the deep rows free for the roles that
        // are gated out of everywhere else — the assignment that maximizes the total when, as here, one role's
        // legal rows are always a suffix of another's.
        foreach (var copy in copies)
        {
            var row = free.FindIndex(depth => depth >= copy.Gate);
            if (row < 0)
                continue;
            free.RemoveAt(row);
            best += copy.Pressure;
        }

        if (best >= rules.Minimum)
            return;

        problems.Add(Impossible("the act's path pressure",
            $"every route is promised {rules.Minimum} point(s) of challenge, and the richest route a "
            + $"{spec.Rows}-row act can hold is worth {best} — one room per row, each the most demanding role "
            + "its depth gate and the act's own ceiling allow."));
    }

    // One placeable room of one role, as many times over as the act's ceiling permits it to exist. Roles worth
    // no pressure are left out: they cannot raise a maximum.
    private static IEnumerable<(MapNodeKind Kind, int Pressure, int Gate)> Placements(
        StrategicActSpec spec, PathPressureRules rules, int rows)
    {
        foreach (var (kind, pressure) in rules.KindPressure.OrderBy(entry => (int)entry.Key))
        {
            if (pressure <= 0 || !CanAppear(spec, kind))
                continue;

            var budget = spec.Rooms.BudgetOf(kind);
            var ceiling = budget is null ? rows : Math.Min(rows, budget.Max);
            var gate = spec.Rooms.EarliestDepthOf(kind);
            for (var copy = 0; copy < ceiling; copy++)
                yield return (kind, pressure, gate);
        }
    }

    // WHETHER A ROLE CAN STAND ANYWHERE IN THIS ACT AT ALL. An authored 0 in every lane profile means "never on
    // this route" everywhere; nothing weighting it and no minimum demanding it means it is never drawn; a
    // ceiling of nought means it may not exist. Shared by every check that needs to know what the act can be
    // made of, because three answers to that question would be three chances to disagree.
    private static bool CanAppear(StrategicActSpec spec, MapNodeKind kind)
    {
        if (!Placeable(kind))
            return false;
        if (spec.Rooms.BudgetOf(kind) is { Max: <= 0 })
            return false;

        var refused = spec.LaneProfiles.Count > 0 && spec.LaneProfiles.All(lane =>
            lane.KindWeights.TryGetValue(kind, out var weight) && weight <= 0);
        var drawn = spec.Rooms.WeightOf(kind) > 0
            || spec.LaneProfiles.Any(lane => lane.KindWeights.TryGetValue(kind, out var weight) && weight > 0);
        return !refused && (drawn || Minimums(spec, kind) > 0);
    }

    // THE CONTRAST THE ACT ASKS OF ITS FORKS, against the sharpest fork its own roles could ever draw.
    //
    // Two things can be said before a seed is spent. An act that cannot fork at all — because it is too short
    // for a branch to live its minimum, or because it is never allowed to widen — is an act whose fork threshold
    // is a sentence about nothing. And a threshold above the widest gap between any two of the act's roles,
    // repeated for every row of the horizon, is a threshold no arrangement of those roles can reach.
    //
    // The second is an upper bound and knowingly a generous one: it assumes every row of the horizon is the
    // act's sharpest possible pairing, which no real act manages. That is the right direction for a check that
    // only ever says IMPOSSIBLE — everything it passes may still be hard, and S9 reports how hard per seed.
    private static void Forks(StrategicActSpec spec, List<StrategicSpecProblem> problems)
    {
        var rules = spec.ForkQuality;
        if (rules.MinimumContrast <= 0)
            return;

        var life = Math.Max(0, spec.Topology.MinBranchLifeRows);
        if (spec.MaxWidth <= 1 || (spec.MaxWidth > spec.MinWidth && 1 + life > spec.RowsBeforeBoss))
        {
            problems.Add(Tight("the act's forks",
                $"it asks a contrast of {rules.MinimumContrast} of its forks, and no row of a {spec.Rows}-row "
                + "act this shape can fork, so the threshold is inert."));
            return;
        }

        var kinds = Enum.GetValues<MapNodeKind>()
            .Where(kind => kind is MapNodeKind.Boss || CanAppear(spec, kind))
            .ToList();
        var sharpest = 0;
        foreach (var left in kinds)
            foreach (var right in kinds)
                sharpest = Math.Max(sharpest, rules.Distance(left, right));

        var reachable = sharpest * rules.HorizonRows;
        if (rules.MinimumContrast <= reachable)
            return;

        problems.Add(Impossible("the act's forks",
            $"it asks a contrast of {rules.MinimumContrast} of its forks, and the sharpest pair of roles this "
            + $"act can hold differs by {sharpest} in a row, so {rules.HorizonRows} row(s) of decision horizon "
            + $"cannot tell two futures further apart than {reachable}."));
    }


    // ONE CAPACITY CLAIM, measured against both ends of the width range — which is where the two severities come
    // from. `shape` is the sentence about the rooms available, with {0} for the row count.
    private static void Capacity(
        List<StrategicSpecProblem> problems,
        string subject,
        int demanded,
        int rows,
        StrategicActSpec spec,
        string shape)
    {
        if (demanded <= rows * spec.MinWidth)
            return;
        var widest = rows * spec.MaxWidth;
        problems.Add(Problem(demanded > widest, subject,
            $"it demands {demanded}, and " + string.Format(shape, rows) + ", which holds "
            + $"{rows * spec.MinWidth} room(s) at the act's narrowest and {widest} at its widest."));
    }

    // Every role any budget in this act says a positive word about — the act's own or one band's.
    private static IEnumerable<MapNodeKind> Asked(StrategicActSpec spec) => spec.Rooms.RoomBudgets
        .Concat(spec.Rooms.DepthBands.SelectMany(band => band.Budgets))
        .Where(entry => entry.Value.Min > 0 || entry.Value.Target > 0)
        .Select(entry => entry.Key)
        .Where(Placeable)
        .Distinct()
        .OrderBy(kind => (int)kind);

    private static int Minimums(StrategicActSpec spec, MapNodeKind kind) => Math.Max(
        spec.Rooms.BudgetOf(kind)?.Min ?? 0,
        spec.Rooms.DepthBands.Select(band => band.BudgetOf(kind)?.Min ?? 0).DefaultIfEmpty(0).Max());

    private static int Targets(StrategicActSpec spec, MapNodeKind kind) => Math.Max(
        spec.Rooms.BudgetOf(kind)?.Target ?? 0,
        spec.Rooms.DepthBands.Select(band => band.BudgetOf(kind)?.Target ?? 0).DefaultIfEmpty(0).Max());

    // A Boss belongs to the topology and a Mimic is a realized Treasure; neither is ever a placed role.
    private static bool Placeable(MapNodeKind kind) => kind is not (MapNodeKind.Boss or MapNodeKind.Mimic);

    // The depths an act's rows actually sit at, listed when the list is short enough to be the answer — which is
    // precisely when it usually is, because a band holding no row is a short-act problem.
    private static string Depths(IReadOnlyList<int> depths)
    {
        var distinct = depths.Distinct().Order().ToList();
        return distinct.Count is 0 or > 10 ? "" : $" (its rows sit at {string.Join(" / ", distinct)} %)";
    }

    private static StrategicSpecProblem Problem(bool impossible, string subject, string message) =>
        impossible ? Impossible(subject, message) : Tight(subject, message);

    private static StrategicSpecProblem Impossible(string subject, string message) => new()
    {
        Severity = StrategicSpecSeverity.Impossible,
        Subject = subject,
        Message = message,
    };

    private static StrategicSpecProblem Tight(string subject, string message) => new()
    {
        Severity = StrategicSpecSeverity.Tight,
        Subject = subject,
        Message = message,
    };
}
