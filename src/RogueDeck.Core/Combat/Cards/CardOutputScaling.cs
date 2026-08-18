namespace RogueDeck.Core.Combat;

// Redacted substrate — legacy path. Scales the player-facing numeric OUTPUT of a legacy card effect request
// (damage / Block / heal / energy gain / draw / status stacks) by a fraction, rounding DOWN, and only when the
// fraction actually reduces a positive amount. Non-output requests pass through unchanged. Program-based cards
// scale per-node via EffectExecutionContext.ScaleOutput; this is the equivalent for cards that still emit legacy
// effect-request recipes, so Redacted behaves identically regardless of authoring path.
//
// This concrete per-request knowledge lives HERE, not in the card-play processor: the processor must stay
// generic about effect-request types (see CombatArchitectureGuardTests) and merely delegates to this helper.
internal static class CardOutputScaling
{
    public static IEffectRequest ScaleRequest(IEffectRequest request, int numerator, int denominator)
    {
        return request switch
        {
            DealDamageEffectRequest r when !r.IsRedistributedShare =>
                r with { Amount = Scale(r.Amount, numerator, denominator) },
            GainBlockEffectRequest r => r with { Amount = Scale(r.Amount, numerator, denominator) },
            HealEffectRequest r => r with { Amount = Scale(r.Amount, numerator, denominator) },
            GainResourceEffectRequest r => r with { Amount = Scale(r.Amount, numerator, denominator) },
            DrawCardsEffectRequest r => r with { Count = Scale(r.Count, numerator, denominator) },
            ApplyStatusEffectRequest r => r with { Stacks = Scale(r.Stacks, numerator, denominator) },
            _ => request,
        };
    }

    private static int Scale(int amount, int numerator, int denominator) =>
        amount <= 0 || numerator >= denominator
            ? amount
            : (int)((long)amount * numerator / denominator);
}
