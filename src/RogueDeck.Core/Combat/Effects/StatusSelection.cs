namespace RogueDeck.Core.Combat;

// ── Status-instance selection (#3 non-combatant target domains) ────────────────
//
// The effect-program target domain has always been the COMBATANT: a node points at combatants and acts on
// them wholesale. Status operations could only name a status by DEFINITION ("remove Poison", "remove all
// buffs") — there was no way to point at a single status INSTANCE the way card targeting points at a single
// card instance. This adds that missing selector: given a combatant, pick ONE of its status instances by a
// polarity filter + a pick mode, so ops like "remove a RANDOM buff" or "steal the enemy's first debuff" become
// expressible. It mirrors the card-instance selection family (positional / random) at the status altitude.

// Which statuses on a combatant are eligible to be picked.
public enum StatusPolarityFilter
{
    Any,
    Buff,
    Debuff,
}

// How to pick among the eligible statuses.
public enum StatusPick
{
    First,  // deterministic: the Index-th eligible status in owner order (a pure read)
    Random, // uniform via the combat RNG (deterministic by seed; advances the RNG step)
}

// A reusable, serializable description of "which one status instance on a combatant". Shared by every op that
// consumes a chosen status (remove today; reduce/copy/steal are follow-ups), so the selection logic lives once.
public sealed record StatusSelectionSpec(
    StatusPolarityFilter Polarity = StatusPolarityFilter.Any,
    StatusPick Pick = StatusPick.First,
    int Index = 0);

public static class StatusSelection
{
    private static bool Matches(StatusInstance status, StatusPolarityFilter filter) => filter switch
    {
        StatusPolarityFilter.Buff => status.Polarity == StatusPolarity.Buff,
        StatusPolarityFilter.Debuff => status.Polarity == StatusPolarity.Debuff,
        _ => true,
    };

    // Resolves the spec against a combatant's live statuses. Returns null when the combatant is absent or no
    // status matches the filter. For StatusPick.Random this advances the combat RNG step (like the random card
    // selector), so a replay reproduces the pick.
    public static StatusInstanceId? Resolve(CombatState combat, CombatantId owner, StatusSelectionSpec spec)
    {
        if (!combat.TryGetCombatant(owner, out var combatant) || combatant is null)
            return null;

        var matches = combatant.Statuses.Where(s => Matches(s, spec.Polarity)).ToList();
        if (matches.Count == 0)
            return null;

        switch (spec.Pick)
        {
            case StatusPick.Random:
                var index = CombatRandom.CreateShuffledIndexes(matches.Count, combat.RandomSeed, combat.RandomStep)[0];
                combat.AdvanceRandomStep();
                return matches[index].Id;
            default: // First
                return spec.Index >= 0 && spec.Index < matches.Count ? matches[spec.Index].Id : null;
        }
    }
}
