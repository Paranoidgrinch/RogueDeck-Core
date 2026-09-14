namespace RogueDeck.Run;

// WHAT AN ACT IS STILL MISSING, in the order the four answers matter (map rework S10).
//
// Four measurements exist by now and they are not equally serious. A minimum the act promised and did not keep
// is a broken contract; a room holding a role that breaks a rule is a defect the allocator had to commit to;
// a route below the floor of challenge is an act that can be walked round; a fork that decides nothing is a
// disappointment. So they are compared LEXICOGRAPHICALLY rather than added up: no weighting of "one shortfall
// equals how many flat forks" exists, nobody has evidence for one, and a repair that traded a promise for a
// prettier fork would be a repair nobody asked for.
public readonly record struct MapDefects(int Shortfall, int Forced, int PressureMissing, int ForkDeficit)
    : IComparable<MapDefects>
{
    // Nothing left to fix: every promise kept, every room legal, every route over the floor, every fork above
    // the soft target. The bit the generator's attempt loop stops on.
    public bool None => Shortfall == 0 && Forced == 0 && PressureMissing == 0 && ForkDeficit == 0;

    public int CompareTo(MapDefects other)
    {
        if (Shortfall != other.Shortfall)
            return Shortfall.CompareTo(other.Shortfall);
        if (Forced != other.Forced)
            return Forced.CompareTo(other.Forced);
        if (PressureMissing != other.PressureMissing)
            return PressureMissing.CompareTo(other.PressureMissing);
        return ForkDeficit.CompareTo(other.ForkDeficit);
    }

    public static bool operator <(MapDefects left, MapDefects right) => left.CompareTo(right) < 0;
    public static bool operator >(MapDefects left, MapDefects right) => left.CompareTo(right) > 0;
    public static bool operator <=(MapDefects left, MapDefects right) => left.CompareTo(right) <= 0;
    public static bool operator >=(MapDefects left, MapDefects right) => left.CompareTo(right) >= 0;

    public override string ToString() =>
        $"short {Shortfall} · forced {Forced} · pressure {PressureMissing} · forks {ForkDeficit}";
}

public enum RepairKind
{
    // Two rooms trade roles. The preferred operation (source document §24, Repair A): it cannot break an act-wide
    // count, because it does not change one.
    Swap,

    // One room changes role. Needed for an act-wide shortfall — no swap can make a fifth elite out of four — and
    // only ever accepted when the totals it moves stay inside their budgets.
    Reassign,
}

// ONE CHANGE, AND WHAT IT WAS FOR. Kept rather than counted: "the act needed three repairs" is a statistic,
// "the act needed three repairs, all of them moving an elite onto the thin route" is a tuning note for S12.
public sealed record RepairOperation
{
    public required RepairKind Operation { get; init; }
    public required NodeId Room { get; init; }
    public required MapNodeKind From { get; init; }
    public required MapNodeKind To { get; init; }

    // The other room of a swap, which receives `From`. Null for a reassignment.
    public NodeId? Partner { get; init; }

    // Which of the four defects the change was aimed at, in the words the report uses.
    public required string Defect { get; init; }

    public required MapDefects Before { get; init; }
    public required MapDefects After { get; init; }

    public override string ToString() => Operation is RepairKind.Swap
        ? $"swap {Room.Value} {MapDiagnostics.Letter(From)}↔{MapDiagnostics.Letter(To)} {Partner!.Value.Value} "
            + $"({Defect}: {Before} → {After})"
        : $"set {Room.Value} {MapDiagnostics.Letter(From)}→{MapDiagnostics.Letter(To)} "
            + $"({Defect}: {Before} → {After})";
}

// How hard the repair may try. Both numbers exist because a generator that is allowed to search until it is
// happy is a generator whose running time is a property of the seed.
public sealed record RepairRules
{
    // How many changes may be accepted. A pass here is ONE accepted change, not a sweep: a sweep that alters
    // several rooms at once cannot say which alteration helped, and this is the list S13 reads.
    public int MaxRepairPasses { get; init; } = 12;

