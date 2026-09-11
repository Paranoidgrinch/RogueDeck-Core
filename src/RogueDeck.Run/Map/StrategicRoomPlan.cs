using System.Text;

namespace RogueDeck.Run;

// HOW MANY OF A ROOM AN ACT HOLDS — a map-wide number, not a per-path one.
//
// The rule-based generator expresses its intent as `PerPathMinimums`: "every route holds at least two elites".
// That is a promise about the WORST route, and the only way to keep it for every route at once is a row every
// route crosses — which is why the old generator inserts width-1 gate funnels and why a BnB act pinches shut
// eight times on its way up. The guarantee is real, and it costs the map its shape: a gate row is a row where
// the branching, and with it the difference between routes, does not exist.
//
// A budget says the same design intent about the ACT instead: this act holds eight elites, at least six, at most
// ten. Nothing has to be inserted to keep it, so the topology stays the one the walk drew, and what a single
// route holds becomes a property to MEASURE (S8's path pressure) rather than a promise to manufacture. The two
// are not the same statement and this is deliberate: "every route has two elites" and "the act has eight" differ
// exactly where the old generator was paying for the difference with its shape.
public sealed record RoomBudget
{
    // The preferred total. The allocator aims at it (see StrategicRoomAllocator's need factor) and is allowed to
    // miss: a target is what the act wants, not what it promises.
    public int Target { get; init; }

    // The hard lower bound — a promise. The allocator places these FIRST, before anything is drawn by weight,
    // and reports a shortfall rather than quietly bending when the act cannot hold them (see RoomShortfall).
    public int Min { get; init; }

    // The hard upper bound. A kind at its ceiling is ineligible, which is how a ceiling differs from a low
    // weight. Unbounded by default, because most roles need no ceiling at all.
    public int Max { get; init; } = int.MaxValue;

    public void Validate(MapNodeKind kind)
    {
        if (Min < 0)
            throw new ArgumentOutOfRangeException(nameof(Min), Min, $"The {kind} budget cannot ask for a negative minimum.");
        if (Target < 0)
            throw new ArgumentOutOfRangeException(nameof(Target), Target, $"The {kind} budget cannot target a negative count.");
        if (Max < Min)
            throw new ArgumentOutOfRangeException(nameof(Max), Max,
                $"The {kind} budget allows at most {Max} rooms but demands at least {Min}.");
        // A target outside its own bounds is not a preference, it is a contradiction: the allocator would either
        // stop short of it by rule or overshoot the ceiling to reach it. Said here, once, rather than resolved
        // silently in the middle of an allocation.
        if (Target < Min || Target > Max)
            throw new ArgumentOutOfRangeException(nameof(Target), Target,
                $"The {kind} budget targets {Target} rooms, outside its own bounds of {Min}..{Max}.");
    }
}

// How the allocator behaves. Every number is a PERCENTAGE and the arithmetic is integer throughout: a map is a
// contract with a seed, and a score that depends on floating-point rounding is a map that depends on the machine
// it was generated on.
public sealed record StrategicRoomRules
{
    // What a role's need falls to once its target is reached. Not zero, so a ceiling above the target is still
    // reachable — the target is where the act wants to land, the ceiling is how far it may be pushed.
    public int AtTargetNeedPercent { get; init; } = 10;

    // How far a role that has fallen behind may be pushed up. A cap matters because the need factor rises as the
    // act runs out of rows: without it, an act whose budget cannot be met would spend its last rows on nothing
    // else, and a single unsatisfiable number would eat the end of the map.
    public int MaxNeedPercent { get; init; } = 400;

    // A room whose neighbour along an edge is already the same kind. A penalty, not a ban: two events in a row
    // is repetitive, and repetitive is not broken.
    public int SameAsNeighbourPercent { get; init; } = 25;

    // The two sides of a fork offering the same thing. This is the one that matters for a BRANCHING map: a
    // choice between two shops is not a choice, and it is invisible in any count of what the act holds.
    public int SameAtForkPercent { get; init; } = 40;

