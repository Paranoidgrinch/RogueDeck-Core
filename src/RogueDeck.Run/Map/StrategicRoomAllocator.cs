namespace RogueDeck.Run;

// WHICH ROOM STANDS WHERE, decided after the act's shape and its routes' flavours are both already settled.
//
// The rule-based generator answers this per column, independently, from one table — and then repairs what the
// independence produced: gate funnels to keep the per-path minimums, and a rewrite of "the deepest offending
// nodes" into Combat to keep the ceilings. Both repairs are invisible in the spec and both cost the act
// something it was authored to have.
//
// Here the decision is a SCORE over the roles a room may legally hold, in the order the source document's
// §16 asks for — the most constrained roles first, the flexible ones after:
//
//   Score(kind, room) = RouteWeight × ActBudgetNeed × BandBudgetNeed × LocalDiversity
//
//   RouteWeight      what the act and this room's ROUTE want. The lane profile S5 bound to the strand wins
//                    where it names the kind, the act's own table answers where it does not.
//   ActBudgetNeed    100 % while the act is on pace for the role's target, more when it has fallen behind,
//                    AtTargetNeedPercent once the target is reached (a target is not a ceiling).
//   BandBudgetNeed   the same pace over the one DEPTH BAND this room sits in, and a flat 100 % where no band has
//                    an opinion — so a spec with no bands scores exactly as it did before bands existed.
//   LocalDiversity   a penalty for a role that already stands next door, and a bigger one for a role already
//                    offered by the other side of the same fork.
//
// Eligibility is a HARD FILTER and never a multiplier: a role gated to a depth, a role at its ceiling — the act's
// or its band's — a role a route's flavour forbids outright (an authored weight of 0) and a role banned from
// standing after itself are not unlikely, they are absent. A band never relaxes a gate in the other direction
// either (source document §14): a band that asks for a role the depth gates keep out of it asks for nothing, and
// StrategicActSpecValidator says so before a seed is spent. The draws come from the ROOMS stream
// (MapSeedStreams.Rooms), so retuning what stands in a room cannot reshape an act or reflavour a route.
//
// WHY THE TWO AUTHORED WEIGHT TABLES ARE NOT MULTIPLIED, which is what the source document's
// `BaseActWeight × StrandAffinity` literally says: both are ABSOLUTE weight tables in this codebase, and a
// product of two absolute tables squares the author's intent — a role the act weights 7 and the lane weights 7
// would be forty-nine times as likely as one both merely allow at 1, a ratio nobody wrote down. A lane profile
// here says what is DIFFERENT about a route, so it overrides the act's table per kind, and is silent about the
// rest. The expressive gain over the old `column % count` is unchanged, and one thing is new: an explicit
// weight of 0 now means "never on this route", which a lane had no way to say before.
//
// Nothing here throws over an act it cannot satisfy. An unkept minimum becomes a RoomShortfall and a room with
// no legal role becomes a ForcedRoom naming the rule that yielded — S7's validator refuses the specs that are
// impossible, S10's repair fixes the ones that were merely unlucky, and an allocator that threw could report
// neither.
public static class StrategicRoomAllocator
{
    public static StrategicRoomPlan Allocate(int seed, StrategicStrandProfiles profiles, StrategicRoomSpec spec)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(spec);
        spec.Validate();

        var rules = spec.Rules;
        var topology = profiles.Topology;
        var rows = topology.Rows.Count;
        var rng = new MapGenRandom(seed);

        // The boss rooms are part of the act's shape, not of its room supply: their content is fixed, and a
        // budget that counted them would be describing the topology twice.
        var kinds = new Dictionary<NodeId, MapNodeKind>();
        var slots = new List<StrategicSlot>();
        foreach (var slot in topology.Slots)
        {
            if (slot.IsBoss)
                kinds[slot.Id] = MapNodeKind.Boss;
            else
                slots.Add(slot);
        }

