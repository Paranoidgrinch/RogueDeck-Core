using RogueDeck.Core.Combat;
using RogueDeck.Scenario.Authoring;

namespace RogueDeck.ShredEngine;

// The composition compiler: an ORDERED shred list becomes a normal CardBlueprint the combat engine plays
// unchanged. Pure and deterministic — the same list always yields the same derived id, costs and program —
// which is what lets a crafted card persist as nothing but its part list (RunCardInstance.Composition) and
// be re-synthesized identically for every fight and on every host (Studio, Godot).
public static class ShredCardSynthesizer
{
    // The synthesized definition id: "shred:" + part ids joined in arrangement order. Ordered on purpose —
    // a different arrangement is a different card (execution order differs).
    public static string DerivedId(IEnumerable<string> partIds) =>
        ShredEngineIds.ComposedCardIdPrefix + string.Join("+", partIds);

    // Compiles the arrangement, or reports WHY it is not buildable (the workbench shows the reason instead
    // of throwing): an invalid composed program (depth / data-flow — EffectProgram's own validation) is a
    // content problem surfaced at build time, never a crash mid-run.
    public static bool TrySynthesize(
        IReadOnlyList<ShredData> parts, out CardBlueprint card, out string? error)
    {
        card = null!;
        if (parts.Count == 0)
        {
            error = "A card needs at least one shred.";
            return false;
        }

        var blueprint = new CardBlueprint(DerivedId(parts.Select(p => p.Id)))
        {
            // Deterministic display name from the parts; hosts may present it any way they like.
            NameKey = string.Join(" + ", parts.Select(p => string.IsNullOrWhiteSpace(p.NameKey) ? p.Id : p.NameKey)),
            DescriptionKey = "",
        };

        blueprint.Costs.AddRange(ComposeCosts(parts));

        // Tags: union in first-occurrence order.
        foreach (var tag in parts.SelectMany(p => p.Tags).Distinct(StringComparer.Ordinal))
            blueprint.Tags.Add(new TagId(tag));

        // Program: the fragments in arrangement order under one Sequence (every part runs predictably;
        // causal chaining is a later per-shred opt-in). No fragments ⇒ a cost-only card with no program.
        var fragments = parts.Where(p => p.Program is not null).Select(p => p.Program!.Root).ToArray();
        if (fragments.Length > 0)
        {
            try
            {
                blueprint.Program = new EffectProgram<CardPlayContext>(
                    fragments.Length == 1 ? fragments[0] : new SequenceEffectNode<CardPlayContext>(fragments));
            }
            catch (Exception ex)
            {
                error = $"The combined program is invalid: {ex.Message}";
                return false;
            }
        }

        card = blueprint;
        error = null;
        return true;
    }

    // The composed cost: per-part cost vectors, sibling modifiers applied in part order (part 0's modifiers
    // first), each application flooring (percent) or adding (delta) and clamping at 0 — then summed per
    // resource. Synthesis-time math: the resulting card carries plain static costs.
    private static IEnumerable<ResourceCost> ComposeCosts(IReadOnlyList<ShredData> parts)
    {
        var vectors = parts
            .Select(p => p.Costs
                .GroupBy(c => c.ResourceId.value, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Sum(c => c.Amount), StringComparer.Ordinal))
            .ToList();

        for (var i = 0; i < parts.Count; i++)
        {
            foreach (var modifier in parts[i].Modifiers)
            {
                for (var target = 0; target < parts.Count; target++)
                {
                    if (!InScope(modifier.Scope, self: i, target))
                        continue;
                    var vector = vectors[target];
                    foreach (var resource in vector.Keys.ToList())
                    {
                        if (modifier.Resource is not null
                            && !string.Equals(modifier.Resource, resource, StringComparison.Ordinal))
                            continue;
                        var value = vector[resource];
                        value = modifier.Op switch
                        {
                            ShredModifierOp.CostFactorPercent => (int)Math.Floor(value * modifier.Amount / 100.0),
                            ShredModifierOp.CostDelta => value + modifier.Amount,
                            _ => value,
                        };
                        vector[resource] = Math.Max(0, value);
                    }
                }
            }
        }

        var total = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var vector in vectors)
            foreach (var (resource, amount) in vector)
                total[resource] = total.GetValueOrDefault(resource) + amount;

        return total.Where(kv => kv.Value > 0)
            .OrderBy(kv => kv.Key, StringComparer.Ordinal) // deterministic cost order
            .Select(kv => new ResourceCost(new ResourceId(kv.Key), kv.Value));
    }

    private static bool InScope(ShredModifierScope scope, int self, int target) => scope switch
    {
        ShredModifierScope.Below => target > self,
        ShredModifierScope.Above => target < self,
        ShredModifierScope.Others => target != self,
        ShredModifierScope.All => true,
        _ => false,
    };
}