    // Kinds that may repeat freely. Combat is the act's rhythm — two fights in a row is an act, not a defect —
    // and penalizing it would push every other role upward everywhere, quietly overriding the authored weights.
    public IReadOnlySet<MapNodeKind> RepeatFreelyKinds { get; init; } = new HashSet<MapNodeKind>
    {
        MapNodeKind.Combat,
    };

    // Kinds that may NOT stand directly after themselves, at all. The source document asks for a mix of hard
    // constraints for obviously bad patterns and penalties for merely repetitive ones; these two are the
    // obviously bad ones, because the second room is a room the player has no reason to enter.
    public IReadOnlySet<MapNodeKind> NoRepeatKinds { get; init; } = new HashSet<MapNodeKind>
    {
        MapNodeKind.Shop, MapNodeKind.Rest,
    };

    // WHICH ROLE IS PLACED FIRST when two are equally constrained — the source document's "most constrained
    // first" order, written down so it is a decision rather than a dictionary's enumeration order. Roles not
    // listed go last, in ordinal order.
    public IReadOnlyList<MapNodeKind> RolePriority { get; init; } = new[]
    {
        MapNodeKind.Elite, MapNodeKind.Shop, MapNodeKind.Workbench, MapNodeKind.Rest,
        MapNodeKind.Treasure, MapNodeKind.MultiCombat, MapNodeKind.Event, MapNodeKind.Combat,
    };

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(RepeatFreelyKinds);
        ArgumentNullException.ThrowIfNull(NoRepeatKinds);
        ArgumentNullException.ThrowIfNull(RolePriority);
        if (AtTargetNeedPercent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(AtTargetNeedPercent), AtTargetNeedPercent,
                "A role's need at its target is a percentage of its normal need (0-100).");
        if (MaxNeedPercent < 100)
            throw new ArgumentOutOfRangeException(nameof(MaxNeedPercent), MaxNeedPercent,
                "A role that has fallen behind cannot be capped below its normal need (100).");
        if (SameAsNeighbourPercent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(SameAsNeighbourPercent), SameAsNeighbourPercent,
                "A repetition penalty is a percentage of the unpenalized score (0-100).");
        if (SameAtForkPercent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(SameAtForkPercent), SameAtForkPercent,
                "A repetition penalty is a percentage of the unpenalized score (0-100).");
    }
}

// What the allocator needs to know about an act, beside its topology and its flavours. Deliberately NOT a change
// to MapGenerationSpec: the rule-based generator's spec is the v0.0.0 contract and the two generators are chosen
// between per run, so the strategic one grows its own vocabulary. S11 folds this into the authored document.
public sealed record StrategicRoomSpec
{
    // BaseActWeight: the act's own table, exactly the existing `MapGenerationSpec.KindWeights`.
    public IReadOnlyDictionary<MapNodeKind, int> KindWeights { get; init; } = new Dictionary<MapNodeKind, int>
    {
        [MapNodeKind.Combat] = 7,
        [MapNodeKind.Event] = 3,
    };

    // How many of each role the ACT holds. Empty means every role is drawn by weight alone, with no count anyone
    // is holding it to.
    public IReadOnlyDictionary<MapNodeKind, RoomBudget> RoomBudgets { get; init; } =
        new Dictionary<MapNodeKind, RoomBudget>();

    // How deep into the act a role may first stand, as a percentage of the act's depth — the existing
    // `MapGenerationSpec.RoleMinimumDepthPercent`, measured with the same `MapDepth.Percent`, and here a HARD
    // filter rather than a preference that yields (the old generator places an early shop anyway rather than
    // leave a per-path promise unkept; there are no per-path promises left to rescue).
    public IReadOnlyDictionary<MapNodeKind, int> RoleMinimumDepthPercent { get; init; } =
        new Dictionary<MapNodeKind, int>();

    public StrategicRoomRules Rules { get; init; } = new();

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(KindWeights);
        ArgumentNullException.ThrowIfNull(RoomBudgets);
        ArgumentNullException.ThrowIfNull(RoleMinimumDepthPercent);
        ArgumentNullException.ThrowIfNull(Rules);

        foreach (var (kind, weight) in KindWeights)
            if (weight < 0)
                throw new ArgumentOutOfRangeException(nameof(KindWeights), weight,
                    $"The act weight for {kind} cannot be negative.");