    // How many candidate changes may be WEIGHED in one pass before the pass gives up. The candidates are
    // generated in the order most likely to help (see MapRepair.Candidates), so a low cap costs little; an
    // unbounded search would cost an act with an unsatisfiable number the whole generation budget.
    public int MaxCandidatesPerPass { get; init; } = 400;

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(MaxRepairPasses);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxCandidatesPerPass);
    }
}

// An act after the repair has done what it can.
public sealed record RepairedAct
{
    public required StrategicRoomPlan Plan { get; init; }
    public required IReadOnlyList<RepairOperation> Operations { get; init; }
    public required MapDefects Before { get; init; }
    public required MapDefects After { get; init; }

    public bool Clean => After.None;
}

// THE DETERMINISTIC REPAIR (map rework S10, source document §24).
//
// The allocator draws a whole act and reports what it could not do; the repair is what happens next, and it is
// a first-improvement hill climb over MapDefects: generate candidate changes in the order most likely to help
// the WORST defect, take the first one that makes the act strictly better, repeat. Two properties fall out of
// that shape rather than being asserted about it — a repair can never make an act worse, because a change is
// only taken when the comparison says better, and the same act repairs the same way every time, because nothing
// here draws a random number.
//
// THE REPAIR STREAM STAYS UNUSED, deliberately. S3 reserved one (source document §26) and a hill climb over a
// heuristically ordered list has nothing to spend it on: a repair that depends on a draw is a repair nobody can
// reason about, and the ordering is a better use of the information than a shuffle. The stream remains there for
// a later repair that needs it.
//
// Legality is not re-implemented here. `Forced` counts the rooms whose role breaks one of the allocator's own
// four hard rules, using the allocator's own predicates, so an illegal change is rejected by the comparison
// itself instead of by a second copy of the rulebook that could disagree with the first.
public static class MapRepair
{
    public static RepairedAct Repair(
        StrategicRoomPlan plan, PathPressureRules pressure, ForkQualityRules forks, RepairRules rules)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(pressure);
        ArgumentNullException.ThrowIfNull(forks);
        ArgumentNullException.ThrowIfNull(rules);
        rules.Validate();

        var act = new Act(plan, pressure, forks);
        var before = act.Defects();
        var operations = new List<RepairOperation>();

        while (operations.Count < rules.MaxRepairPasses)
        {
            var current = act.Defects();
            if (current.None)
                break;

            var taken = act.Improve(current, rules.MaxCandidatesPerPass);
            if (taken is null)
                break;
            operations.Add(taken);
        }

