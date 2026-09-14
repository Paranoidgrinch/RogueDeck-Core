using System.Text;

namespace RogueDeck.Run;

// WHAT ONE ROUTE THROUGH AN ACT ASKS OF A PLAYER — the promise that replaces the per-path role minimums.
//
// The rule-based generator keeps its side of the deal room by room: every route holds two elites, every route
// holds four fights. It pays for that in shape (a promise about every route needs a row every route crosses, so
// the act pinches shut once per promise) and it buys something oddly weak — a route with two elites and nine
// events satisfies it exactly as well as a route with two elites and nine fights, and those are not the same
// act to play.
//
// Path pressure says the useful thing instead: a route must carry enough CHALLENGE, wherever it comes from. An
// elite is worth more than a fight, a fight more than a campfire, and a route may reach the floor by any mixture
// it likes. No single role is promised anywhere, which is exactly why no funnel has to be inserted anywhere.
//
// TWO NUMBERS WITH TWO DIFFERENT STATUSES, and the difference is deliberate (plan S8):
//   Minimum  a PROMISE. A route below it is an act that can be walked round — the "skip every fight" route the
//            old per-path minimums existed to forbid. Reported as unkept here; S10's repair is what acts on it.
//   Maximum  a DIAGNOSTIC. A route above it is a spike, and a spike is a balance question rather than a
//            structural defect: whether it is too hard depends on the deck, the relics and the act, none of
//            which the generator can see. So it is measured and reported, and nothing is rejected for it yet.
//
// PRESSURE IS AUTHORED IN WHOLE POINTS, not in fractions. The plan's worked table reads Combat 1.0 /
// MultiCombat 1.5 / Elite 2.5; here that is 10 / 15 / 25 and a floor of 11 is a floor of 110. The ratios are the
// ones the design asked for and the arithmetic stays integer, for the reason StrategicRoomRules gives for its
// own percentages: a map is a contract with a seed, and a threshold a route clears on one machine and misses on
// another — because 1.1 is not 1.1 — is a bug nobody can reproduce.
public sealed record PathPressureRules
{
    // What a room of each role asks of the player, in points. Silence is zero: a campfire, a shop and a workbench
    // ask nothing, and that is the whole reason a route made of them is the route this measurement catches.
    public IReadOnlyDictionary<MapNodeKind, int> KindPressure { get; init; } = new Dictionary<MapNodeKind, int>
    {
        [MapNodeKind.Combat] = 10,
        [MapNodeKind.MultiCombat] = 15,
        [MapNodeKind.Elite] = 25,
    };

    // The floor every complete route must clear. Zero — the default — means the act promises nothing, and then
    // the measurement is pure diagnosis.
    public int Minimum { get; init; }

    // The ceiling no route should exceed. Unbounded by default, and never a reason to reject an act: see the
    // header on why this one is only ever reported.
    public int Maximum { get; init; } = int.MaxValue;

    public int PressureOf(MapNodeKind kind) => Math.Max(0, KindPressure.GetValueOrDefault(kind));

    // Whether this act promises anything at all. A floor nothing can be earned against is not a promise but a
    // contradiction, and it is caught in Validate rather than measured every seed.
    public bool Promises => Minimum > 0;

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(KindPressure);
        foreach (var (kind, pressure) in KindPressure)
            if (pressure < 0)
                throw new ArgumentOutOfRangeException(nameof(KindPressure), pressure,
                    $"A {kind} room cannot ask a negative amount of a player.");
        if (Minimum < 0)
            throw new ArgumentOutOfRangeException(nameof(Minimum), Minimum,
                "A route cannot be promised a negative amount of challenge.");
        if (Maximum < Minimum)
            throw new ArgumentOutOfRangeException(nameof(Maximum), Maximum,
                $"Path pressure is allowed at most {Maximum} and promised at least {Minimum}, so no route can be "
                + "both.");
        if (Minimum > 0 && !KindPressure.Any(entry => entry.Value > 0))
            throw new ArgumentException(
                $"Every route is promised {Minimum} points of pressure and no role is worth any, so the promise "
                + "cannot be kept by any act whatsoever.", nameof(KindPressure));
    }
}

// ONE EXTREME ROUTE, as the rooms it walks and what they are. The rooms are what a repair needs (S10 changes one
// of them), the roles are what a reader needs — "10 points" says nothing, "C C E R ? ?" says where they came
// from and where a swap would help.
public sealed record PressureRoute
{
    public required int Pressure { get; init; }
    public required IReadOnlyList<NodeId> Rooms { get; init; }
    public required IReadOnlyList<MapNodeKind> Kinds { get; init; }

    // The route in MapDiagnostics' one-letter alphabet, so a route reads the same here as in every grid.
    public string Letters => string.Concat(Kinds.Select(MapDiagnostics.Letter));

    public override string ToString() => $"{Pressure} · {Letters}";
}

// WHAT THE THINNEST AND THE RICHEST ROUTE OF ONE ACT ARE WORTH, against what the act promised and allowed.
public sealed record PathPressureReport
{
    public required PressureRoute Thinnest { get; init; }
    public required PressureRoute Richest { get; init; }
    public required int Demanded { get; init; }
    public required int Allowed { get; init; }

