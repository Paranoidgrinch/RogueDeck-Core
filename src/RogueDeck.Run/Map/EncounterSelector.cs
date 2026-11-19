namespace RogueDeck.Run;

// Picks the concrete encounter a generated node runs. Among a role's weighted candidates, it prefers those whose
// NET difficulty (loadout strength + the encounter's threat, threat being negative) lands within `tolerance` of a
// target net, and draws one of them weighted-random. When none are in band it falls back to the single candidate
// whose net is CLOSEST to the target — so a fight is always chosen and the run stays as winnable-but-not-trivial
// as the pool allows. This is the balancing hook in action: the target net (lower with depth) is supplied by the
// generator from MapGenerationSpec.BalanceTargets. Deterministic — all randomness comes from the injected `next`
// (maxExclusive → [0, maxExclusive)), which the generator seeds via MapGenRandom.
public sealed class EncounterSelector
{
    private readonly EncounterDistribution _distribution;
    private readonly BalanceCalculator _balance;

    public EncounterSelector(EncounterDistribution distribution, BalanceCalculator balance)
    {
        ArgumentNullException.ThrowIfNull(distribution);
        ArgumentNullException.ThrowIfNull(balance);
        _distribution = distribution;
        _balance = balance;
    }

    public bool HasCandidates(MapNodeKind role) => _distribution.For(role).Count > 0;

    // Every fight the role can draw, for a gate that has to compare them before choosing.
    public IEnumerable<EncounterId> Candidates(MapNodeKind role) => _distribution.For(role).Select(e => e.Encounter);

    // `eligible` is the DEPTH gate (MapGenerationSpec.EncounterMinimumDepthPercent), asked before anything
    // else: a fight the design does not open before the far end of the act is not a candidate in the third
    // room, however well its threat fits. If it filters everything away the gate yields rather than leaving
    // the node empty.
    public EncounterId Select(
        MapNodeKind role, int loadoutStrength, int targetNet, int tolerance, Func<int, int> next,
        ISet<EncounterId>? exclude = null, Func<EncounterId, bool>? eligible = null,
        IReadOnlyDictionary<EncounterId, int>? forbidden = null, int row = -1)
    {
        ArgumentNullException.ThrowIfNull(next);
        var all = _distribution.For(role);
        if (all.Count == 0)
            throw new InvalidOperationException(
                $"No encounter candidates for role {role}; the distribution must list at least one.");

        // What this depth may run at all, before the run's own history is consulted.
        var open = all;
        if (eligible is not null)
        {
            var allowed = all.Where(e => eligible(e.Encounter)).ToList();
            if (allowed.Count > 0)
                open = allowed;
        }

        // Draw WITHOUT replacement: skip templates already placed this run. If every candidate for this role
        // is used up (pool smaller than the number of nodes needing it), fall back to the full list so a fight
        // is still chosen — repeats only happen once a role's pool is genuinely exhausted.
        var candidates = exclude is null || exclude.Count == 0
            ? open
            : open.Where(e => !exclude.Contains(e.Encounter)).ToList();

        // `forbidden` is harder than `exclude`: the fights already on a route into this room, each with the row it
        // stands at. When the act-wide draw has run dry, a repeat is taken from what the routes in have NOT met —
        // in the depth band first, then from the whole role. Only a role whose every fight is already on a route
        // in repeats one, and then the one met furthest back.
        if (forbidden is { Count: > 0 })
        {
            bool Fresh(EncounterPoolEntry e) => !forbidden.ContainsKey(e.Encounter);
            candidates = candidates.Where(Fresh).ToList();
            if (candidates.Count == 0)
                candidates = open.Where(Fresh).ToList();
            if (candidates.Count == 0)
                candidates = all.Where(e => Fresh(e) && (exclude is null || !exclude.Contains(e.Encounter))).ToList();
            if (candidates.Count == 0)
                candidates = all.Where(Fresh).ToList();
            if (candidates.Count == 0)
            {
                // Never straight after itself: when the band's furthest-back fight stood in the row just before,
                // the whole role is searched for one further back.
                var pool = open;
                var furthest = pool.Min(e => forbidden[e.Encounter]);
                if (row >= 0 && row - furthest < 2)
                {
                    pool = all;
                    furthest = pool.Min(e => forbidden[e.Encounter]);
                }
                candidates = pool.Where(e => forbidden[e.Encounter] == furthest).ToList();
            }
        }
        if (candidates.Count == 0)
            candidates = open;

        var inBand = new List<EncounterPoolEntry>();
        foreach (var entry in candidates)
            if (Math.Abs(NetOf(entry, loadoutStrength) - targetNet) <= tolerance)
                inBand.Add(entry);

        if (inBand.Count > 0)
            return WeightedPick(inBand, next);

        // Nothing in band: the candidate nearest the target (deterministic; ties keep the earlier candidate).
        var best = candidates[0];
        var bestDistance = int.MaxValue;
        foreach (var entry in candidates)
        {
            var distance = Math.Abs(NetOf(entry, loadoutStrength) - targetNet);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = entry;
            }
        }
        return best.Encounter;
    }

    private int NetOf(EncounterPoolEntry entry, int loadoutStrength) =>
        loadoutStrength + _balance.EncounterThreat(entry.Encounter);

    private static EncounterId WeightedPick(IReadOnlyList<EncounterPoolEntry> entries, Func<int, int> next)
    {
        var total = 0;
        foreach (var entry in entries)
            total += Math.Max(1, entry.Weight);

        var roll = next(total);
        var cumulative = 0;
        foreach (var entry in entries)
        {
            cumulative += Math.Max(1, entry.Weight);
            if (roll < cumulative)
                return entry.Encounter;
        }
        return entries[^1].Encounter; // unreachable: roll < total
    }
}