        foreach (var (kind, budget) in RoomBudgets)
        {
            if (budget is null)
                throw new ArgumentException($"The {kind} budget is null.", nameof(RoomBudgets));
            if (kind is MapNodeKind.Boss)
                throw new ArgumentException(
                    "An act's boss rooms are part of its shape, so they are not budgeted for.", nameof(RoomBudgets));
            // A Mimic is a Treasure that bit back, not a room the allocator places: it is decided when the
            // treasure is realized, and for counting purposes it IS that treasure (source document §36).
            if (kind is MapNodeKind.Mimic)
                throw new ArgumentException(
                    "A Mimic is a realized Treasure, not a placed role; budget Treasure instead.",
                    nameof(RoomBudgets));
            budget.Validate(kind);
        }

        foreach (var (kind, percent) in RoleMinimumDepthPercent)
            if (percent is < 0 or > 100)
                throw new ArgumentOutOfRangeException(nameof(RoleMinimumDepthPercent), percent,
                    $"The earliest depth for the {kind} role must be a percentage (0-100).");

        Rules.Validate();
    }

    public int WeightOf(MapNodeKind kind) => Math.Max(0, KindWeights.GetValueOrDefault(kind));

    public RoomBudget? BudgetOf(MapNodeKind kind) => RoomBudgets.GetValueOrDefault(kind);

    public int EarliestDepthOf(MapNodeKind kind) => Math.Clamp(RoleMinimumDepthPercent.GetValueOrDefault(kind), 0, 100);
}

// A PROMISE THE ACT COULD NOT KEEP. A budget minimum the allocator ran out of legal rooms for — because the
// depth gates leave too few, because a ceiling elsewhere took them, or because the act is simply too small.
//
// Reported rather than thrown, for the reason StrategicTopology.Crossings is measured rather than asserted: an
// allocator that throws cannot tell anyone WHICH promise failed or by how much, and that is the only useful
// thing to say. S7's spec validator refuses the combinations that are impossible before a seed is spent on
// them; S10's repair fixes the ones that are merely unlucky.
public sealed record RoomShortfall
{
    public required MapNodeKind Kind { get; init; }
    public required int Wanted { get; init; }
    public required int Placed { get; init; }
    public required string Reason { get; init; }

    public int Missing => Math.Max(0, Wanted - Placed);

    public override string ToString() => $"{Kind} {Placed}/{Wanted} — {Reason}";
}

// A ROOM THAT HAD NO LEGAL CONTENT, and the rule that was broken to fill it anyway. An empty room is not a
// thing a map may contain, so when every role is gated out of a slot the allocator breaks the cheapest rule it
// can and says so here — a ceiling first, then a no-repeat ban, then a depth gate. Naming the broken rule is
// the whole point: "the act came out fine" and "the act came out fine because a gate yielded twice" are
// different reports, and the old generator could only produce the first.
public sealed record ForcedRoom
{
    public required NodeId Room { get; init; }
    public required MapNodeKind Kind { get; init; }
    public required string Reason { get; init; }

    public override string ToString() => $"{Room.Value} {Kind} — {Reason}";
}

// Which role stands in every room of an act. A class, like the two stages before it, because it carries lookups
// and because two plans are "the same" when Render() is.
public sealed class StrategicRoomPlan
{
    private readonly Dictionary<NodeId, MapNodeKind> _kinds;
    private readonly Dictionary<MapNodeKind, int> _counts;

    public StrategicRoomPlan(
        StrategicStrandProfiles profiles,
        StrategicRoomSpec spec,
        IReadOnlyDictionary<NodeId, MapNodeKind> kinds,
        IReadOnlyList<RoomShortfall> shortfalls,
        IReadOnlyList<ForcedRoom> forced)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(kinds);
        ArgumentNullException.ThrowIfNull(shortfalls);
        ArgumentNullException.ThrowIfNull(forced);

        Profiles = profiles;
        Spec = spec;
        Shortfalls = shortfalls;
        Forced = forced;

