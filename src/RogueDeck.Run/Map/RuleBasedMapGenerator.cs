namespace RogueDeck.Run;

// A generated map plus the generator role each node was built as. The RunMap only stores NodeType + payload, and a
// Combat and an Elite node share the SAME node type ("combat" + EncounterRef) — so the role, which the constraint
// checker counts, is carried alongside here rather than baked into the node. Roles are a generation-time concern;
// nothing downstream of generation needs them (the placed encounter already carries the fight's real difficulty).
public sealed record GeneratedMap(RunMap Map, IReadOnlyDictionary<NodeId, MapNodeKind> Roles);

// Builds a run's map from a MapGenerationSpec. The act is a backbone of `Rows` WIDE "branch" rows (each column
// draws its own kind from KindWeights — so rows are heterogeneous and paths differ) plus a single boss row. Per-path
// minimums and the enemy floor are met by inserting width-1 GATE rows: narrow funnels every path crosses. Only as
// many gates as the WORST path still needs are added — the varied rows are credited first (via a reverse-topo DP),
// so a combat-rich backbone gets no pointless combat funnels. Deterministic from `seed`; the caller's `content`
// delegate realizes each (role, coord, chosen-encounter) into a concrete Node. Output always passes RunMapValidator
// and MapConstraintValidator for the same spec.
public static class RuleBasedMapGenerator
{
    // Guard so a pathological spec can never loop forever; each iteration adds ≥1 gate, and enough gates of a kind
    // trivially satisfy that kind, so convergence is well within this bound.
    private const int MaxGateIterations = 256;

    public static GeneratedMap Generate(
        MapGenerationSpec spec,
        int seed,
        int startingLoadout,
        BalanceCalculator balance,
        Func<MapNodeKind, MapCoord, EncounterId?, NodeContent> content)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(balance);
        ArgumentNullException.ThrowIfNull(content);
        spec.Validate();

        // Independent, reset-able RNG streams so the gate-count search (which rebuilds the topology repeatedly) never
        // disturbs the widths/kinds or the encounter picks.
        var kindsSeed = seed;
        var wireSeed = unchecked(seed * 31 + 7);
        var contentSeed = unchecked(seed * 31 + 13);

        var branches = DrawBranches(spec, kindsSeed);

        // Find the gate multiset that satisfies every per-path constraint, crediting the branch rows.
        var gateCounts = new Dictionary<MapNodeKind, int>();
        for (var iteration = 0; iteration < MaxGateIterations; iteration++)
        {
            var (trialMap, trialRoles) = Build(spec, branches, gateCounts, wireSeed);
            if (!AccumulateDeficits(spec, trialMap, trialRoles, gateCounts))
                break;
        }