        var placeable = Placeable(profiles, spec);
        if (placeable.Count == 0)
            throw new ArgumentException(
                "An act has no role to fill its rooms with: no kind carries a positive weight, a target or a "
                + "minimum.", nameof(spec));

        // Who stands next to whom, once. `Neighbours` is along the edges (the rooms a player walks between) and
        // `Siblings` is the other side of a fork — two rooms of one row reachable from the same room, which is
        // where offering the same thing twice stops being repetitive and becomes a choice that is not one.
        var neighbours = new Dictionary<NodeId, List<NodeId>>();
        var siblings = new Dictionary<NodeId, List<NodeId>>();
        foreach (var slot in slots)
        {
            var along = topology.PredecessorsOf(slot.Id).Concat(topology.SuccessorsOf(slot.Id)).Distinct().ToList();
            neighbours[slot.Id] = along;
            siblings[slot.Id] = topology.PredecessorsOf(slot.Id)
                .SelectMany(topology.SuccessorsOf)
                .Where(other => other != slot.Id)
                .Distinct()
                .ToList();
        }

        // WHICH DEPTH BAND EACH ROOM SITS IN, once. Bands cannot overlap, so a room has at most one, and -1 means
        // no band has an opinion about it — the common case, and the whole act when nothing is banded.
        var bands = spec.DepthBands;
        var bandOf = slots.ToDictionary(slot => slot.Id, slot => spec.BandIndexOf(slot.Row, rows));

        // How many rooms each role could stand in AT ALL, by its depth gate alone — the denominator the budget's
        // pace is measured against. A role allowed only in the act's last third is not "behind" in its first.
        var eligibleTotal = placeable.ToDictionary(kind => kind, kind => slots.Count(slot => DeepEnough(spec, kind, slot, rows)));
        var eligibleLeft = new Dictionary<MapNodeKind, int>(eligibleTotal);
        var placed = placeable.ToDictionary(kind => kind, _ => 0);

        // The same three numbers per band. A band's denominator is its OWN rooms, which is what makes a band a
        // statement about one slice of the act rather than a second way of writing the act's total.
        var bandEligibleTotal = new List<Dictionary<MapNodeKind, int>>();
        var bandEligibleLeft = new List<Dictionary<MapNodeKind, int>>();
        var bandPlaced = new List<Dictionary<MapNodeKind, int>>();
        for (var band = 0; band < bands.Count; band++)
        {
            var index = band;
            var total = placeable.ToDictionary(kind => kind,
                kind => slots.Count(slot => bandOf[slot.Id] == index && DeepEnough(spec, kind, slot, rows)));
            bandEligibleTotal.Add(total);
            bandEligibleLeft.Add(new Dictionary<MapNodeKind, int>(total));
            bandPlaced.Add(placeable.ToDictionary(kind => kind, _ => 0));
        }

        var shortfalls = new List<RoomShortfall>();
        var forced = new List<ForcedRoom>();

        void Fill(StrategicSlot slot, MapNodeKind kind)
        {
            kinds[slot.Id] = kind;
            placed[kind]++;
            var band = bandOf[slot.Id];
            if (band >= 0)
                bandPlaced[band][kind]++;
            foreach (var other in placeable)
            {
                if (!DeepEnough(spec, other, slot, rows))
                    continue;
                eligibleLeft[other]--;
                if (band >= 0)
                    bandEligibleLeft[band][other]--;
            }
        }

