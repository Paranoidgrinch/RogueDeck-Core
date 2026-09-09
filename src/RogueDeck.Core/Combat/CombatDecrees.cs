using System.Collections.Immutable;

namespace RogueDeck.Core.Combat;

// ── Decrees: statuses that change a RULE of combat rather than a number ────────────────────────────────────
//
// Everything a status could say until now was arithmetic (a passive modifier), a reaction (a triggered
// program) or an interception (a prohibition, a death prevention). None of those can say "no more than four
// cards may be played this turn" or "a played card is exhausted instead of discarded", because those are not
// quantities — they are the rules the fight is being played under, and the engine had exactly one of them
// hard-coded per site.
//
// A CombatRuleSpec is one such rule, carried by a status definition and IN FORCE while a combatant bears it.
// Which combatant matters is decided at the read site, and it is always the obvious one: the player whose
// turn is being limited, the combatant whose card is being priced, the one drawing, the one refilling. A rule
// meant to be UNIVERSAL — Enlil's word binds him too — is simply worn by both sides, and then each side reads
// it about itself. That is the whole mechanism; there is no global rule table, because a rule nobody wears is
// a rule with no chip on any row, and this game does not have those.
public enum CombatRule
{
    // No more than Amount cards may be played by the bearer in one turn. The card is genuinely refused —
    // this is a rule, not a promise (which is the other Act-V god's department entirely).
    MaxCardsPerTurn,

    // A card may not be played if it shares one of the spec's Tags with the card the bearer played
    // immediately before it, this turn. Tags name what "likeness" MEANS — in a game whose cards have a type,
    // the type tags. An empty tag list makes the rule inert rather than refusing everything.
    NoLikenessInSuccession,

    // The Amount-th card the bearer plays each turn costs nothing (1 = the first, 3 = the third).
    NthCardOfTurnIsFree,

    // No card the bearer plays may cost less than Amount of Resource (energy when unnamed). Unlike every
    // other cost rule this one can ADD a cost to a card that has none, which is the only way to say "cards
    // cannot cost zero" about a card whose printed cost is zero — such a card carries no cost entry at all,
    // so there is nothing for an ordinary cost modifier to raise.
    MinimumCardCost,

    // What the bearer has not spent is still there after the refill: the turn-start refill ADDS the pool's
    // refill target to what was left instead of replacing it, and the pool is allowed past its ceiling while
    // it does. The ceiling returns the moment the rule does.
    UnspentResourceCarries,

    // A card the bearer plays goes to the exhaust pile instead of wherever its definition sends it. A card
    // whose definition already exhausts it is unaffected.
    PlayedCardsExhaust,

    // The bearer draws no card that would take their hand past Amount. The cards are NOT drawn — they stay
    // on the draw pile rather than being drawn and thrown away.
    MaxHandSize
}

// One rule of combat, as carried by a status definition.
//
// Amount is the rule's number and means whatever the rule says it means. Tags name the card tags a rule about
// likeness compares. Resource names the pool a cost rule prices in, and is the energy resource when unnamed.
public sealed record CombatRuleSpec(
    CombatRule Rule,
    int Amount = 0,
    IReadOnlyList<TagId>? Tags = null,
    ResourceId? Resource = null)
{
    public IReadOnlyList<TagId> LikenessTags => Tags ?? [];

    public ResourceId ResourceOrEnergy => Resource ?? StandardCombatIds.EnergyResource;
}

// Reading the rules a combatant is under. Deliberately allocation-light and order-stable: specs are collected
// in (status-id, spec-index) order so two decrees that say the same thing compose the same way every replay.
public static class CombatDecrees
{
    public static ImmutableArray<CombatRuleSpec> InForce(
        CombatDefinitionRegistry? registry, CombatantState combatant, CombatRule rule)
    {
        if (registry is null || combatant.Statuses.Count == 0)
            return [];

        List<(string StatusId, int Index, CombatRuleSpec Spec)> found = [];
        foreach (var group in combatant.Statuses.GroupBy(s => s.DefinitionId))
        {
            if (!registry.StatusDefinitions.TryGetValue(group.Key, out var definition) ||
                definition.CombatRules.Count == 0)
                continue;

            for (var i = 0; i < definition.CombatRules.Count; i++)
            {
                if (definition.CombatRules[i].Rule == rule)
                    found.Add((group.Key.value, i, definition.CombatRules[i]));
            }
        }

        if (found.Count == 0)
            return [];

        found.Sort((a, b) =>
        {
            var c = string.CompareOrdinal(a.StatusId, b.StatusId);
            return c != 0 ? c : a.Index.CompareTo(b.Index);
        });

        return [.. found.Select(f => f.Spec)];
    }

