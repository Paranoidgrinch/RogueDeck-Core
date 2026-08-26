using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Redacted (B&B enemy-mechanics arc, Phase 2). A card instance can carry a one-shot next-play output scale:
// the card-play pipeline reads the reserved scale mark-counters off the played instance, installs the fraction
// on that play's execution context, and every output node (damage/Block/heal/draw/energy/status) narrows its
// amount accordingly — then the mark is consumed so exactly the next play is reduced. Content realises Redacted
// by setting the two reserved counters to 1/2 with the ordinary card-mark op.
public class RedactedOutputScaleTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    private static CardInstance AddToHand(CombatState combat, CardDefinitionId def)
    {
        var card = new CardInstance(combat.CreateNextCardInstanceId(), def, HeroId, CardZone.Hand);
        combat.GetCardZones(HeroId).AddCard(card);
        return card;
    }

    private static void GiveEnergy(CombatState combat) =>
        combat.GetCombatant(HeroId).AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(9, 9));

    private static void MarkHalfOutput(CardInstance card)
    {
        card.SetMarkCounter(StandardCombatIds.CardOutputScaleNumeratorCounter, 1);
        card.SetMarkCounter(StandardCombatIds.CardOutputScaleDenominatorCounter, 2);
    }

    [Fact]
    public void A_redacted_card_deals_half_damage_on_its_next_play_then_the_mark_is_consumed()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        GiveEnergy(combat);

        var redacted = AddToHand(combat, StandardCombatIds.StrikeCard);   // Strike = 6 damage
        MarkHalfOutput(redacted);
        var normal = AddToHand(combat, StandardCombatIds.StrikeCard);

        var processor = new CombatCardPlayProcessor();
        processor.PlayCardInstance(combat, registry,
            new CardInstancePlayRequest(redacted.Id, HeroId, GoblinId));

        // 6 → 3 (halved, floored). Goblin 12 → 9.
        Assert.Equal(9, combat.GetCombatant(GoblinId).Health.Current);
        // One-shot: the scale counters were consumed.
        Assert.Equal(0, redacted.GetMarkCounter(StandardCombatIds.CardOutputScaleDenominatorCounter));

        processor.PlayCardInstance(combat, registry,
            new CardInstancePlayRequest(normal.Id, HeroId, GoblinId));

        // Unmarked Strike lands its full 6. Goblin 9 → 3.
        Assert.Equal(3, combat.GetCombatant(GoblinId).Health.Current);
    }

    [Fact]
    public void A_redacted_card_grants_half_block()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        GiveEnergy(combat);

        var redacted = AddToHand(combat, StandardCombatIds.DefendCard);   // Defend = 5 block
        MarkHalfOutput(redacted);

        new CombatCardPlayProcessor().PlayCardInstance(combat, registry,
            new CardInstancePlayRequest(redacted.Id, HeroId));

        // 5 → 2 (halved, floored).
        Assert.Equal(2, combat.GetCombatant(HeroId).DefensivePools[StandardCombatIds.BlockDefensivePool].Current);
    }

    [Fact]
    public void The_scale_also_applies_to_a_program_based_cards_output()
    {
        // Real B&B content is Program-based (BuildContent), so prove the per-node path scales too.
        var damageCard = new CardDefinitionId("test.zap");
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterCard(new CardDefinitionBuilder(damageCard, new PackageId("test"), "card.n", "card.d")
        {
            Program = new EffectProgram<CardPlayContext>(
                new DealDamageNode<CardPlayContext>(
                    CombatantTargetSelectors.EventTarget, new ConstantExpression<CardPlayContext>(8))),
        });
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        GiveEnergy(combat);
        var redacted = AddToHand(combat, damageCard);
        MarkHalfOutput(redacted);

        new CombatCardPlayProcessor().PlayCardInstance(combat, registry,
            new CardInstancePlayRequest(redacted.Id, HeroId, GoblinId));

        // 8 → 4 (halved by the node executor). Goblin 12 → 8.
        Assert.Equal(8, combat.GetCombatant(GoblinId).Health.Current);
    }

    [Fact]
    public void An_unmarked_card_is_never_scaled()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        GiveEnergy(combat);

        var normal = AddToHand(combat, StandardCombatIds.StrikeCard);
        new CombatCardPlayProcessor().PlayCardInstance(combat, registry,
            new CardInstancePlayRequest(normal.Id, HeroId, GoblinId));

        Assert.Equal(6, combat.GetCombatant(GoblinId).Health.Current); // full 6 damage
    }

    // The mark is a FRACTION, not a discount: an inscription that says "half again as much" writes 3/2 the
    // same way a redaction writes 1/2, and the play reads both the same way.
    [Fact]
    public void A_widening_mark_amplifies_the_same_play_a_narrowing_one_would_reduce()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        GiveEnergy(combat);
        combat.GetCombatant(GoblinId).Health.SetMax(40);
        combat.GetCombatant(GoblinId).Health.SetCurrent(40);

        var revised = AddToHand(combat, StandardCombatIds.StrikeCard);   // Strike = 6 damage
        revised.SetMarkCounter(StandardCombatIds.CardOutputScaleNumeratorCounter, 3);
        revised.SetMarkCounter(StandardCombatIds.CardOutputScaleDenominatorCounter, 2);

        new CombatCardPlayProcessor().PlayCardInstance(combat, registry,
            new CardInstancePlayRequest(revised.Id, HeroId, GoblinId));

        Assert.Equal(31, combat.GetCombatant(GoblinId).Health.Current); // 6 → 9
        // Still one-shot, in both directions.
        Assert.Equal(0, revised.GetMarkCounter(StandardCombatIds.CardOutputScaleDenominatorCounter));
    }

    // A card asked to go where it already is stays put — unless the request is about POSITION. "Fetch the
    // partner to the top of the draw pile" has nowhere else to fetch from.
    [Fact]
    public void Moving_a_card_to_the_top_of_the_pile_it_is_already_in_reorders_it()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var zones = combat.GetCardZones(HeroId);

        var first = new CardInstance(combat.CreateNextCardInstanceId(), StandardCombatIds.StrikeCard, HeroId, CardZone.DrawPile);
        var second = new CardInstance(combat.CreateNextCardInstanceId(), StandardCombatIds.DefendCard, HeroId, CardZone.DrawPile);
        zones.AddCard(first);
        zones.AddCard(second);
        Assert.Equal(first.Id, zones.GetCardsInZone(CardZone.DrawPile)[0].Id);

        combat.EnqueueEffect(new MoveCardToZoneEffectRequest(
            HeroId, second.Id, CardZone.DrawPile, Placement: ZonePlacement.Top));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(second.Id, zones.GetCardsInZone(CardZone.DrawPile)[0].Id);

        // Bottom keeps the historical no-op, so nothing authored before placements existed shifts under it.
        combat.EnqueueEffect(new MoveCardToZoneEffectRequest(HeroId, second.Id, CardZone.DrawPile));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
        Assert.Equal(second.Id, zones.GetCardsInZone(CardZone.DrawPile)[0].Id);
    }
}