        // PASS ONE — THE PROMISES. A budget minimum is placed before anything is drawn by weight, because a role
        // that waits its turn competes for rooms a filler has already taken. A BAND's minimum is a promise of the
        // same kind, and a narrower one — its rooms are a subset of the act's — so the ordering below puts it
        // first by the same rule it puts a rare role first: the fewer rooms a promise may be kept in, the less
        // freedom there is to keep it later. A band minimum also counts toward the act's own, which falls out of
        // `placed` being one map: "at least two elites after the halfway mark" half-satisfies "at least four".
        var promises = new List<(MapNodeKind Kind, int Band, int Wanted, int Pool)>();
        foreach (var kind in placeable)
        {
            for (var band = 0; band < bands.Count; band++)
            {
                var minimum = bands[band].BudgetOf(kind)?.Min ?? 0;
                if (minimum > 0)
                    promises.Add((kind, band, minimum, bandEligibleTotal[band][kind]));
            }
            var wanted = spec.BudgetOf(kind)?.Min ?? 0;
            if (wanted > 0)
                promises.Add((kind, -1, wanted, eligibleTotal[kind]));
        }

        int Kept(MapNodeKind kind, int band) => band < 0 ? placed[kind] : bandPlaced[band][kind];

        foreach (var promise in promises
            .OrderBy(promise => promise.Pool)
            .ThenBy(promise => promise.Band < 0 ? 1 : 0)
            .ThenBy(promise => promise.Band)
            .ThenBy(promise => Priority(rules, promise.Kind))
            .ThenBy(promise => (int)promise.Kind)
            .ToList())
        {
            var (kind, band, wanted, _) = promise;
            while (Kept(kind, band) < wanted)
            {
                var choices = new List<(StrategicSlot Slot, int Weight)>();
                foreach (var slot in slots)
                {
                    if (kinds.ContainsKey(slot.Id)
                        || (band >= 0 && bandOf[slot.Id] != band)
                        || !Legal(profiles, spec, kind, slot, rows, bandOf[slot.Id], placed, bandPlaced, kinds,
                            neighbours, promise: true))
                        continue;
                    // The promise is not negotiable, but WHERE it is kept still follows the route's flavour and
                    // the local variety: a guaranteed elite lands on a route that wanted elites.
                    var weight = Math.Max(1,
                        Math.Max(1, RouteWeight(profiles, spec, kind, slot))
                        * Diversity(rules, kind, slot, kinds, neighbours, siblings) / 100);
                    choices.Add((slot, weight));
                }

                if (choices.Count == 0)
                {
                    var pool = band < 0 ? slots : slots.Where(slot => bandOf[slot.Id] == band).ToList();
                    shortfalls.Add(new RoomShortfall
                    {
                        Kind = kind,
                        Band = band < 0 ? null : bands[band],
                        Wanted = wanted,
                        Placed = Kept(kind, band),
                        Reason = Why(spec, kind, pool, rows, kinds, band < 0 ? "this act" : $"the {bands[band].Label} band"),
                    });
                    break;
                }

                Fill(Draw(choices, rng), kind);
            }
        }

        // PASS TWO — THE REST, in reading order, each room drawn from the roles it may legally hold. The need
        // factor means the order is not neutral: a role still short of its target grows more likely as the act
        // runs out of rooms, which is what keeps a soft target soft and still mostly met.
        foreach (var slot in slots)
        {
            if (kinds.ContainsKey(slot.Id))
                continue;

            var band = bandOf[slot.Id];
            var candidates = new List<(MapNodeKind Kind, int Weight)>();
            foreach (var kind in placeable)
            {
                if (!Legal(profiles, spec, kind, slot, rows, band, placed, bandPlaced, kinds, neighbours,
                        promise: false))
                    continue;
                // Long arithmetic and then back to int: the two need factors are percentages that a pathological
                // MaxNeedPercent could multiply past an int, and a weight that silently wrapped would be a map
                // that depends on the machine. A band need of 100 % cancels exactly, so an unbanded act's weights
                // are bit-identical to what they were before this factor existed.
                var weight = (int)((long)RouteWeight(profiles, spec, kind, slot)
                    * Need(spec, kind, placed, eligibleTotal, eligibleLeft)
                    * BandNeed(spec, kind, band, bandPlaced, bandEligibleTotal, bandEligibleLeft) / 100
                    * Diversity(rules, kind, slot, kinds, neighbours, siblings) / 100);
                candidates.Add((kind, weight));
            }

            if (candidates.Sum(candidate => candidate.Weight) > 0)
            {
                Fill(slot, Draw(candidates, rng));
                continue;
            }

            // NOTHING SCORED, BUT SOMETHING IS LEGAL — the budgets and penalties cancelled out. The room goes to
            // what this ROUTE is most about, not to Combat: "Combat is the universal repair target" is exactly
            // the old ceiling logic's defect, and on an errand lane the honest filler is a shop.
            if (candidates.Count > 0)
            {
                Fill(slot, Strongest(profiles, spec, candidates.Select(candidate => candidate.Kind), slot));
                continue;
            }

            // NOTHING IS LEGAL. A room cannot be empty, so exactly one rule yields, cheapest first, and says so.
            var (fallback, reason) = Yield(profiles, spec, slot, rows, band, placed, bandPlaced, kinds, neighbours,
                placeable);
            forced.Add(new ForcedRoom { Room = slot.Id, Kind = fallback, Reason = reason });
            Fill(slot, fallback);
        }

