namespace RogueDeck.Run;

// A generated map plus the generator role each node was built as. The RunMap only stores NodeType + payload, and a
// Combat and an Elite node share the SAME node type ("combat" + EncounterRef) — so the role, which the constraint
// checker counts, is carried alongside here rather than baked into the node. Roles are a generation-time concern;
// nothing downstream of generation needs them (the placed encounter already carries the fight's real difficulty).
public sealed record GeneratedMap(RunMap Map, IReadOnlyDictionary<NodeId, MapNodeKind> Roles);

// Builds a run's map from a MapGenerationSpec: a layered act whose per-path minimums are GUARANTEED by construction
// (every entry→boss path visits one node per row, so a reserved full row of a kind gives every path exactly one of
// it), whose non-reserved rows add per-column variety, and whose combat/elite/boss nodes draw a balanced encounter
// (loadout + threat kept near a depth target). Deterministic from `seed`; the caller's `content` delegate realizes
// each (role, coord, chosen-encounter) into a concrete Node (EncounterRef for a fight, ShopRef / EventRef / … for
// the rest). Output always passes RunMapValidator and MapConstraintValidator for the same spec.
public static class RuleBasedMapGenerator
{
    // `content` receives the node's role, its coordinate, and — for a combat/elite/boss role that has encounter
    // candidates — the EncounterId the generator selected (null otherwise, so the caller supplies a non-combat ref).
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

        var rng = new MapGenRandom(seed);
        var selector = new EncounterSelector(spec.Encounters, balance);

        // Fixed kind per row (null = a varied row whose columns each draw a kind). Reserved rows meet the per-path
        // minimums; their placement among the middle rows is shuffled by the seed.
        var rowPlan = BuildRowPlan(spec, rng);

        var widths = new int[spec.Rows + 1];
        for (var r = 0; r < spec.Rows; r++)
            widths[r] = spec.MinWidth + rng.Next(spec.MaxWidth - spec.MinWidth + 1);
        widths[spec.Rows] = 1; // boss

        var builder = new RunMapBuilder();
        var roles = new Dictionary<NodeId, MapNodeKind>();
        for (var r = 0; r <= spec.Rows; r++)
            for (var c = 0; c < widths[r]; c++)
            {
                var coord = new MapCoord(r, c);
                var kind = rowPlan[r] ?? DrawVariedKind(spec, rng);
                var encounter = SelectEncounter(kind, r, spec, startingLoadout, selector, rng);
                var realized = content(kind, coord, encounter);
                var id = new NodeId(MapWiring.Id(r, c));
                builder.AddNode(id, realized.Type, realized.Payload);
                roles[id] = kind;
            }

        for (var c = 0; c < widths[0]; c++)
            builder.Entry(MapWiring.Id(0, c));

        for (var r = 0; r < spec.Rows; r++)
            MapWiring.WireRows(builder, r, widths[r], widths[r + 1], rng);

        return new GeneratedMap(builder.Build(), roles);
    }

    // Row 0 is the entry Combat, row Rows is the Boss; the middle rows hold the reserved full-kind rows followed by
    // varied rows, shuffled deterministically so shop/elite/… rows land in different places per seed.
    private static MapNodeKind?[] BuildRowPlan(MapGenerationSpec spec, MapGenRandom rng)
    {
        var plan = new MapNodeKind?[spec.Rows + 1];
        plan[0] = MapNodeKind.Combat;
        plan[spec.Rows] = MapNodeKind.Boss;

        var middle = new List<MapNodeKind?>();
        var reserved = spec.ReservedRows();
        foreach (var kind in MapGenerationSpec.ReservableKinds)
            for (var i = 0; i < reserved[kind]; i++)
                middle.Add(kind);
        for (var varied = spec.AvailableMiddleRows() - middle.Count; varied > 0; varied--)
            middle.Add(null);

        Shuffle(middle, rng);
        for (var i = 0; i < middle.Count; i++)
            plan[1 + i] = middle[i];
        return plan;
    }

    // A per-column kind for a varied row, weighted by spec.KindWeights. Iterates the fixed ReservableKinds order
    // (not the dictionary's) so the draw is reproducible regardless of how KindWeights was constructed / round-tripped.
    private static MapNodeKind DrawVariedKind(MapGenerationSpec spec, MapGenRandom rng)
    {
        var total = 0;
        foreach (var kind in MapGenerationSpec.ReservableKinds)
            total += Weight(spec, kind);
        if (total <= 0)
            return MapNodeKind.Combat;

        var roll = rng.Next(total);
        var cumulative = 0;
        foreach (var kind in MapGenerationSpec.ReservableKinds)
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
        MapNodeKind kind, int row, MapGenerationSpec spec, int startingLoadout, EncounterSelector selector,
        MapGenRandom rng)
    {
        if (!IsCombatRole(kind) || !selector.HasCandidates(kind))
            return null;

        var loadout = spec.BalanceTargets.AssumedLoadout(startingLoadout, row);
        var target = spec.BalanceTargets.TargetNet(row);
        return selector.Select(kind, loadout, target, spec.BalanceTargets.Tolerance, rng.Next);
    }

    private static bool IsCombatRole(MapNodeKind kind) =>
        kind is MapNodeKind.Combat or MapNodeKind.Elite or MapNodeKind.Boss;

    private static void Shuffle<T>(IList<T> list, MapGenRandom rng)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