    public static bool Any(
        CombatDefinitionRegistry? registry, CombatantState combatant, CombatRule rule) =>
        !InForce(registry, combatant, rule).IsEmpty;

    // The strictest ceiling among the decrees in force, or null when none is. Two decrees that both cap a
    // thing do not argue: the smaller number is the rule, because a rule that has been spoken cannot be
    // loosened by a second one (§11.2 — decrees are not shortened, delayed or bargained away).
    public static int? Ceiling(
        CombatDefinitionRegistry? registry, CombatantState combatant, CombatRule rule)
    {
        var specs = InForce(registry, combatant, rule);
        return specs.IsEmpty ? null : specs.Min(s => s.Amount);
    }

    // The highest floor among the decrees in force, for the same reason read the other way round.
    public static int? Floor(
        CombatDefinitionRegistry? registry, CombatantState combatant, CombatRule rule)
    {
        var specs = InForce(registry, combatant, rule);
        return specs.IsEmpty ? null : specs.Max(s => s.Amount);
    }
}

// ── the sites that read them ──────────────────────────────────────────────────────────────────────────────

// "The fourth work shall be the last", and "no work shall follow its likeness". Both refuse a play outright,
// which is what separates a decree from an oath: the card is not offered and then judged, it is unavailable.
public sealed class DecreeCardPlayValidator : ICardPlayValidator
{
    public string ModifierId => "standard.decree_validator";

    // Below Unplayable and Stun (200): a curse is refused for being a curse before a decree is consulted.
    public int Priority => 150;

    public void Validate(CardPlayValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var registry = context.Registry;
        var source = context.Source;

        if (CombatDecrees.Ceiling(registry, source, CombatRule.MaxCardsPerTurn) is { } cap)
        {
            var played = context.Combat.GetCardPlayTurnStats(source.Id).CardsPlayedThisTurn;
            if (played >= cap)
            {
                throw new InvalidOperationException(
                    $"Combatant '{source.Id}' may play no more than {cap} card(s) this turn by decree.");
            }
        }

        foreach (var spec in CombatDecrees.InForce(registry, source, CombatRule.NoLikenessInSuccession))
        {
            var stats = context.Combat.GetCardPlayTurnStats(source.Id);
            if (stats.LastCardPlayedDefinitionId is null)
                continue;

            foreach (var tag in spec.LikenessTags)
            {
                if (context.Card.Tags.Contains(tag) && stats.LastCardPlayedThisTurnHasTag(tag))
                {
                    throw new InvalidOperationException(
                        $"Combatant '{source.Id}' may not follow a '{tag.value}' card with another by decree.");
                }
            }
        }
    }
}

// "The first word shall be without price", "the third work shall be without price". The floor that answers
// them ("no work shall be without measure") is NOT here: it has to be able to price a card that carries no
// cost entry at all, so it lives where the whole cost of a play is assembled.
public sealed class DecreeCardCostModifier : ICardCostModifier
{
    public string ModifierId => "standard.decree_free_nth_card";

    public int Priority => 300;

    public int ModifyCostAmount(CardCostModificationContext context, int currentAmount)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (currentAmount <= 0)
            return currentAmount;

        var played = context.Combat.GetCardPlayTurnStats(context.Source.Id).CardsPlayedThisTurn;
        foreach (var spec in CombatDecrees.InForce(context.Registry, context.Source, CombatRule.NthCardOfTurnIsFree))
        {
            if (spec.Amount == played + 1)
                return 0;
        }

        return currentAmount;
    }
}