        return new StrategicRoomPlan(profiles, spec, kinds, shortfalls, forced);
    }

    public static StrategicRoomPlan Allocate(int seed, StrategicStrandProfiles profiles) =>
        Allocate(seed, profiles, new StrategicRoomSpec());

    // Every role this act can hold: one the act weights, one a route's flavour weights, or one a budget asks
    // for. Boss is the topology's and Mimic is a realized Treasure — neither is ever placed here.
    internal static List<MapNodeKind> Placeable(StrategicStrandProfiles profiles, StrategicRoomSpec spec)
    {
        var placeable = new HashSet<MapNodeKind>();
        foreach (var (kind, weight) in spec.KindWeights)
            if (weight > 0)
                placeable.Add(kind);
        foreach (var profile in profiles.Profiles)
            foreach (var (kind, weight) in profile.KindWeights)
                if (weight > 0)
                    placeable.Add(kind);
        foreach (var (kind, budget) in spec.RoomBudgets)
            if (budget.Min > 0 || budget.Target > 0)
                placeable.Add(kind);
        foreach (var band in spec.DepthBands)
            foreach (var (kind, budget) in band.Budgets)
                if (budget.Min > 0 || budget.Target > 0)
                    placeable.Add(kind);
        placeable.Remove(MapNodeKind.Boss);
        placeable.Remove(MapNodeKind.Mimic);
        return placeable.OrderBy(kind => (int)kind).ToList();
    }

    // What the act and this room's ROUTE want of a role. The lane profile wins where it names the kind — that is
    // what makes a route's character a character — and is silent about the rest, where the act's own table is
    // the answer. A room whose strand carries no profile at all cannot happen on a finished assignment; if it
    // ever did, the act's table is still a correct answer, which is why this reads rather than throws.
    internal static int RouteWeight(
        StrategicStrandProfiles profiles, StrategicRoomSpec spec, MapNodeKind kind, StrategicSlot slot)
    {
        if (!profiles.TryIndexOf(slot.Strand, slot.Row, out var index))
            return spec.WeightOf(kind);
        return profiles.Profiles[index].KindWeights.TryGetValue(kind, out var weight)
            ? Math.Max(0, weight)
            : spec.WeightOf(kind);
    }

    // HOW BADLY THE ACT STILL NEEDS A ROLE, as a percentage of its normal appetite. The pace is the density the
    // budget asked for: `Target / eligible rooms` at the start against `still wanted / eligible rooms left` now.
    // On pace is 100 %, so a role with no budget and a role exactly on schedule are scored identically — the
    // budget bends the draw only where the act has drifted.
    private static int Need(
        StrategicRoomSpec spec,
        MapNodeKind kind,
        IReadOnlyDictionary<MapNodeKind, int> placed,
        IReadOnlyDictionary<MapNodeKind, int> eligibleTotal,
        IReadOnlyDictionary<MapNodeKind, int> eligibleLeft)
    {
        var budget = spec.BudgetOf(kind);
        if (budget is null || budget.Target <= 0)
            return 100;
        return Pace(spec.Rules, budget.Target - placed[kind], budget.Target, eligibleTotal[kind], eligibleLeft[kind]);
    }

    // THE SAME PACE, OVER ONE SLICE OF THE ACT. A band budget is a statement about WHERE a role sits rather than
    // how much of it there is, so the factor is the same arithmetic on the band's own rooms — and exactly 100 %
    // where no band has an opinion, which is what keeps an unbanded act scored as it was before bands existed.
    private static int BandNeed(
        StrategicRoomSpec spec,
        MapNodeKind kind,
        int band,
        IReadOnlyList<Dictionary<MapNodeKind, int>> placed,
        IReadOnlyList<Dictionary<MapNodeKind, int>> eligibleTotal,
        IReadOnlyList<Dictionary<MapNodeKind, int>> eligibleLeft)
    {
        if (band < 0)
            return 100;
        var budget = spec.DepthBands[band].BudgetOf(kind);
        if (budget is null || budget.Target <= 0)
            return 100;
        return Pace(spec.Rules, budget.Target - placed[band][kind], budget.Target,
            eligibleTotal[band][kind], eligibleLeft[band][kind]);
    }

    // The pace itself, written once: the density a target asked for against the density still wanted. Shared by
    // the act and its bands deliberately — two copies of this formula would let a band drift from the act it is a
    // slice of, and "the act is behind on elites" and "this third of the act is" would stop being the same
    // sentence at different scopes.
    private static int Pace(StrategicRoomRules rules, int wanted, int target, int total, int left)
    {
        if (wanted <= 0)
            return rules.AtTargetNeedPercent;
        if (left <= 0)
            return rules.MaxNeedPercent;
        var need = 100L * wanted * total / ((long)left * target);
        return (int)Math.Clamp(need, rules.AtTargetNeedPercent, rules.MaxNeedPercent);
    }

    // THE PENALTY FOR REPEATING, as a percentage. Two penalties, multiplied, and both measured only against
    // rooms that are ALREADY decided — an allocator that waited for its neighbours would be a solver.
    private static int Diversity(
        StrategicRoomRules rules,
        MapNodeKind kind,
        StrategicSlot slot,
        IReadOnlyDictionary<NodeId, MapNodeKind> kinds,
        IReadOnlyDictionary<NodeId, List<NodeId>> neighbours,
        IReadOnlyDictionary<NodeId, List<NodeId>> siblings)
    {
        if (rules.RepeatFreelyKinds.Contains(kind))
            return 100;

        var diversity = 100;
        if (neighbours[slot.Id].Any(other => kinds.GetValueOrDefault(other, MapNodeKind.Boss) == kind))
            diversity = diversity * rules.SameAsNeighbourPercent / 100;
        if (siblings[slot.Id].Any(other => kinds.GetValueOrDefault(other, MapNodeKind.Boss) == kind))
            diversity = diversity * rules.SameAtForkPercent / 100;
        return diversity;
    }

    // THE HARD FILTERS ARE `internal` FROM HERE DOWN, because S10's repair asks exactly the same questions of a
    // FINISHED act — may this room hold that role — and two implementations of "legal" that must agree is the
    // drift this file spent S6 avoiding. The allocator asks them of its running state, the repair of a plan; the
    // rules themselves are written once.
    //
    // The hard filters, in the order they are cheapest to check. Boss as the "no room here yet" default is safe:
    // Boss is never placeable, so an undecided neighbour can never look like a repetition. A route weight of 0 is
    // a filter rather than a weight of zero, so that "no shops on the gauntlet" is a fact about the act and not
    // an outcome that a budget's need factor could out-vote.
    private static bool Legal(
        StrategicStrandProfiles profiles,
        StrategicRoomSpec spec,
        MapNodeKind kind,
        StrategicSlot slot,
        int rows,
        int band,
        IReadOnlyDictionary<MapNodeKind, int> placed,
        IReadOnlyList<Dictionary<MapNodeKind, int>> bandPlaced,
        IReadOnlyDictionary<NodeId, MapNodeKind> kinds,
        IReadOnlyDictionary<NodeId, List<NodeId>> neighbours,
        bool promise) =>
        DeepEnough(spec, kind, slot, rows)
        && Below(spec, kind, band, placed, bandPlaced)
        && !Forbidden(spec, kind, slot, neighbours, kinds)
        // A MINIMUM IS PLACED ON THE STRENGTH OF BEING A MINIMUM, and a weight only decides WHERE. So a promise
        // asks the weaker question — did this route REFUSE the role — while a preference asks whether anyone
        // actually wants it. The difference is the act that says "at least two workbenches" and never mentions
        // workbenches in any weight table: a promise is already the statement of intent, and silence is not a
        // refusal. An authored 0 still is one, in both passes.
        && (promise ? !RouteRefuses(profiles, kind, slot) : RouteWeight(profiles, spec, kind, slot) > 0);

    // Whether this room's route forbids a role OUTRIGHT — an authored weight of 0 in the lane profile the
    // strand carries. Silence is not a refusal; it only means the act's own table answers (see RouteWeight).
    internal static bool RouteRefuses(StrategicStrandProfiles profiles, MapNodeKind kind, StrategicSlot slot) =>
        profiles.TryIndexOf(slot.Strand, slot.Row, out var index)
        && profiles.Profiles[index].KindWeights.TryGetValue(kind, out var weight)
        && weight <= 0;

    internal static bool DeepEnough(StrategicRoomSpec spec, MapNodeKind kind, StrategicSlot slot, int rows) =>
        MapDepth.Percent(slot.Row, rows) >= spec.EarliestDepthOf(kind);

    // UNDER BOTH CEILINGS — the act's and, where the room sits in one, its band's. A band ceiling is the half of a
    // band that actually forbids something: "at most one shop this early" is a rule, while "two shops early" as a
    // target is only a preference the weights may miss.
    private static bool Below(
        StrategicRoomSpec spec,
        MapNodeKind kind,
        int band,
        IReadOnlyDictionary<MapNodeKind, int> placed,
        IReadOnlyList<Dictionary<MapNodeKind, int>> bandPlaced) =>
        placed[kind] < (spec.BudgetOf(kind)?.Max ?? int.MaxValue)
        && (band < 0 || bandPlaced[band][kind] < (spec.DepthBands[band].BudgetOf(kind)?.Max ?? int.MaxValue));

    internal static bool Forbidden(
        StrategicRoomSpec spec,
        MapNodeKind kind,
        StrategicSlot slot,
        IReadOnlyDictionary<NodeId, List<NodeId>> neighbours,
        IReadOnlyDictionary<NodeId, MapNodeKind> kinds) =>
        spec.Rules.NoRepeatKinds.Contains(kind)
        && neighbours[slot.Id].Any(other => kinds.GetValueOrDefault(other, MapNodeKind.Boss) == kind);

    // WHICH RULE YIELDS when a room has no legal role at all, cheapest first: a ceiling is the author's
    // preference, a no-repeat ban is about the room next door, a route's flavour is about this act's feel, and a
    // depth gate is about the act's whole difficulty curve — so the depth gate is the last thing to break.
    private static (MapNodeKind Kind, string Reason) Yield(
        StrategicStrandProfiles profiles,
        StrategicRoomSpec spec,
        StrategicSlot slot,
        int rows,
        int band,
        IReadOnlyDictionary<MapNodeKind, int> placed,
        IReadOnlyList<Dictionary<MapNodeKind, int>> bandPlaced,
        IReadOnlyDictionary<NodeId, MapNodeKind> kinds,
        IReadOnlyDictionary<NodeId, List<NodeId>> neighbours,
        IReadOnlyList<MapNodeKind> placeable)
    {
        var deep = placeable.Where(kind => DeepEnough(spec, kind, slot, rows)).ToList();

        var ceilingOnly = deep
            .Where(kind => !Forbidden(spec, kind, slot, neighbours, kinds)
                && RouteWeight(profiles, spec, kind, slot) > 0)
            .ToList();
        if (ceilingOnly.Count > 0)
        {
            // Which ceiling, said out loud: a role still under the act's own total but full for this stretch of
            // depth is a band's doing, and sending the author to the act's number would be sending them to the
            // wrong one.
            var banded = band >= 0
                && ceilingOnly.Any(kind => placed[kind] < (spec.BudgetOf(kind)?.Max ?? int.MaxValue));
            return (Strongest(profiles, spec, ceilingOnly, slot), banded
                ? $"every role this room may hold is full for the {spec.DepthBands[band].Label} band"
                : "every role this room may hold is at its ceiling");
        }

        var banOnly = deep.Where(kind => RouteWeight(profiles, spec, kind, slot) > 0).ToList();
        if (banOnly.Count > 0)
            return (Strongest(profiles, spec, banOnly, slot),
                "the only role allowed at this depth already stands next door");

        if (deep.Count > 0)
            return (Strongest(profiles, spec, deep, slot),
                "this route's flavour forbids every role allowed at this depth");

        return (Strongest(profiles, spec, placeable, slot), "no role may stand this shallow in the act");
    }

    // The role this ROUTE wants most, the authored priority order breaking a tie. Used wherever a room has to be
    // filled without a draw, so the filler is a statement about the route rather than a constant.
    private static MapNodeKind Strongest(
        StrategicStrandProfiles profiles,
        StrategicRoomSpec spec,
        IEnumerable<MapNodeKind> candidates,
        StrategicSlot slot) =>
        candidates
            .OrderByDescending(kind => RouteWeight(profiles, spec, kind, slot))
            .ThenBy(kind => Priority(spec.Rules, kind))
            .ThenBy(kind => (int)kind)
            .First();

    // Why a promise could not be kept, in the words of whatever actually blocked it — the useful half of a
    // shortfall report.
    private static string Why(
        StrategicRoomSpec spec,
        MapNodeKind kind,
        IReadOnlyList<StrategicSlot> slots,
        int rows,
        IReadOnlyDictionary<NodeId, MapNodeKind> kinds,
        string where)
    {
        var deep = slots.Count(slot => DeepEnough(spec, kind, slot, rows));
        if (deep == 0)
            return $"no room of {where} is as deep as {spec.EarliestDepthOf(kind)} %";
        var free = slots.Count(slot => !kinds.ContainsKey(slot.Id) && DeepEnough(spec, kind, slot, rows));
        if (free == 0)
            return $"all {deep} room(s) of {where} deep enough for it were already taken";
        return $"the {free} free room(s) of {where} deep enough for it are on routes that forbid it, at a ceiling, "
            + "or next to one of its own";
    }

    private static int Priority(StrategicRoomRules rules, MapNodeKind kind)
    {
        var index = rules.RolePriority.ToList().IndexOf(kind);
        return index < 0 ? rules.RolePriority.Count + (int)kind : index;
    }

    // The weighted draw, over whatever is being chosen between. Same shape as the topology walk's: the weights
    // are relative, the stream is the act's, and the first option wins a degenerate total rather than throwing.
    private static T Draw<T>(IReadOnlyList<(T Option, int Weight)> options, MapGenRandom rng)
    {
        var total = options.Sum(option => Math.Max(0, option.Weight));
        if (total <= 0)
            return options[0].Option;
        var roll = rng.Next(total);
        foreach (var option in options)
        {
            roll -= Math.Max(0, option.Weight);
            if (roll < 0)
                return option.Option;
        }
        return options[^1].Option;
    }
}
