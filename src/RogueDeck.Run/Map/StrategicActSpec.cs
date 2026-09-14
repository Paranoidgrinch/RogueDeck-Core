namespace RogueDeck.Run;

// EVERYTHING THE STRATEGIC GENERATOR NEEDS TO KNOW ABOUT ONE ACT, in one place.
//
// Up to here the three stages each took their own arguments — rows and widths to the topology walk, lane profiles
// to the flavour pass, a room spec to the allocator — which is exactly right while each stage is being built and
// wrong the moment anything wants to ask a question about the WHOLE act. The validator is that moment: "can this
// act hold two shops in its opening quarter" is a question about the act's length, its widths, its depth gates,
// its budgets and its lanes at once, and a validator taking six loose parameters would be a validator nobody
// calls from a UI.
//
// So this is the act as one authored thing. S11 binds it onto `RunAct`/`RunBlueprint` and persists the choice of
// generator beside it; nothing here knows about documents, saves or Godot yet.
public sealed record StrategicActSpec
{
    // EVERY row the act has, the boss rooms included — the same meaning as StrategicTopologyGenerator's `rows`
    // and deliberately unlike the rule-based `MapGenerationSpec.Rows`, which counts only a backbone and then
    // grows by however many gate rows it needs. The number authored here is the number of rooms a route walks.
    public required int Rows { get; init; }

    public int BossRooms { get; init; } = 1;
    public int MinWidth { get; init; } = 2;
    public int MaxWidth { get; init; } = 4;
    public StrategicTopologyRules Topology { get; init; } = new();

    // The act's flavours, in authored order — and the order is meaningful: neighbours in this list are related
    // flavours a fork may step between (see StrategicStrandProfileRules).
    public required IReadOnlyList<MapLaneProfile> LaneProfiles { get; init; }

    public StrategicStrandProfileRules LaneAssignment { get; init; } = new();
    public StrategicRoomSpec Rooms { get; init; } = new();

    // WHAT A SINGLE ROUTE THROUGH THE ACT MUST ASK OF THE PLAYER. The act-wide budgets above say what the act
    // HOLDS and say nothing about what one walk through it meets; this is the other half, and it is the half the
    // per-path minimums used to be (see StrategicPathPressure).
    public PathPressureRules PathPressure { get; init; } = new();

    // WHETHER THE ACT'S FORKS ARE CHOICES. Measured, not yet enforced (see ForkQualityRules): what a fork is
    // worth is the one thing a branching act exists for and the one thing nothing used to count.
    public ForkQualityRules ForkQuality { get; init; } = new();

    public RepairRules Repair { get; init; } = new();

    // HOW MANY WHOLE ACTS MAY BE TRIED before the generator gives up and says why (source document §25). Each
    // retry is a fresh family of seed streams derived from the same run seed, so the fourth attempt at a hard
    // spec is as reproducible as the first. An act that promises nothing is clean on the first one.
    public int MaxGenerationAttempts { get; init; } = 8;

    // The rows that hold a room the allocator chooses. A boss room's content is fixed, so it is part of the act's
    // shape and never part of its supply.
    public int RowsBeforeBoss => Math.Max(0, Rows - Math.Max(0, BossRooms));

    // HOW DEEP EACH OF THOSE ROWS SITS, by MapDepth.Percent — the one measure every gate and every band is
    // authored against. The set is coarse for a short act and that is the whole reason a band can turn out to
    // hold nothing: a 4-row act has rows at 0 % and 50 % and nothing whatsoever between them, so a 25-50 % band
    // is a perfectly sensible sentence about an act that cannot hear it.
    public IReadOnlyList<int> RowDepthPercents =>
        Enumerable.Range(0, RowsBeforeBoss).Select(row => MapDepth.Percent(row, Rows)).ToList();

    // Malformed, as opposed to merely unsatisfiable: a spec that contradicts itself as a data structure throws
    // here, the way RoomBudget and StrategicRoomSpec do, while a spec that is well formed and impossible to
    // satisfy is REPORTED by StrategicActSpecValidator. The difference is who made the mistake — a caller passing
    // a negative width has a bug, an author asking for six elites in a four-room act has a design problem.
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Rows);
        ArgumentOutOfRangeException.ThrowIfNegative(BossRooms);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(BossRooms, Rows);
        ArgumentOutOfRangeException.ThrowIfLessThan(MinWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxWidth, MinWidth);
        ArgumentNullException.ThrowIfNull(Topology);
        ArgumentNullException.ThrowIfNull(LaneProfiles);
        ArgumentNullException.ThrowIfNull(LaneAssignment);
        ArgumentNullException.ThrowIfNull(Rooms);
        ArgumentNullException.ThrowIfNull(PathPressure);
        ArgumentNullException.ThrowIfNull(ForkQuality);
        ArgumentNullException.ThrowIfNull(Repair);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxGenerationAttempts);

        if (LaneProfiles.Count == 0)
            throw new ArgumentException("An act needs at least one lane profile to flavour its routes with.",
                nameof(LaneProfiles));
        for (var index = 0; index < LaneProfiles.Count; index++)
        {
            if (LaneProfiles[index] is null)
                throw new ArgumentException($"Lane profile {index} is null.", nameof(LaneProfiles));
            if (LaneProfiles[index].KindWeights is null || LaneProfiles[index].KindWeights.Count == 0)
                throw new ArgumentException(
                    $"Lane profile '{LaneProfiles[index].Name}' has no kind weights, so it cannot flavour anything.",
                    nameof(LaneProfiles));
        }

        Rooms.Validate();
        PathPressure.Validate();
        ForkQuality.Validate();
        Repair.Validate();
    }
}
