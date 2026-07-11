using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Per-card lifecycle triggers (engine gap #4): a card can carry a program that fires while it sits in a zone, not
// only when played — the base mechanic behind Burn/Decay/Regret. The card OWNS the behaviour on its definition,
// rather than a separate global rule filtered to its id. Slice 1: TurnEndInHand (a "burn" that hurts its owner if
// still held at turn end). The hand is intact at TurnEnded (the discard is deferred), so ordinary and held cards
// both see the trigger.
public class CardLifecycleProgramTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CardDefinitionId BurnId = new("curse.burn");

    // A standard registry plus a "burn" curse: while in hand at turn end, deal 2 to its owner.
    private static CombatDefinitionRegistry RegistryWithBurn()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterCard(new CardDefinitionBuilder(BurnId, new PackageId("test"), "burn.n", "burn.d")
        {
            LifecyclePrograms =
            {
                [CardLifecycleTrigger.TurnEndInHand] = new EffectProgram<CardLifecycleContext>(
                    new DealDamageNode<CardLifecycleContext>(
                        CombatantTargetSelectors.Source, new ConstantExpression<CardLifecycleContext>(2))),
            },
        });
        return builder.Build();
    }

    private static CardInstance AddToHand(CombatState combat, CardDefinitionId definition)
    {
        var card = new CardInstance(combat.CreateNextCardInstanceId(), definition, HeroId, CardZone.Hand);
        combat.GetCardZones(HeroId).AddCard(card);
        return card;
    }

    [Fact]
    public void A_burn_held_at_turn_end_damages_its_owner()
    {
        var registry = RegistryWithBurn();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var before = combat.GetCombatant(HeroId).Health.Current;
        AddToHand(combat, BurnId);

        combat.EnqueueEvent(new TurnEndedCombatEvent(HeroId, Round: 1, Turn: 1));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(before - 2, combat.GetCombatant(HeroId).Health.Current);
    }

    [Fact]
    public void An_ordinary_card_held_at_turn_end_does_nothing()
    {
        var registry = RegistryWithBurn();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var before = combat.GetCombatant(HeroId).Health.Current;
        AddToHand(combat, StandardCombatIds.StrikeCard); // no lifecycle program

        combat.EnqueueEvent(new TurnEndedCombatEvent(HeroId, Round: 1, Turn: 1));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(before, combat.GetCombatant(HeroId).Health.Current);
    }

    [Fact]
    public void A_burn_in_the_discard_pile_does_not_trigger()
    {
        var registry = RegistryWithBurn();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var before = combat.GetCombatant(HeroId).Health.Current;
        var burn = new CardInstance(combat.CreateNextCardInstanceId(), BurnId, HeroId, CardZone.DiscardPile);
        combat.GetCardZones(HeroId).AddCard(burn);

        combat.EnqueueEvent(new TurnEndedCombatEvent(HeroId, Round: 1, Turn: 1));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(before, combat.GetCombatant(HeroId).Health.Current); // only hand cards fire TurnEndInHand
    }
}
