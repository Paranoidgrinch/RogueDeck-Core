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
        Func<MapNodeKind, MapCoord, EncounterId?, string?, NodeContent> content)
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

        // Find the gate multiset that satisfies every per-path constraint, crediting the branch rows — and hold
        // the per-path CEILINGS while doing it: an overshooting branch node is rewritten before the next pass, so
        // gates and ceilings settle together.
        var gateCounts = new Dictionary<MapNodeKind, int>();
        for (var iteration = 0; iteration < MaxGateIterations; iteration++)
        {
            var (trialMap, trialRoles, trialGuaranteed) = Build(spec, branches, gateCounts, wireSeed);
            var changed = AccumulateDeficits(spec, trialMap, trialRoles, trialGuaranteed, gateCounts);
            changed |= EnforceMaximums(spec, branches, trialMap, trialRoles);
            if (!changed)
                break;
        }

        var (map, roles, _) = Build(
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
                kinds[r][c] = DrawVariedKind(spec, rng, c);
        }
        return new Branches(widths, kinds);
    }

    // ── Gate sizing (crediting the varied rows) ──────────────────────────────────────────────────────────
    // Raises gateCounts to cover any remaining per-path / enemy shortfall on the current map. Returns true if it
    // changed anything (so the caller rebuilds and re-checks). Map-wide minimums are advisory (validator-only).
    private static bool AccumulateDeficits(
        MapGenerationSpec spec, RunMap map, IReadOnlyDictionary<NodeId, MapNodeKind> roles,
        IReadOnlySet<NodeId> guaranteed, Dictionary<MapNodeKind, int> gateCounts)
    {
        var added = false;

        var pendingCombat = 0;
        var pendingElite = 0;
        foreach (var (kind, min) in spec.PerPathMinimums)
        {
            if (min <= 0)
                continue;
            var kindLocal = kind;
            // A treasure drawn by a branch row may still turn out to be a mimic, so it cannot be counted
            // toward the treasure promise — only the ones the guarantee rows put there can.
            var flippable = kindLocal == MapNodeKind.Treasure && spec.TreasureMimicChancePercent > 0;
            var deficit = min - MapConstraintValidator.WorstPathCount(
                map, roles, (id, k) => k == kindLocal && (!flippable || guaranteed.Contains(id)));
            if (deficit > 0)
            {
                gateCounts[kind] = gateCounts.GetValueOrDefault(kind) + deficit;
                added = true;
                if (kind is MapNodeKind.Combat or MapNodeKind.MultiCombat) pendingCombat += deficit;
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
        Func<MapNodeKind, MapCoord, EncounterId?, string?, NodeContent> Content);

    private static (RunMap Map, Dictionary<NodeId, MapNodeKind> Roles, HashSet<NodeId> GuaranteedNodes) Build(
        MapGenerationSpec spec, Branches branches, IReadOnlyDictionary<MapNodeKind, int> gateCounts, int wireSeed,
        ContentRealization? realization = null)
    {
        var plan = AssembleRowPlan(branches.Widths.Length, gateCounts);
        var wire = new MapGenRandom(wireSeed);
        var contentRng = realization is null ? null : new MapGenRandom(realization.ContentSeed);

        // Encounter templates are drawn WITHOUT replacement across the whole map, so a run never meets the same
        // fight twice (until a role's pool is exhausted). Shared across roles: a template lives in one pool.
        var usedEncounters = new HashSet<EncounterId>();
        // Same for non-combat node refs (events/rest/treasure), per NodeRefPools — distinct nodes get distinct refs.
        var usedRefs = new HashSet<string>();

        var builder = new RunMapBuilder();
        var roles = new Dictionary<NodeId, MapNodeKind>();
        // Nodes that sit on a guarantee row: those are the promises, and only they may be counted toward a
        // per-path minimum when the kind can still change under the player's feet (a treasure→mimic flip).
        var guaranteed = new HashSet<NodeId>();

        for (var ri = 0; ri < plan.Count; ri++)
        {
            var row = plan[ri];
            var width = WidthOf(spec, row, branches);
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
                    // A treasure that sits on a GUARANTEE row never flips: that node is the promise "every path
                    // holds a treasure", and a mimic would quietly break it for the paths crossing it.
                    var effectiveKind = kind;
                    if (kind == MapNodeKind.Treasure && spec.TreasureMimicChancePercent > 0
                        && realization.Selector.HasCandidates(MapNodeKind.Mimic)
                        && contentRng!.Next(100) < spec.TreasureMimicChancePercent
                        && !row.IsGate)
                    {
                        effectiveKind = MapNodeKind.Mimic;
                    }

                    var encounter = SelectEncounter(effectiveKind, ri, spec, realization, contentRng!, usedEncounters);
                    var nodeRef = SelectNodeRef(effectiveKind, spec, contentRng!, usedRefs);
                    var realized = realization.Content(effectiveKind, new MapCoord(ri, c), encounter, nodeRef);
                    builder.AddNode(id, realized.Type, realized.Payload, realized.Tags);
                    kind = effectiveKind;
                }
                roles[id] = kind;
                if (row.IsGate)
                    guaranteed.Add(id);
            }
        }

        // Entries are the first row's columns (row 0 is always a branch row).
        for (var c = 0; c < branches.Widths[0]; c++)
            builder.Entry(MapWiring.Id(0, c));

        for (var ri = 0; ri < plan.Count - 1; ri++)
            MapWiring.WireRows(builder, ri, WidthOf(spec, plan[ri], branches), WidthOf(spec, plan[ri + 1], branches), wire);

        return (builder.Build(), roles, guaranteed);
    }

    // A guarantee row is a funnel (width 1) unless the spec asks it to keep the map's width, in which case it
    // borrows the width of the branch row it sits next to.
    private static int WidthOf(MapGenerationSpec spec, RowPlanRow row, Branches branches) =>
        row.IsGate
            ? (spec.WideGuaranteeRows && row.GateKind != MapNodeKind.Boss
                ? branches.Widths[Math.Min(row.BranchIndex, branches.Widths.Length - 1)]
                : 1)
            : branches.Widths[row.BranchIndex];

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
                plan.Add(new RowPlanRow(true, gates[gateIndex++], branchIndex));
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

    // ── Per-path ceilings ────────────────────────────────────────────────────────────────────────────────
    // Rewrites branch nodes until no path holds more of a kind than the spec allows. The DEEPEST offender goes
    // first (the map's opening keeps its promised flavour, the tail gives way), and it becomes Combat — the one
    // kind a ceiling is never put on in practice, and the honest filler for "this slot may not be that".
    // Returns true when something changed, so the caller rebuilds and re-measures.
    private static bool EnforceMaximums(
        MapGenerationSpec spec, Branches branches, RunMap map, IReadOnlyDictionary<NodeId, MapNodeKind> roles)
    {
        var changed = false;
        foreach (var (kind, max) in spec.PerPathMaximums)
        {
            if (kind == MapNodeKind.Combat)
                continue; // the filler cannot be capped by rewriting into itself
            var kindLocal = kind;
            if (MapConstraintValidator.RichestPathCount(map, roles, k => k == kindLocal) <= max)
                continue;

            for (var r = branches.Kinds.Length - 1; r >= 0; r--)
            {
                var rewritten = false;
                for (var c = branches.Kinds[r].Length - 1; c >= 0; c--)
                    if (branches.Kinds[r][c] == kind)
                    {
                        branches.Kinds[r][c] = MapNodeKind.Combat;
                        rewritten = true;
                        break;
                    }

                if (rewritten)
                {
                    changed = true;
                    break;
                }
            }
        }

        return changed;
    }

    // ── Varied-row kind draw + encounter selection ───────────────────────────────────────────────────────
    // `column` picks the lane: with LaneProfiles set, each column draws from its own flavour, so the left of the
    // map can be a gauntlet while the right is an errand run.
    private static MapNodeKind DrawVariedKind(MapGenerationSpec spec, MapGenRandom rng, int column)
    {
        var weights = LaneWeights(spec, column);

        var total = 0;
        foreach (var kind in MapGenerationSpec.GateKinds)
            total += Weight(weights, kind);
        if (total <= 0)
            return MapNodeKind.Combat;

        var roll = rng.Next(total);
        var cumulative = 0;
        foreach (var kind in MapGenerationSpec.GateKinds)
        {
            cumulative += Weight(weights, kind);
            if (roll < cumulative)
                return kind;
        }
        return MapNodeKind.Combat; // unreachable: roll < total
    }

    private static IReadOnlyDictionary<MapNodeKind, int> LaneWeights(MapGenerationSpec spec, int column) =>
        spec.LaneProfiles.Count == 0
            ? spec.KindWeights
            : spec.LaneProfiles[column % spec.LaneProfiles.Count].KindWeights;

    private static int Weight(IReadOnlyDictionary<MapNodeKind, int> weights, MapNodeKind kind) =>
        weights.TryGetValue(kind, out var value) ? Math.Max(0, value) : 0;

    private static EncounterId? SelectEncounter(
        MapNodeKind kind, int row, MapGenerationSpec spec, ContentRealization realization, MapGenRandom rng,
        ISet<EncounterId> used)
    {
        if (!IsCombatRole(kind) || !realization.Selector.HasCandidates(kind))
            return null;

        var loadout = spec.BalanceTargets.AssumedLoadout(realization.StartingLoadout, row);
        var target = spec.BalanceTargets.TargetNet(row);
        var picked = realization.Selector.Select(kind, loadout, target, spec.BalanceTargets.Tolerance, rng.Next, used);
        used.Add(picked);
        return picked;
    }

    // Picks the authored ref a non-combat node realizes as, from NodeRefPools[kind] WITHOUT replacement (so a path
    // holds distinct events/treasures/rests). Returns null for combat roles and for kinds with no pool (the realizer
    // then falls back to the single NodeRefs[kind]); consumes RNG only when a pool is actually drawn, so an empty
    // NodeRefPools leaves generation byte-identical.
    private static string? SelectNodeRef(
        MapNodeKind kind, MapGenerationSpec spec, MapGenRandom rng, ISet<string> used)
    {
        if (IsCombatRole(kind))
            return null;
        if (!spec.NodeRefPools.TryGetValue(kind, out var pool) || pool.Count == 0)
            return null;

        var available = pool.Where(r => !used.Contains(r)).ToList();
        if (available.Count == 0)
            available = pool.ToList(); // pool exhausted → allow reuse
        var picked = available[rng.Next(available.Count)];
        used.Add(picked);
        return picked;
    }

    private static bool IsCombatRole(MapNodeKind kind) =>
        kind is MapNodeKind.Combat or MapNodeKind.MultiCombat or MapNodeKind.Elite
            or MapNodeKind.Boss or MapNodeKind.Mimic;

    private static readonly NodeType TrialType = new("gen.trial");
    private const string TrialPayload = "";
}
