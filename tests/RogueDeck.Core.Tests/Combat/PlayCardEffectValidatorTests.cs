using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// A card play arrives by two roads: the strict processor (used by scripted scenarios and tests, which THROWS
// when a rule forbids the play) and the effect request (used by the interactive host, the run walker and the
// Godot front end, which must leave the card in hand instead). Both have to ask the same rules.
//
// They did not. Stun — and every other card-play validator — was consulted only on the strict road, so on the
// road the game is actually played on a stunned combatant played its whole hand. "You lose the turn" has to
// mean the turn is lost wherever the turn is taken.
public class PlayCardEffectValidatorTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    [Fact]
    public void AStunnedCombatantPlaysNothingThroughTheEffectRequestPath()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);
        EnsureEnergy(hero, 3, 3);

        var card = AddCardToHand(combat, HeroId, StandardCombatIds.StrikeCard);
        var goblinHealthBefore = combat.GetCombatant(GoblinId).Health.Current;

        combat.EnqueueEffect(new ApplyStatusEffectRequest(
            HeroId, StandardCombatIds.StunStatus, DurationTurns: 1));

        var slot = new PlayCardOutcomeSlot();
        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, card.Id, GoblinId, slot));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.False(slot.Value!.WasPlayed);
        Assert.Equal(goblinHealthBefore, combat.GetCombatant(GoblinId).Health.Current);
        Assert.Equal(3, hero.Resources[StandardCombatIds.EnergyResource].Current); // nothing was paid either
        Assert.Equal(CardZone.Hand, combat.GetCardZones(HeroId).GetCard(card.Id).Zone);
    }

    // …and an unstunned one still plays, so the gate is the rule and not the road.
    [Fact]
    public void AnUnhinderedCombatantStillPlaysThroughTheEffectRequestPath()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        EnsureEnergy(combat.GetCombatant(HeroId), 3, 3);

        var card = AddCardToHand(combat, HeroId, StandardCombatIds.StrikeCard);
        var goblinHealthBefore = combat.GetCombatant(GoblinId).Health.Current;

        var slot = new PlayCardOutcomeSlot();
        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, card.Id, GoblinId, slot));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.True(slot.Value!.WasPlayed);
        Assert.True(combat.GetCombatant(GoblinId).Health.Current < goblinHealthBefore);
    }

    private static void EnsureEnergy(CombatantState combatant, int current, int max)
    {
        if (combatant.Resources.TryGetValue(StandardCombatIds.EnergyResource, out var energy))
        {
            energy.SetMax(max);
            energy.SetCurrent(current);
            return;
        }

        combatant.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(current, max));
    }

    private static CardInstance AddCardToHand(
        CombatState combat, CombatantId ownerId, CardDefinitionId definitionId)
    {
        var card = new CardInstance(combat.CreateNextCardInstanceId(), definitionId, ownerId, CardZone.Hand);
        combat.GetCardZones(ownerId).AddCard(card);
        return card;
    }
}
