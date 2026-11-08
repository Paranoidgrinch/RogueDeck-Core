using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Pledge (StandardCombatIds.PledgeTag): a card that may be played without enough Energy. The owner pays what they
// have, every cost modifier applied first, and the unpaid rest is written to PledgeShortfallCounter on the payer
// before the card's program runs — so the card can read its own debt and answer for it.
public class PledgeCardTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");
    private static readonly CardDefinitionId PledgeId = new("pledge.oath");
    private static readonly CardDefinitionId PlainId = new("plain.costly");

    // A 3-cost pledge card whose program deals damage equal to the shortfall it just ran up, and the same card
    // without the tag.
    private static CombatDefinitionRegistry Registry()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterCard(Card(PledgeId, pledge: true));
        builder.RegisterCard(Card(PlainId, pledge: false));
        return builder.Build();
    }

    private static CardDefinitionBuilder Card(CardDefinitionId id, bool pledge)
    {
        var card = new CardDefinitionBuilder(id, new PackageId("test"), "c.n", "c.d")
        {
            Costs = { new ResourceCost(StandardCombatIds.EnergyResource, 3) },
            Program = new EffectProgram<CardPlayContext>(new DealDamageNode<CardPlayContext>(
                CombatantTargetSelectors.EventTarget,
                new CombatantCounterExpression<CardPlayContext>(
                    CombatantTargetSelectors.Source, StandardCombatIds.PledgeShortfallCounter))),
        };
        if (pledge)
            card.Tags.Add(StandardCombatIds.PledgeTag);
        return card;
    }

    private static (CombatState Combat, CardInstance Card) Setup(CardDefinitionId definition, int energy)
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.GetCombatant(HeroId).AddResource(
            StandardCombatIds.EnergyResource, new ValuePoolState(current: energy, max: 3));
        var card = new CardInstance(combat.CreateNextCardInstanceId(), definition, HeroId, CardZone.Hand);
        combat.GetCardZones(HeroId).AddCard(card);
        return (combat, card);
    }

    private static bool PlayViaEffect(CombatState combat, CombatDefinitionRegistry registry, CardInstance card)
    {
        var slot = new PlayCardOutcomeSlot();
        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, card.Id, GoblinId, slot));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
        return slot.Value!.WasPlayed;
    }

    private static int Energy(CombatState combat) =>
        combat.GetCombatant(HeroId).Resources[StandardCombatIds.EnergyResource].Current;

    [Fact]
    public void A_pledge_card_short_of_energy_is_played_and_its_program_reads_the_shortfall()
    {
        var registry = Registry();
        var (combat, card) = Setup(PledgeId, energy: 1);
        var goblinHp = combat.GetCombatant(GoblinId).Health.Current;

        Assert.True(PlayViaEffect(combat, registry, card));

        Assert.Equal(0, Energy(combat));                                              // paid the 1 it had
        Assert.Equal(2, combat.GetCombatant(HeroId).GetCounter(StandardCombatIds.PledgeShortfallCounter));
        Assert.Equal(goblinHp - 2, combat.GetCombatant(GoblinId).Health.Current);    // read before it ran
        Assert.Empty(combat.GetCardZones(HeroId).Hand);
    }

    [Fact]
    public void The_cost_paid_event_reports_what_was_actually_paid()
    {
        var registry = Registry();
        var (combat, card) = Setup(PledgeId, energy: 1);

        PlayViaEffect(combat, registry, card);

        Assert.Contains(combat.CombatLog, e => e.Type == StandardCombatLogTypes.CardCostPaid
                                              && e.Message.Contains("paid 1 standard.energy"));
    }

    [Fact]
    public void A_pledge_paid_in_full_writes_a_zero_shortfall()
    {
        var registry = Registry();
        var (combat, card) = Setup(PledgeId, energy: 3);
        combat.GetCombatant(HeroId).SetCounter(StandardCombatIds.PledgeShortfallCounter, 5); // a stale debt

        Assert.True(PlayViaEffect(combat, registry, card));

        Assert.Equal(0, Energy(combat));
        Assert.Equal(0, combat.GetCombatant(HeroId).GetCounter(StandardCombatIds.PledgeShortfallCounter));
    }

    [Fact]
    public void Cost_modifiers_apply_before_the_shortfall_is_measured()
    {
        var registry = Registry();
        var (combat, card) = Setup(PledgeId, energy: 1);
        card.SetMarkCounter(StandardCombatIds.CardCostDeltaCounter, -1); // this copy costs 2

        Assert.True(PlayViaEffect(combat, registry, card));

        Assert.Equal(0, Energy(combat));
        Assert.Equal(1, combat.GetCombatant(HeroId).GetCounter(StandardCombatIds.PledgeShortfallCounter));
    }

    [Fact]
    public void The_direct_processor_path_plays_a_pledge_card_too()
    {
        var registry = Registry();
        var (combat, card) = Setup(PledgeId, energy: 0);

        new CombatCardPlayProcessor().PlayCardInstance(
            combat, registry, new CardInstancePlayRequest(card.Id, HeroId, GoblinId));

        Assert.Equal(3, combat.GetCombatant(HeroId).GetCounter(StandardCombatIds.PledgeShortfallCounter));
        Assert.Empty(combat.GetCardZones(HeroId).Hand);
    }

    [Fact]
    public void The_same_card_without_the_tag_stays_unaffordable()
    {
        var registry = Registry();
        var (combat, card) = Setup(PlainId, energy: 1);

        Assert.False(PlayViaEffect(combat, registry, card));

        Assert.Equal(1, Energy(combat));
        Assert.Same(card, Assert.Single(combat.GetCardZones(HeroId).Hand));
        Assert.Throws<InvalidOperationException>(() => new CombatCardPlayProcessor().PlayCardInstance(
            combat, registry, new CardInstancePlayRequest(card.Id, HeroId, GoblinId)));
    }

    [Fact]
    public void CanPayCosts_lets_a_pledge_through_and_nothing_else()
    {
        var registry = Registry();
        var (combat, _) = Setup(PledgeId, energy: 1);
        var hero = combat.GetCombatant(HeroId);
        var costs = new[] { new CalculatedResourceCost(StandardCombatIds.EnergyResource, 3) };

        Assert.True(CombatCardPlayProcessor.CanPayCosts(registry.GetCard(PledgeId), hero, costs));
        Assert.False(CombatCardPlayProcessor.CanPayCosts(registry.GetCard(PlainId), hero, costs));
    }
}
