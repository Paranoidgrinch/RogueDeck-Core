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

    public EncounterId Select(
        MapNodeKind role, int loadoutStrength, int targetNet, int tolerance, Func<int, int> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        var candidates = _distribution.For(role);
        if (candidates.Count == 0)
            throw new InvalidOperationException(
                $"No encounter candidates for role {role}; the distribution must list at least one.");

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
