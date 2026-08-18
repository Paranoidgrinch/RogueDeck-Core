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

// The rules a run's map is generated from (RuleBasedMapGenerator). The act is a backbone of `Rows` WIDE "branch"
// rows (row 0 is the entry) plus a single boss row, where each branch node draws its kind independently from
// `KindWeights` — so both a row's columns and the paths through it genuinely differ. Per-path minimums and the
// enemy floor are met by inserting narrow width-1 GATE rows (funnels every path crosses), and only as many as the
// worst path still needs after crediting what the varied rows already provide (so a combat-rich backbone adds no
// pointless combat funnels). Encounters for combat/elite/boss nodes are drawn from `Encounters` and balanced via
// `BalanceTargets`. Always buildable (gates are always insertable — no infeasibility). A plain record, so it
// round-trips through RunJson.
public sealed record MapGenerationSpec
{
    // The branching backbone length: how many WIDE varied rows the act has (before any inserted gate funnels and the
    // boss). More rows ⇒ a taller, branchier map.
    public int Rows { get; init; } = 5;
    public int MinWidth { get; init; } = 2;
    public int MaxWidth { get; init; } = 4;

    // Guaranteed count of each kind on EVERY entry→boss path. Met by width-1 gate funnels, minus what the varied
    // rows already guarantee on the worst path. Kinds not listed default to 0.
    public IReadOnlyDictionary<MapNodeKind, int> PerPathMinimums { get; init; } = EmptyCounts;

    // Guaranteed enemies (Combat + Elite nodes) on every path. Met by adding Combat gate funnels for whatever the
    // varied rows and the elite gates don't already guarantee on the worst path.
    public int MinEnemiesPerPath { get; init; }

    // Optional whole-graph minimums (totals across all nodes, not per path) — validated by MapConstraintValidator.
    public IReadOnlyDictionary<MapNodeKind, int> MapWideMinimums { get; init; } = EmptyCounts;

    // Per-column kind weights for the WIDE branch rows. Default: Combat-heavy with a little Event. The boss row is
    // fixed and never drawn from here.
    public IReadOnlyDictionary<MapNodeKind, int> KindWeights { get; init; } = DefaultWeights;

    public EncounterDistribution Encounters { get; init; } = new();
    public BalanceTargets BalanceTargets { get; init; } = new();

    // Chance (0–100) that a generated Treasure node is instead a MIMIC: a combat drawn from Encounters[Mimic]
    // (tuned ≈ a weak elite of this act). 0 = never. Rise it per act (e.g. 5/10/15/20 across Acts I–IV) so
    // treasure gets progressively riskier. Requires at least one Encounters[Mimic] candidate when > 0.
    public int TreasureMimicChancePercent { get; init; }

    // Which authored content a NON-combat generated node references, by role → id: a Shop id, a Workbench id, or an
    // Event id (Event / Rest / Treasure roles all resolve to an authored event by default). Combat / Elite / Boss
    // nodes ignore this — their encounter is drawn from Encounters. A role that can appear (a gate or a positive
    // weight) but has no ref here makes generation fail with a clear message (RunDocumentValidator flags it earlier).
    public IReadOnlyDictionary<MapNodeKind, string> NodeRefs { get; init; } = new Dictionary<MapNodeKind, string>();

    // Optional per-kind POOLS of authored ref ids for non-combat roles (Event/Rest/Treasure/Shop/Workbench). When
    // a kind has a non-empty pool here, each such node draws a DISTINCT ref without replacement (so a path can hold
    // several different events, not the same one repeated) — the non-combat analogue of the Encounters pools. Falls
    // back to the single NodeRefs[kind] when a kind has no pool. Pools are drawn from the same deterministic content
    // RNG; leaving this empty keeps generation byte-identical to before.
    public IReadOnlyDictionary<MapNodeKind, IReadOnlyList<string>> NodeRefPools { get; init; } =
        new Dictionary<MapNodeKind, IReadOnlyList<string>>();

    // The kinds a gate funnel can be, in a fixed order (used to lay gates out and to iterate deterministically).
    // Boss is the fixed top row and is never a per-path gate.
    public static readonly IReadOnlyList<MapNodeKind> GateKinds = new[]
    {
        MapNodeKind.Combat, MapNodeKind.MultiCombat, MapNodeKind.Elite, MapNodeKind.Event, MapNodeKind.Shop,
        MapNodeKind.Rest, MapNodeKind.Treasure, MapNodeKind.Workbench,
    };

    // Every kind that can appear on a generated map: Combat + Boss always, plus any kind with a per-path minimum
    // (a gate) or a positive branch weight. Used to check that each appearing kind has resolvable content.
    public IReadOnlyCollection<MapNodeKind> AppearingKinds()
    {
        var kinds = new HashSet<MapNodeKind> { MapNodeKind.Combat, MapNodeKind.Boss };
        foreach (var (kind, min) in PerPathMinimums)
            if (min > 0)
                kinds.Add(kind);
        if (MinEnemiesPerPath > 0)
            kinds.Add(MapNodeKind.Combat);
        foreach (var (kind, weight) in KindWeights)
            if (weight > 0)
                kinds.Add(kind);
        return kinds;
    }

    public void Validate()
    {
        if (Rows < 1)
            throw new ArgumentOutOfRangeException(nameof(Rows), Rows, "An act needs at least one branch row.");
        if (MinWidth < 1)
            throw new ArgumentOutOfRangeException(nameof(MinWidth), MinWidth, "Row width must be at least 1.");
        if (MaxWidth < MinWidth)
            throw new ArgumentOutOfRangeException(nameof(MaxWidth), MaxWidth, "MaxWidth must be >= MinWidth.");
    }

    private static readonly IReadOnlyDictionary<MapNodeKind, int> EmptyCounts = new Dictionary<MapNodeKind, int>();

    private static readonly IReadOnlyDictionary<MapNodeKind, int> DefaultWeights = new Dictionary<MapNodeKind, int>
    {
        [MapNodeKind.Combat] = 7,
        [MapNodeKind.Event] = 3,
    };
}
