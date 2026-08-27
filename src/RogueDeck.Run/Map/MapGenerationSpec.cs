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

// The spoils a generated fight of one role grants. Mirrors the three reward fields of EncounterRef; the id is
// suffixed with the encounter so two fights of the same role stay distinguishable in the run log.
public sealed record MapVictoryReward(IRewardSource Source, string RewardIdPrefix = "spoils", int PickCount = 1);

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

    // Ceiling of each kind on EVERY entry→boss path: no path may hold more than this. Where the varied rows
    // overshoot, the generator rewrites the deepest offending nodes into ordinary Combat until the ceiling
    // holds. Kinds not listed are unlimited. A maximum below the matching minimum is a spec error.
    public IReadOnlyDictionary<MapNodeKind, int> PerPathMaximums { get; init; } = EmptyCounts;

    // How a guarantee row is drawn. A per-path minimum is met by a row EVERY path crosses; by default that row
    // is a width-1 funnel, which is a readable landmark but pinches the map shut wherever a guarantee lands.
    // With this on, the row keeps the map's width and simply fills every column with that kind: the guarantee
    // is just as absolute (a path crosses exactly one node per row) while the branching — and with it the
    // variety between routes — survives. The nodes still draw their own content, so the columns differ.
    public bool WideGuaranteeRows { get; init; }

    // Column FLAVOURS. With lanes, a map's columns are not drawn from one table: column c takes its weights
    // from LaneProfiles[c % count], so the left of the map can be a combat gauntlet while the right is an
    // errand run — paths that keep to a side differ in what they hold AND in the order they hold it. Empty
    // (the default) = every column uses KindWeights, exactly as before.
    public IReadOnlyList<MapLaneProfile> LaneProfiles { get; init; } = Array.Empty<MapLaneProfile>();

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

    // What a generated FIGHT pays out, per role. A generated map's combat nodes otherwise carry a bare
    // EncounterRef and grant nothing on victory, which quietly costs a procedural act its whole reward economy
    // (an authored map states the spoils on each EncounterRef). Roles without an entry pay nothing, exactly as
    // before; Elite/Boss usually pay more than Combat.
    public IReadOnlyDictionary<MapNodeKind, MapVictoryReward> VictoryRewards { get; init; } =
        new Dictionary<MapNodeKind, MapVictoryReward>();

    // What ONE named encounter pays out, overriding its role's entry above. A role-wide table cannot express
    // "this boss hands you one of ITS three relics" — every boss of the act would hand out the same pool — and
    // that is how designs usually treat their bosses. Keyed by encounter id; the most specific reward wins, so
    // an entry here states the WHOLE payout for that fight (its gold and cards included), not an addition to
    // the role's. Empty (the default) ⇒ every fight pays what its role pays, exactly as before.
    public IReadOnlyDictionary<string, MapVictoryReward> VictoryRewardsByEncounter { get; init; } =
        new Dictionary<string, MapVictoryReward>();

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

    // How DEEP into the act one authored ref may first appear, as a percentage of the act's own depth (0 = from
    // the entry row on, 100 = only the last row before the boss). A design that gates its doors by stage — "the
    // Librarian at the end of the aisle, earliest stage 8" — cannot say that with NodeRefPools alone: a pool
    // draws with no notion of where the node sits, so the deepest room can open on the first step. Keyed by REF
    // id rather than by kind, because the gate belongs to the content, not to the role. Refs with no entry may
    // appear anywhere. A percentage rather than a row index because the generated map is TALLER than the act's
    // branch backbone (gate funnels are inserted into it), so a row number authored against the design's stage
    // count would land at the wrong depth. Where a row can honour no ref of a pool at all, the gate yields: a
    // node is never left without content. Empty (the default) leaves generation byte-identical.
    public IReadOnlyDictionary<string, int> NodeRefMinimumDepthPercent { get; init; } =
        new Dictionary<string, int>();

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
        foreach (var lane in LaneProfiles)
            foreach (var (kind, weight) in lane.KindWeights)
                if (weight > 0)
                    kinds.Add(kind);
        // A treasure that can flip into a mimic makes that role appear too — it draws an encounter like any
        // other fight, and a spec without candidates for it would only fail once a run rolled one.
        if (TreasureMimicChancePercent > 0)
            kinds.Add(MapNodeKind.Mimic);
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

        foreach (var (kind, max) in PerPathMaximums)
        {
            if (max < 0)
                throw new ArgumentOutOfRangeException(nameof(PerPathMaximums), max,
                    $"A per-path maximum cannot be negative ({kind}).");
            if (PerPathMinimums.TryGetValue(kind, out var min) && min > max)
                throw new ArgumentOutOfRangeException(nameof(PerPathMaximums), max,
                    $"The per-path maximum for {kind} ({max}) is below its minimum ({min}).");
        }

        foreach (var lane in LaneProfiles)
            if (lane.KindWeights.Count == 0)
                throw new ArgumentException($"Lane '{lane.Name}' has no kind weights.", nameof(LaneProfiles));

        foreach (var (nodeRef, percent) in NodeRefMinimumDepthPercent)
            if (percent is < 0 or > 100)
                throw new ArgumentOutOfRangeException(nameof(NodeRefMinimumDepthPercent), percent,
                    $"The earliest depth for ref '{nodeRef}' must be a percentage (0-100).");
    }

    private static readonly IReadOnlyDictionary<MapNodeKind, int> EmptyCounts = new Dictionary<MapNodeKind, int>();


    private static readonly IReadOnlyDictionary<MapNodeKind, int> DefaultWeights = new Dictionary<MapNodeKind, int>
    {
        [MapNodeKind.Combat] = 7,
        [MapNodeKind.Event] = 3,
    };
}

// The weights one column FLAVOUR draws from (see MapGenerationSpec.LaneProfiles). Name is for authoring and
// the Studio preview only — generation reads the weights.
public sealed record MapLaneProfile(string Name, IReadOnlyDictionary<MapNodeKind, int> KindWeights);