    // The promise: no route below the floor. The one bit S10's repair and S13's report start from.
    public bool Held => Thinnest.Pressure >= Demanded;

    // The diagnostic: no route above the ceiling. Never a reason to reject an act — see PathPressureRules.
    public bool WithinMaximum => Allowed == int.MaxValue || Richest.Pressure <= Allowed;

    public int Missing => Math.Max(0, Demanded - Thinnest.Pressure);
    public int Excess => Allowed == int.MaxValue ? 0 : Math.Max(0, Richest.Pressure - Allowed);

    // How far apart the two ends of the act are, as a percentage of the thinnest route — the number that says
    // whether choosing a route MEANS anything. A spread of 0 is an act whose branches are decoration, which is
    // precisely what §1 measured about v0.0.0. A thin route worth nothing has no ratio to give, and is reported
    // as 100 % rather than as infinity: the act it describes is already the worst case the FLOOR is there to
    // catch, and a report reads better than a division by zero.
    public int SpreadPercent => Thinnest.Pressure <= 0
        ? Richest.Pressure > 0 ? 100 : 0
        : (int)Math.Round((Richest.Pressure - Thinnest.Pressure) * 100d / Thinnest.Pressure);

    public string Render()
    {
        var text = new StringBuilder();
        text.Append("pressure ").Append(Thinnest.Pressure).Append("..").Append(Richest.Pressure)
            .Append(" (spread ").Append(SpreadPercent).Append(" %)")
            .Append(" · promised ").Append(Demanded)
            .Append(" · allowed ").Append(Allowed == int.MaxValue ? "∞" : Allowed.ToString())
            .Append(" · ").Append(Held ? "held" : $"SHORT by {Missing}");
        if (!WithinMaximum)
            text.Append(" · OVER by ").Append(Excess);
        text.AppendLine();
        text.Append("thinnest ").AppendLine(Thinnest.ToString());
        text.Append("richest  ").AppendLine(Richest.ToString());
        return text.ToString();
    }

    public override string ToString() => Render().TrimEnd();
}

// The measurement itself. Both overloads are the same O(V+E) reverse-topological pass through
// WeightedPathEvaluator, so a strategic act and a v0.0.0 map are scored by one implementation — which is what
// makes "today's worst route is worth 13" and "this act's worst route is worth 9" comparable numbers at all.
public static class StrategicPathPressure
{
    // An act the strategic generator planned: the topology it walks and the roles the allocator put in it.
    public static PathPressureReport Measure(StrategicRoomPlan plan, PathPressureRules rules)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return Measure(plan.Topology, id => plan.TryKindOf(id, out var kind) ? kind : MapNodeKind.Combat, rules);
    }

    // The same act with the rooms handed in separately — what S10's repair measures, because a repair holds a
    // scratch set of rooms it has not committed to a plan yet and must be able to ask what it would be worth.
    public static PathPressureReport Measure(
        StrategicTopology topology, Func<NodeId, MapNodeKind> kindOf, PathPressureRules rules)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(kindOf);
        ArgumentNullException.ThrowIfNull(rules);

        return Report(
            WeightedPathEvaluator.Score(
                topology.Slots.Select(slot => slot.Id).ToList(),
                topology.SuccessorsOf,
                topology.EntryIds,
                id => rules.PressureOf(kindOf(id))),
            kindOf,
            rules);
    }

    // A finished map from either generator — which is how v0.0.0's own pressure range gets measured rather than
    // assumed, and how the two generators can be held to one number.
    public static PathPressureReport Measure(GeneratedMap generated, PathPressureRules rules)
    {
        ArgumentNullException.ThrowIfNull(generated);
        return Measure(generated.Map, generated.Roles, rules);
    }

    public static PathPressureReport Measure(
        RunMap map, IReadOnlyDictionary<NodeId, MapNodeKind> roles, PathPressureRules rules)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(roles);
        ArgumentNullException.ThrowIfNull(rules);

        return Report(
            WeightedPathEvaluator.Score(
                map.Nodes.Select(node => node.Id).ToList(),
                map.SuccessorIds,
                WeightedPathEvaluator.EntryNodes(map).ToList(),
                id => roles.TryGetValue(id, out var kind) ? rules.PressureOf(kind) : 0d),
            id => roles.GetValueOrDefault(id, MapNodeKind.Combat),
            rules);
    }

    private static PathPressureReport Report(
        PathScores scores, Func<NodeId, MapNodeKind> kindOf, PathPressureRules rules) => new()
        {
            // Every weight is a whole number of points, so both sums are integers that happened to travel in a
            // double; rounding here is a cast with a proof behind it, not a tolerance.
            Thinnest = Route(scores.Minimum, scores.ThinnestRoute, kindOf),
            Richest = Route(scores.Maximum, scores.RichestRoute, kindOf),
            Demanded = rules.Minimum,
            Allowed = rules.Maximum,
        };

    private static PressureRoute Route(double pressure, IReadOnlyList<NodeId> rooms, Func<NodeId, MapNodeKind> kindOf) =>
        new()
        {
            Pressure = (int)Math.Round(pressure),
            Rooms = rooms,
            Kinds = rooms.Select(kindOf).ToList(),
        };
}
