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
    int Index = 0)
{
    // Narrow the pick to one KIND of status. Polarity alone cannot say "one of my Overdue" — every debuff on
    // the player is a candidate, and a rule that means to spend its own would eat somebody else's.
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public StatusDefinitionId? Definition { get; init; }

    // Narrow it to the instances THIS effect's source put there. Source-bound statuses — Overdue, Trespass —
    // are the whole point of a threshold each enemy owns on the shared player: the rule that fires has to
    // consume its own stacks and leave every other enemy's alone.
    [System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool FromActingSource { get; init; }
}

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
    public static StatusInstanceId? Resolve(
        CombatState combat, CombatantId owner, StatusSelectionSpec spec, CombatantId? actingSource = null)
    {
        if (!combat.TryGetCombatant(owner, out var combatant) || combatant is null)
            return null;

        var matches = combatant.Statuses
            .Where(s => Matches(s, spec.Polarity))
            .Where(s => spec.Definition is not { } definition || s.DefinitionId == definition)
            // Asking for the acting source's own instances when there IS no acting source matches nothing,
            // rather than quietly matching everything — a rule that means "mine" must never eat another's.
            .Where(s => !spec.FromActingSource || (actingSource is { } src && s.SourceCombatantId == src))
            .ToList();
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
