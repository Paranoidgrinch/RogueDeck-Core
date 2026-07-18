namespace RogueDeck.Run;

// How the difficulty band tracks depth — the balancing hook's tuning. At row r the generator assumes the player's
// loadout is `StartingLoadout + LoadoutGrowthPerRow * r` and aims each fight's NET difficulty (that loadout + the
// encounter's negative threat) at `StartNet - NetDropPerRow * r`, searching within ±Tolerance. Dropping the target
// and/or the assumed growth deeper makes later fights harder while staying winnable. All-zero (the default) ⇒ a
// flat target; the selector then just draws near StartNet, or falls back to the closest encounter.
public sealed record BalanceTargets
{
    public int StartNet { get; init; }
    public int NetDropPerRow { get; init; }
    public int LoadoutGrowthPerRow { get; init; }
    public int Tolerance { get; init; } = 20;

    // The assumed loadout strength and the target net at a given row depth.
    public int AssumedLoadout(int startingLoadout, int row) => startingLoadout + LoadoutGrowthPerRow * row;
    public int TargetNet(int row) => StartNet - NetDropPerRow * row;
}

// The rules a run's map is generated from (RuleBasedMapGenerator). A layered act of `Rows` pre-boss rows (row 0 is
// the entry, one node per column) plus a single boss row. Per-path minimums are GUARANTEED constructively: because
// every entry→boss path visits exactly one node per row, reserving a whole row for a kind gives every path exactly
// one of that kind — so `PerPathMinimums[Elite] = 2` reserves two full Elite rows, etc. Rows not reserved for a
// minimum are "varied": each column draws a kind from `KindWeights`, so paths genuinely differ (fork/merge matters)
// ABOVE the guaranteed floor. Encounters for combat/elite/boss nodes are drawn from `Encounters` and balanced via
// `BalanceTargets`. A plain record, so it round-trips through RunJson.
public sealed record MapGenerationSpec
{
    public int Rows { get; init; } = 5;
    public int MinWidth { get; init; } = 2;
    public int MaxWidth { get; init; } = 4;

    // Guaranteed count of each kind on EVERY entry→boss path (reserved full rows). Kinds not listed default to 0.
    public IReadOnlyDictionary<MapNodeKind, int> PerPathMinimums { get; init; } = EmptyCounts;

    // Guaranteed enemies (Combat + Elite nodes) on every path. Met by reserving extra full Combat rows on top of
    // the entry row and any reserved Elite rows.
    public int MinEnemiesPerPath { get; init; }

    // Optional whole-graph minimums (totals across all nodes, not per path) — validated by MapConstraintValidator.
    public IReadOnlyDictionary<MapNodeKind, int> MapWideMinimums { get; init; } = EmptyCounts;

    // Per-column kind weights for varied rows. Default: Combat-heavy with a little Event. Boss/Combat handling for
    // the entry and boss rows is fixed and not drawn from here.
    public IReadOnlyDictionary<MapNodeKind, int> KindWeights { get; init; } = DefaultWeights;

    public EncounterDistribution Encounters { get; init; } = new();
    public BalanceTargets BalanceTargets { get; init; } = new();

    // Which authored content a NON-combat generated node references, by role → id: a Shop id, a Workbench id, or an
    // Event id (Event / Rest / Treasure roles all resolve to an authored event by default). Combat / Elite / Boss
    // nodes ignore this — their encounter is drawn from Encounters. A role that can appear (reserved or weighted)
    // but has no ref here makes generation fail with a clear message (RunDocumentValidator flags it earlier).
    public IReadOnlyDictionary<MapNodeKind, string> NodeRefs { get; init; } = new Dictionary<MapNodeKind, string>();

    // The placeable kinds a full row can be reserved for (to meet a per-path minimum). Boss is the fixed top row;
    // it is never reserved here.
    public static readonly IReadOnlyList<MapNodeKind> ReservableKinds = new[]
    {
        MapNodeKind.Combat, MapNodeKind.Elite, MapNodeKind.Event, MapNodeKind.Shop,
        MapNodeKind.Rest, MapNodeKind.Treasure, MapNodeKind.Workbench,
    };

    // How many reserved FULL rows each placeable kind needs to meet the per-path minimums, including the extra
    // Combat rows the enemy floor demands (the entry row and reserved Elite rows already count as enemies).
    public IReadOnlyDictionary<MapNodeKind, int> ReservedRows()
    {
        var reserved = new Dictionary<MapNodeKind, int>();
        foreach (var kind in ReservableKinds)
            reserved[kind] = PerPathMinimums.TryGetValue(kind, out var min) ? Math.Max(0, min) : 0;

        var guaranteedEnemies = 1 + reserved[MapNodeKind.Combat] + reserved[MapNodeKind.Elite];
        if (guaranteedEnemies < MinEnemiesPerPath)
            reserved[MapNodeKind.Combat] += MinEnemiesPerPath - guaranteedEnemies;

        return reserved;
    }

    // Total reserved full rows among the pre-boss rows 1..Rows-1 (row 0 is the fixed entry combat).
    public int RequiredMiddleRows() => ReservedRows().Values.Sum();

    // Pre-boss rows available for reservation (all rows except the fixed entry row 0).
    public int AvailableMiddleRows() => Math.Max(0, Rows - 1);

    public bool IsFeasible() => RequiredMiddleRows() <= AvailableMiddleRows();

    public void Validate()
    {
        if (Rows < 1)
            throw new ArgumentOutOfRangeException(nameof(Rows), Rows, "An act needs at least one row before the boss.");
        if (MinWidth < 1)
            throw new ArgumentOutOfRangeException(nameof(MinWidth), MinWidth, "Row width must be at least 1.");
        if (MaxWidth < MinWidth)
            throw new ArgumentOutOfRangeException(nameof(MaxWidth), MaxWidth, "MaxWidth must be >= MinWidth.");
        if (!IsFeasible())
            throw new InvalidOperationException(
                $"Map spec is infeasible: the per-path minimums need {RequiredMiddleRows()} reserved rows plus the "
                + $"entry row, but Rows = {Rows} leaves only {AvailableMiddleRows()} for reservation. "
                + "Increase Rows or lower the minimums.");
    }

    private static readonly IReadOnlyDictionary<MapNodeKind, int> EmptyCounts = new Dictionary<MapNodeKind, int>();

    private static readonly IReadOnlyDictionary<MapNodeKind, int> DefaultWeights = new Dictionary<MapNodeKind, int>
    {
        [MapNodeKind.Combat] = 7,
        [MapNodeKind.Event] = 3,
    };
}
