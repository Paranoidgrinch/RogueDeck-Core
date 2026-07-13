namespace RogueDeck.Core.Combat;

// ── Resource-pool selection (#3 non-combatant target domains) ──────────────────
//
// Resource operations have always addressed a pool by a KNOWN id ("lose 1 Energy"). There was no way to point
// at ONE of a combatant's resource pools by a SELECTION the way status-instance selection points at one of its
// statuses — so "drain the enemy's highest resource" or "reduce a random resource pool" could not be expressed
// without naming the id up front. This adds that selector: given a combatant, pick ONE of its resource pools by
// an eligibility filter + a pick mode, resolving to a ResourceId. It mirrors StatusSelection at the resource
// altitude.

// Which of a combatant's resource pools are eligible to be picked.
public enum ResourcePoolFilter
{
    Any,       // every pool the combatant has
    NonEmpty,  // only pools whose current value is > 0 (so a drain doesn't pick an already-empty pool)
}

// How to pick among the eligible pools. First/Highest/Lowest are pure reads (deterministic); Random advances
// the combat RNG. The eligible pools are ordered by resource id (ordinal) up front, so First's Index is stable
// and Highest/Lowest break ties by id — the pick is reproducible regardless of the pools' insertion order.
public enum ResourcePick
{
    First,   // the Index-th eligible pool in resource-id order
    Random,  // uniform via the combat RNG
    Highest, // the eligible pool with the greatest current value
    Lowest,  // the eligible pool with the least current value
}

// A reusable, serializable description of "which one resource pool on a combatant". Shared by every op that
// consumes a chosen resource, so the selection logic lives once.
public sealed record ResourceSelectionSpec(
    ResourcePoolFilter Filter = ResourcePoolFilter.Any,
    ResourcePick Pick = ResourcePick.First,
    int Index = 0);

public static class ResourceSelection
{
    private static bool Matches(ValuePoolState pool, ResourcePoolFilter filter) => filter switch
    {
        ResourcePoolFilter.NonEmpty => pool.Current > 0,
        _ => true,
    };

    // Resolves the spec against a combatant's live resource pools. Returns null when the combatant is absent or
    // no pool matches the filter. For ResourcePick.Random this advances the combat RNG step (like the random
    // status selector), so a replay reproduces the pick.
    public static ResourceId? Resolve(CombatState combat, CombatantId owner, ResourceSelectionSpec spec)
    {
        if (!combat.TryGetCombatant(owner, out var combatant) || combatant is null)
            return null;

        // Order by resource id (ordinal) so every pick mode is deterministic regardless of dictionary order.
        var eligible = combatant.Resources
            .Where(kv => Matches(kv.Value, spec.Filter))
            .OrderBy(kv => kv.Key.value, StringComparer.Ordinal)
            .ToList();
        if (eligible.Count == 0)
            return null;

        switch (spec.Pick)
        {
            case ResourcePick.Random:
                var index = CombatRandom.CreateShuffledIndexes(eligible.Count, combat.RandomSeed, combat.RandomStep)[0];
                combat.AdvanceRandomStep();
                return eligible[index].Key;
            case ResourcePick.Highest:
                // Stable sort keeps the ordinal-id order on ties, so the tie-break is deterministic.
                return eligible.OrderByDescending(kv => kv.Value.Current).First().Key;
            case ResourcePick.Lowest:
                return eligible.OrderBy(kv => kv.Value.Current).First().Key;
            default: // First
                return spec.Index >= 0 && spec.Index < eligible.Count ? eligible[spec.Index].Key : null;
        }
    }
}