        return new RepairedAct
        {
            Plan = act.Commit(),
            Operations = operations,
            Before = before,
            After = act.Defects(),
        };
    }

    // What an act is still missing, without repairing anything — the measurement the generator's attempt loop
    // and S13's report both start from.
    public static MapDefects Defects(
        StrategicRoomPlan plan, PathPressureRules pressure, ForkQualityRules forks)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(pressure);
        ArgumentNullException.ThrowIfNull(forks);
        return new Act(plan, pressure, forks).Defects();
    }

    // The act as something that can be changed and measured: the rooms in a scratch dictionary, everything
    // derived from the topology computed once, and one method per question anybody asks of it.
    private sealed class Act
    {
        private readonly StrategicRoomPlan _original;
        private readonly StrategicStrandProfiles _profiles;
        private readonly StrategicRoomSpec _spec;
        private readonly StrategicTopology _topology;
        private readonly PathPressureRules _pressure;
        private readonly ForkQualityRules _forks;
        private readonly Dictionary<NodeId, MapNodeKind> _kinds;
        private readonly List<StrategicSlot> _slots;
        private readonly Dictionary<NodeId, StrategicSlot> _slotOf;
        private readonly Dictionary<NodeId, List<NodeId>> _neighbours;
        private readonly Dictionary<NodeId, int> _bandOf;
        private readonly List<MapNodeKind> _placeable;
        private readonly int _rows;

        public Act(StrategicRoomPlan plan, PathPressureRules pressure, ForkQualityRules forks)
        {
            _original = plan;
            _profiles = plan.Profiles;
            _spec = plan.Spec;
            _topology = plan.Topology;
            _pressure = pressure;
            _forks = forks;
            _rows = _topology.Rows.Count;
            _kinds = new Dictionary<NodeId, MapNodeKind>(plan.Kinds);
            _slots = _topology.Slots.Where(slot => !slot.IsBoss).ToList();
            _slotOf = _slots.ToDictionary(slot => slot.Id);
            _placeable = StrategicRoomAllocator.Placeable(_profiles, _spec);
            _bandOf = _slots.ToDictionary(slot => slot.Id, slot => _spec.BandIndexOf(slot.Row, _rows));
            _neighbours = _slots.ToDictionary(
                slot => slot.Id,
                slot => _topology.PredecessorsOf(slot.Id).Concat(_topology.SuccessorsOf(slot.Id)).Distinct().ToList());
        }

        private MapNodeKind KindOf(NodeId room) => _kinds.GetValueOrDefault(room, MapNodeKind.Boss);

        // ————— the four measurements —————

        public MapDefects Defects()
        {
            var tally = Tally();
            return new MapDefects(Shortfall(tally), Forced(tally), PressureMissing(), ForkDeficit());
        }

        // How many promised rooms the act is short of, over its own budgets and its bands'. A band's promise
        // counts toward the act's (S7), so the act's own shortfall is measured against the total and a band's
        // against its own rooms — the same arithmetic the allocator reports, read off the finished act.
        private int Shortfall(Counts counts)
        {
            var missing = 0;
            foreach (var (kind, budget) in _spec.RoomBudgets)
                missing += Math.Max(0, budget.Min - counts.Act.GetValueOrDefault(kind));
            for (var band = 0; band < _spec.DepthBands.Count; band++)
                foreach (var (kind, budget) in _spec.DepthBands[band].Budgets)
                    missing += Math.Max(0, budget.Min - counts.InBand(band, kind));
            return missing;
        }

        // Rooms whose role breaks one of the allocator's hard rules.
        private int Forced(Counts counts) => _slots.Count(slot => !Legal(slot, _kinds[slot.Id], counts));

        private PathPressureReport Pressure() => StrategicPathPressure.Measure(_topology, KindOf, _pressure);

        private ForkQualityReport Forks() => ForkQualityEvaluator.Measure(
            _topology.Slots.Select(slot => slot.Id).ToList(), _topology.SuccessorsOf, KindOf, _forks);

        private int PressureMissing() => _pressure.Promises ? Pressure().Missing : 0;

        // How far the act's forks fall short of the soft target, ADDED UP rather than counted: a sum tells a
        // hill climb that a fork lifted from 10 to 30 was progress, and a count of weak forks does not.
        private int ForkDeficit()
        {
            if (_forks.MinimumContrast <= 0)
                return 0;
            return Forks().Forks.Sum(fork => Math.Max(0, _forks.MinimumContrast - fork.Contrast));
        }

        // ————— one accepted change —————

        public RepairOperation? Improve(MapDefects current, int maxCandidates)
        {
            var weighed = 0;
            foreach (var candidate in Candidates(current))
            {
                if (++weighed > maxCandidates)
                    break;

                var from = _kinds[candidate.Room];
                var partnerFrom = candidate.Partner is null ? MapNodeKind.Boss : _kinds[candidate.Partner.Value];

                _kinds[candidate.Room] = candidate.To;
                if (candidate.Partner is not null)
                    _kinds[candidate.Partner.Value] = from;

                var after = Better(current);
                if (after is not null)
                    return new RepairOperation
                    {
                        Operation = candidate.Partner is null ? RepairKind.Reassign : RepairKind.Swap,
                        Room = candidate.Room,
                        From = from,
                        To = candidate.To,
                        Partner = candidate.Partner,
                        Defect = candidate.Defect,
                        Before = current,
                        After = after.Value,
                    };

                _kinds[candidate.Room] = from;
                if (candidate.Partner is not null)
                    _kinds[candidate.Partner.Value] = partnerFrom;
            }
            return null;
        }

        // The current rooms, measured against the cost that was — and STOPPING at the first dimension that
        // differs, because the expensive two are the last two. Most candidates are rejected on a count.
        private MapDefects? Better(MapDefects current)
        {
            var counts = Tally();
            var shortfall = Shortfall(counts);
            if (shortfall != current.Shortfall)
                return shortfall < current.Shortfall
                    ? new MapDefects(shortfall, Forced(counts), PressureMissing(), ForkDeficit())
                    : null;

            var forced = Forced(counts);
            if (forced != current.Forced)
                return forced < current.Forced
                    ? new MapDefects(shortfall, forced, PressureMissing(), ForkDeficit())
                    : null;

            var missing = PressureMissing();
            if (missing != current.PressureMissing)
                return missing < current.PressureMissing
                    ? new MapDefects(shortfall, forced, missing, ForkDeficit())
                    : null;

            var deficit = ForkDeficit();
            return deficit < current.ForkDeficit ? new MapDefects(shortfall, forced, missing, deficit) : null;
        }

        // ————— what to try, and in which order —————

        private readonly record struct Candidate(NodeId Room, MapNodeKind To, NodeId? Partner, string Defect);

        // Candidates for the WORST defect the act currently has, most promising first. Everything here is
        // deterministic: the same act offers the same candidates in the same order, on every machine.
        private IEnumerable<Candidate> Candidates(MapDefects defects)
        {
            if (defects.Shortfall > 0)
                return ForShortfall();
            if (defects.Forced > 0)
                return ForForced();
            if (defects.PressureMissing > 0)
                return ForPressure();
            return ForForks();
        }

        // A PROMISE THE ACT DID NOT KEEP. An act-wide minimum can only be met by making another room into one
        // (a swap moves a role, it does not create one), while a BAND's minimum is a placement problem and a
        // swap is exactly the right tool: take one from outside the band, give back what stood there.
        private IEnumerable<Candidate> ForShortfall()
        {
            var counts = Tally();
            foreach (var (kind, budget) in _spec.RoomBudgets.OrderBy(entry => (int)entry.Key))
            {
                if (counts.Act.GetValueOrDefault(kind) >= budget.Min)
                    continue;
                foreach (var slot in _slots)
                    if (_kinds[slot.Id] != kind)
                        yield return new Candidate(slot.Id, kind, null, $"{kind} below its minimum");
            }

            for (var band = 0; band < _spec.DepthBands.Count; band++)
                foreach (var (kind, budget) in _spec.DepthBands[band].Budgets.OrderBy(entry => (int)entry.Key))
                {
                    if (counts.InBand(band, kind) >= budget.Min)
                        continue;
                    var label = $"{kind} below its minimum in the {_spec.DepthBands[band].Label} band";
                    var inside = _slots.Where(slot => _bandOf[slot.Id] == band && _kinds[slot.Id] != kind).ToList();
                    var outside = _slots.Where(slot => _bandOf[slot.Id] != band && _kinds[slot.Id] == kind).ToList();
                    foreach (var slot in inside)
                    {
                        foreach (var donor in outside)
                            yield return new Candidate(slot.Id, kind, donor.Id, label);
                        yield return new Candidate(slot.Id, kind, null, label);
                    }
                }
        }

        // A ROOM THE ALLOCATOR HAD TO BREAK A RULE TO FILL. Try every legal role for it first — the cheapest
        // possible fix, and often there is one now that its neighbours have changed — then trade with any other
        // room, which is how a room with no legal role at all is rescued by moving the problem somewhere it is
        // not a problem.
        private IEnumerable<Candidate> ForForced()
        {
            var counts = Tally();
            foreach (var slot in _slots.Where(slot => !Legal(slot, _kinds[slot.Id], counts)))
            {
                foreach (var kind in _placeable)
                    if (kind != _kinds[slot.Id])
                        yield return new Candidate(slot.Id, kind, null, "a room with no legal role");
                foreach (var other in _slots)
                    if (other.Id != slot.Id && _kinds[other.Id] != _kinds[slot.Id])
                        yield return new Candidate(slot.Id, _kinds[other.Id], other.Id, "a room with no legal role");
            }
        }

        // THE ROUTE THAT WALKS ROUND EVERYTHING. The thin route is known by name (S8 reports the rooms), so the
        // candidates are: give one of its rooms a more demanding role, and give what stood there to a room that
        // is not on the route. Ordered by how much pressure the trade moves, because the first improvement is
        // the one taken and the biggest trade is the likeliest to be it.
        private IEnumerable<Candidate> ForPressure()
        {
            var thin = Pressure().Thinnest.Rooms.ToHashSet();
            var trades =
                from slot in _slots
                where thin.Contains(slot.Id)
                from other in _slots
                where !thin.Contains(other.Id)
                let gain = _pressure.PressureOf(_kinds[other.Id]) - _pressure.PressureOf(_kinds[slot.Id])
                where gain > 0
                orderby gain descending, slot.Row, slot.Column, other.Row, other.Column
                select new Candidate(slot.Id, _kinds[other.Id], other.Id, "a route below the floor of challenge");
            return trades;
        }

        // A FORK THAT DECIDES NOTHING. Only the rooms the choice is actually about are worth changing — the
        // rooms a player reaches within the decision horizon — and they are traded against rooms outside it, so
        // that what the act holds does not change while what the fork offers does.
        private IEnumerable<Candidate> ForForks()
        {
            var report = Forks();
            foreach (var fork in report.Forks
                .Where(fork => fork.Contrast < _forks.MinimumContrast)
                .OrderBy(fork => fork.Contrast)
                .ThenBy(fork => fork.Room.Value, StringComparer.Ordinal))
            {
                var inside = Horizon(fork);
                var ways = fork.Branches.Select(branch => branch.Successor).ToList();

                // THE ROOM THAT WOULD SHARPEN THE CHOICE MOST, FIRST. A hill climb takes the first improvement
                // it is offered, so the order is the difference between a fork mended in one swap and a fork
                // nudged four times: a role is worth trying here in proportion to how far it stands from what
                // the OTHER ways on already offer.
                foreach (var room in inside)
                {
                    var rivals = ways.Where(way => way != room).Select(way => _kinds[way]).ToList();
                    var trades =
                        from other in _slots
                        where !inside.Contains(other.Id) && _kinds[other.Id] != _kinds[room]
                        let promise = rivals.Count == 0
                            ? 0
                            : rivals.Max(rival => _forks.Distance(_kinds[other.Id], rival))
                        orderby promise descending, other.Row, other.Column
                        select new Candidate(room, _kinds[other.Id], other.Id, "a fork that decides little");
                    foreach (var trade in trades)
                        yield return trade;
                }
            }
        }

        // The rooms one fork's choice is about: everything reachable from it within the decision horizon.
        private HashSet<NodeId> Horizon(ForkQuality fork)
        {
            var rooms = new HashSet<NodeId>();
            var edge = fork.Branches.Select(branch => branch.Successor).ToList();
            for (var row = 0; row < _forks.HorizonRows && edge.Count > 0; row++)
            {
                var next = new List<NodeId>();
                foreach (var room in edge)
                {
                    if (!_slotOf.ContainsKey(room) || !rooms.Add(room))
                        continue;
                    next.AddRange(_topology.SuccessorsOf(room));
                }
                edge = next;
            }
            return rooms;
        }

        // ————— the rules, asked of the allocator —————

        // Whether a room may hold a role: the allocator's four hard filters, read off a finished act rather than
        // off a running allocation. The one reconstruction is which of the two ROUTE rules applies — the
        // allocator asks the weaker question of a role it is placing to keep a promise (silence is not a refusal)
        // and the stronger one of a role it is merely drawing — and a role still at or under its minimum is a
        // role the act is placing as a promise.
        private bool Legal(StrategicSlot slot, MapNodeKind kind, Counts counts)
        {
            if (kind is MapNodeKind.Boss or MapNodeKind.Mimic)
                return false;
            if (!StrategicRoomAllocator.DeepEnough(_spec, kind, slot, _rows))
                return false;
            if (StrategicRoomAllocator.Forbidden(_spec, kind, slot, _neighbours, _kinds))
                return false;

            if (counts.Act.GetValueOrDefault(kind) > (_spec.BudgetOf(kind)?.Max ?? int.MaxValue))
                return false;
            var band = _bandOf[slot.Id];
            if (band >= 0
                && counts.InBand(band, kind) > (_spec.DepthBands[band].BudgetOf(kind)?.Max ?? int.MaxValue))
                return false;

            var promise = counts.Act.GetValueOrDefault(kind) <= Math.Max(
                _spec.BudgetOf(kind)?.Min ?? 0,
                band < 0 ? 0 : _spec.DepthBands[band].BudgetOf(kind)?.Min ?? 0);
            return promise
                ? !StrategicRoomAllocator.RouteRefuses(_profiles, kind, slot)
                : StrategicRoomAllocator.RouteWeight(_profiles, _spec, kind, slot) > 0;
        }

        // How many rooms of each role the act holds, and how many each band holds — counted ONCE per
        // measurement rather than once per room. A repair weighs hundreds of candidates per pass and every one
        // of them asks these questions of every room; counting inside the question would make the pass
        // quadratic in the act's size for no reason at all.
        private sealed record Counts(
            Dictionary<MapNodeKind, int> Act, List<Dictionary<MapNodeKind, int>> Bands)
        {
            public int InBand(int band, MapNodeKind kind) =>
                band < 0 || band >= Bands.Count ? 0 : Bands[band].GetValueOrDefault(kind);
        }

        private Counts Tally()
        {
            var act = new Dictionary<MapNodeKind, int>();
            var bands = new List<Dictionary<MapNodeKind, int>>();
            for (var band = 0; band < _spec.DepthBands.Count; band++)
                bands.Add([]);
            foreach (var slot in _slots)
            {
                var kind = _kinds[slot.Id];
                act[kind] = act.GetValueOrDefault(kind) + 1;
                var band = _bandOf[slot.Id];
                if (band >= 0)
                    bands[band][kind] = bands[band].GetValueOrDefault(kind) + 1;
            }
            return new Counts(act, bands);
        }

        // ————— the act, written back —————

        // The repaired act as a plan of its own. The shortfalls and forced rooms are recomputed from the rooms
        // as they now stand, and each keeps the ALLOCATOR'S OWN words for why it happened: the count changes, the
        // reason does not, because the reason is the story of how the act was built and no repair rewrites that.
        public StrategicRoomPlan Commit()
        {
            var counts = Tally();
            var shortfalls = new List<RoomShortfall>();

            foreach (var (kind, budget) in _spec.RoomBudgets.OrderBy(entry => (int)entry.Key))
            {
                var placed = counts.Act.GetValueOrDefault(kind);
                if (placed >= budget.Min)
                    continue;
                shortfalls.Add(Restated(kind, band: null, budget.Min, placed));
            }

            for (var band = 0; band < _spec.DepthBands.Count; band++)
                foreach (var (kind, budget) in _spec.DepthBands[band].Budgets.OrderBy(entry => (int)entry.Key))
                {
                    var placed = counts.InBand(band, kind);
                    if (placed >= budget.Min)
                        continue;
                    shortfalls.Add(Restated(kind, _spec.DepthBands[band], budget.Min, placed));
                }

            var forced = new List<ForcedRoom>();
            foreach (var slot in _slots)
            {
                var kind = _kinds[slot.Id];
                if (Legal(slot, kind, counts))
                    continue;
                var told = _original.Forced.FirstOrDefault(room => room.Room == slot.Id && room.Kind == kind);
                forced.Add(told ?? new ForcedRoom
                {
                    Room = slot.Id,
                    Kind = kind,
                    Reason = "no role was legal here after the act was repaired",
                });
            }

            return new StrategicRoomPlan(_profiles, _spec, _kinds, shortfalls, forced);
        }

        private RoomShortfall Restated(MapNodeKind kind, DepthBandBudget? band, int wanted, int placed)
        {
            var told = _original.Shortfalls.FirstOrDefault(short_ => short_.Kind == kind && short_.Band == band);
            return new RoomShortfall
            {
                Kind = kind,
                Wanted = wanted,
                Placed = placed,
                Band = band,
                Reason = told?.Reason ?? "the act held too few of them once the repair had finished",
            };
        }
    }
}