        _kinds = new Dictionary<NodeId, MapNodeKind>(kinds);
        _counts = [];
        foreach (var kind in _kinds.Values)
            _counts[kind] = _counts.GetValueOrDefault(kind) + 1;
    }

    public StrategicStrandProfiles Profiles { get; }
    public StrategicRoomSpec Spec { get; }
    public StrategicTopology Topology => Profiles.Topology;
    public IReadOnlyList<RoomShortfall> Shortfalls { get; }
    public IReadOnlyList<ForcedRoom> Forced { get; }

    public IReadOnlyDictionary<NodeId, MapNodeKind> Kinds => _kinds;

    // Every room counted by role, boss rooms included — what the act actually holds, which is the number a
    // budget is about.
    public IReadOnlyDictionary<MapNodeKind, int> Counts => _counts;

    // Whether the act holds what it promised without any rule having to yield. The single number S7's validator,
    // S10's repair and S13's report all start from.
    public bool Honoured => Shortfalls.Count == 0 && Forced.Count == 0;

    public MapNodeKind KindOf(NodeId room) => _kinds.TryGetValue(room, out var kind)
        ? kind
        : throw new ArgumentOutOfRangeException(nameof(room), $"No room {room.Value} in this act.");

    public bool TryKindOf(NodeId room, out MapNodeKind kind) => _kinds.TryGetValue(room, out kind);

    public int Count(MapNodeKind kind) => _counts.GetValueOrDefault(kind);

    // How many rooms of a role stand on routes flavoured by one authored profile. The measurement that says
    // whether the lane profiles did anything at all — with `column % count` gone, this is where the difference
    // between the gauntlet side and the errand side becomes a number.
    public int CountOnProfile(int profileIndex, MapNodeKind kind) => Topology.Slots
        .Count(slot => !slot.IsBoss
            && _kinds.GetValueOrDefault(slot.Id) == kind
            && Profiles.TryIndexOf(slot.Strand, slot.Row, out var index)
            && index == profileIndex);

    // The act as a picture, laid out like MapDiagnostic.Grid() and StrategicTopology.Render() so a shape, its
    // flavours and its rooms can be read one under the other and diffed by a machine.
    public string Render()
    {
        var text = new StringBuilder();
        text.Append("seed ").Append(Topology.Seed)
            .Append(" · rows ").Append(Topology.Rows.Count)
            .Append(" · rooms ").Append(_kinds.Count)
            .Append(" · shortfalls ").Append(Shortfalls.Count)
            .Append(" · forced ").Append(Forced.Count).AppendLine();
        text.Append("rooms ").AppendLine(string.Join(" ", _counts
            .OrderBy(entry => (int)entry.Key)
            .Select(entry => $"{MapDiagnostics.Letter(entry.Key)}{entry.Value}")));

        if (Spec.RoomBudgets.Count > 0)
            text.Append("budgets ").AppendLine(string.Join(" ", Spec.RoomBudgets
                .OrderBy(entry => (int)entry.Key)
                .Select(entry =>
                {
                    var ceiling = entry.Value.Max == int.MaxValue ? "∞" : entry.Value.Max.ToString();
                    return $"{entry.Key}={Count(entry.Key)}/{entry.Value.Target}[{entry.Value.Min}..{ceiling}]";
                })));

        foreach (var row in Topology.Rows)
        {
            var rooms = string.Join(" ", row.Slots.Select(slot =>
                _kinds.TryGetValue(slot.Id, out var kind) ? MapDiagnostics.Letter(kind) : '.'));
            var lanes = string.Join("", row.Slots.Select(slot =>
                Profiles.TryIndexOf(slot.Strand, slot.Row, out var index) ? index.ToString() : "?"));
            text.Append('r').Append(row.Index.ToString().PadLeft(2, '0'))
                .Append(" w").Append(row.Width).Append("  ")
                .Append(rooms.PadRight(10)).Append("  lanes ").AppendLine(lanes);
        }

        foreach (var shortfall in Shortfalls)
            text.Append("shortfall ").AppendLine(shortfall.ToString());
        foreach (var forced in Forced)
            text.Append("forced ").AppendLine(forced.ToString());
        return text.ToString();
    }
}