        var (map, roles) = Build(
            spec, branches, gateCounts, wireSeed,
            new ContentRealization(new EncounterSelector(spec.Encounters, balance), startingLoadout, contentSeed, content));
        return new GeneratedMap(map, roles);
    }

    // ── Branch backbone ─────────────────────────────────────────────────────────────────────────────────
    private sealed record Branches(int[] Widths, MapNodeKind[][] Kinds);

    private static Branches DrawBranches(MapGenerationSpec spec, int seed)
    {
        var rng = new MapGenRandom(seed);
        var widths = new int[spec.Rows];
        var kinds = new MapNodeKind[spec.Rows][];
        for (var r = 0; r < spec.Rows; r++)
        {
            var width = spec.MinWidth + rng.Next(spec.MaxWidth - spec.MinWidth + 1);
            widths[r] = width;
            kinds[r] = new MapNodeKind[width];
            for (var c = 0; c < width; c++)
                kinds[r][c] = DrawVariedKind(spec, rng);
        }
        return new Branches(widths, kinds);
    }

    // ── Gate sizing (crediting the varied rows) ──────────────────────────────────────────────────────────
    // Raises gateCounts to cover any remaining per-path / enemy shortfall on the current map. Returns true if it
    // changed anything (so the caller rebuilds and re-checks). Map-wide minimums are advisory (validator-only).
    private static bool AccumulateDeficits(
        MapGenerationSpec spec, RunMap map, IReadOnlyDictionary<NodeId, MapNodeKind> roles,
        Dictionary<MapNodeKind, int> gateCounts)
    {
        var added = false;

        var pendingCombat = 0;
        var pendingElite = 0;
        foreach (var (kind, min) in spec.PerPathMinimums)
        {
            if (min <= 0)
                continue;
            var kindLocal = kind;
            var deficit = min - MapConstraintValidator.WorstPathCount(map, roles, k => k == kindLocal);
            if (deficit > 0)
            {
                gateCounts[kind] = gateCounts.GetValueOrDefault(kind) + deficit;
                added = true;
                if (kind == MapNodeKind.Combat) pendingCombat += deficit;
                if (kind == MapNodeKind.Elite) pendingElite += deficit;
            }
        }

        if (spec.MinEnemiesPerPath > 0)
        {
            var worst = MapConstraintValidator.WorstPathCount(map, roles, MapConstraintValidator.IsEnemyRole);
            // Credit the enemy gates this pass already added (each is on every path).
            var enemyDeficit = spec.MinEnemiesPerPath - worst - pendingCombat - pendingElite;
            if (enemyDeficit > 0)
            {
                gateCounts[MapNodeKind.Combat] = gateCounts.GetValueOrDefault(MapNodeKind.Combat) + enemyDeficit;
                added = true;
            }
        }

        return added;
    }

    // ── Build (topology + optional content) ──────────────────────────────────────────────────────────────
    private sealed record ContentRealization(
        EncounterSelector Selector, int StartingLoadout, int ContentSeed,
        Func<MapNodeKind, MapCoord, EncounterId?, NodeContent> Content);

    private static (RunMap Map, Dictionary<NodeId, MapNodeKind> Roles) Build(
        MapGenerationSpec spec, Branches branches, IReadOnlyDictionary<MapNodeKind, int> gateCounts, int wireSeed,
        ContentRealization? realization = null)
    {
        var plan = AssembleRowPlan(branches.Widths.Length, gateCounts);
        var wire = new MapGenRandom(wireSeed);
        var contentRng = realization is null ? null : new MapGenRandom(realization.ContentSeed);

        var builder = new RunMapBuilder();
        var roles = new Dictionary<NodeId, MapNodeKind>();

        for (var ri = 0; ri < plan.Count; ri++)
        {
            var row = plan[ri];
            var width = row.IsGate ? 1 : branches.Widths[row.BranchIndex];
            for (var c = 0; c < width; c++)
            {
                var kind = row.IsGate ? row.GateKind : branches.Kinds[row.BranchIndex][c];
                var id = new NodeId(MapWiring.Id(ri, c));
                if (realization is null)
                {
                    builder.AddNode(id, TrialType, TrialPayload);
                }
                else
                {
                    // A Treasure node flips into a Mimic combat with a per-act chance. The roll consumes the
                    // content RNG unconditionally on Treasure nodes so map layout stays deterministic per seed.
                    var effectiveKind = kind;
                    if (kind == MapNodeKind.Treasure && spec.TreasureMimicChancePercent > 0
                        && realization.Selector.HasCandidates(MapNodeKind.Mimic)
                        && contentRng!.Next(100) < spec.TreasureMimicChancePercent)
                    {
                        effectiveKind = MapNodeKind.Mimic;
                    }

                    var encounter = SelectEncounter(effectiveKind, ri, spec, realization, contentRng!);
                    var realized = realization.Content(effectiveKind, new MapCoord(ri, c), encounter);
                    builder.AddNode(id, realized.Type, realized.Payload);
                    kind = effectiveKind;
                }
                roles[id] = kind;
            }
        }

        // Entries are the first row's columns (row 0 is always a branch row).
        for (var c = 0; c < branches.Widths[0]; c++)
            builder.Entry(MapWiring.Id(0, c));

        for (var ri = 0; ri < plan.Count - 1; ri++)
            MapWiring.WireRows(builder, ri, WidthOf(plan[ri], branches), WidthOf(plan[ri + 1], branches), wire);

        return (builder.Build(), roles);
    }

    private static int WidthOf(RowPlanRow row, Branches branches) =>
        row.IsGate ? 1 : branches.Widths[row.BranchIndex];

    // ── Row plan: branch rows (row 0 first) + spread gate funnels + boss ─────────────────────────────────
    private readonly record struct RowPlanRow(bool IsGate, MapNodeKind GateKind, int BranchIndex);

    private static IReadOnlyList<RowPlanRow> AssembleRowPlan(
        int branchCount, IReadOnlyDictionary<MapNodeKind, int> gateCounts)
    {
        var gates = FlattenGates(gateCounts);
        var plan = new List<RowPlanRow> { new(false, default, 0) }; // row 0 = the first branch (entries)

        // Spread the gates as evenly as possible among the remaining branch rows.
        var tail = branchCount - 1 + gates.Count;
        var gateIndex = 0;
        var branchIndex = 1;
        for (var slot = 0; slot < tail; slot++)
        {
            var isGate = gateIndex < gates.Count
                && ((slot + 1) * gates.Count / tail) > (slot * gates.Count / tail);
            if (isGate)
                plan.Add(new RowPlanRow(true, gates[gateIndex++], 0));
            else
                plan.Add(new RowPlanRow(false, default, branchIndex++));
        }

        plan.Add(new RowPlanRow(true, MapNodeKind.Boss, 0)); // the single boss leaf
        return plan;
    }

    // A flat gate order that round-robins the kinds (so like gates don't clump), in the fixed GateKinds order.
    private static IReadOnlyList<MapNodeKind> FlattenGates(IReadOnlyDictionary<MapNodeKind, int> gateCounts)
    {
        var remaining = new Dictionary<MapNodeKind, int>(gateCounts);
        var gates = new List<MapNodeKind>();
        bool any;
        do
        {
            any = false;
            foreach (var kind in MapGenerationSpec.GateKinds)
                if (remaining.GetValueOrDefault(kind) > 0)
                {
                    gates.Add(kind);
                    remaining[kind]--;
                    any = true;
                }
        } while (any);
        return gates;
    }

    // ── Varied-row kind draw + encounter selection ───────────────────────────────────────────────────────
    private static MapNodeKind DrawVariedKind(MapGenerationSpec spec, MapGenRandom rng)
    {
        var total = 0;
        foreach (var kind in MapGenerationSpec.GateKinds)
            total += Weight(spec, kind);
        if (total <= 0)
            return MapNodeKind.Combat;

        var roll = rng.Next(total);
        var cumulative = 0;
        foreach (var kind in MapGenerationSpec.GateKinds)
        {
            cumulative += Weight(spec, kind);
            if (roll < cumulative)
                return kind;
        }
        return MapNodeKind.Combat; // unreachable: roll < total
    }

    private static int Weight(MapGenerationSpec spec, MapNodeKind kind) =>
        spec.KindWeights.TryGetValue(kind, out var value) ? Math.Max(0, value) : 0;

    private static EncounterId? SelectEncounter(
        MapNodeKind kind, int row, MapGenerationSpec spec, ContentRealization realization, MapGenRandom rng)
    {
        if (!IsCombatRole(kind) || !realization.Selector.HasCandidates(kind))
            return null;

        var loadout = spec.BalanceTargets.AssumedLoadout(realization.StartingLoadout, row);
        var target = spec.BalanceTargets.TargetNet(row);
        return realization.Selector.Select(kind, loadout, target, spec.BalanceTargets.Tolerance, rng.Next);
    }

    private static bool IsCombatRole(MapNodeKind kind) =>
        kind is MapNodeKind.Combat or MapNodeKind.Elite or MapNodeKind.Boss or MapNodeKind.Mimic;

    private static readonly NodeType TrialType = new("gen.trial");
    private const string TrialPayload = "";
}
