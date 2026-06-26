using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Battery probe #14 Doppelgänger: put a copy of the card you just played into your hand. Closes the gap
// that CreateCardInstanceNode only took a *constant* card definition id — the new CreateCardCopyNode
// resolves the definition at execution time from a read card instance (here PlayedCardInstance), so a
// card can clone itself / the last-played card without naming its own definition.
public class DoppelgangerCompositionTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    [Fact]
    public void Doppelganger_CopiesThePlayedCardIntoHand()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var cardId = new CardDefinitionId("challenge.doppelganger");
        builder.RegisterCard(new CardDefinitionBuilder(cardId, new PackageId("challenge"), "card.n", "card.d")
        {
            Program = new EffectProgram<CardPlayContext>(
                new CreateCardCopyNode<CardPlayContext>(
                    CombatantTargetSelectors.Source, // the player receives the copy
                    new PlayedCardInstanceExpression<CardPlayContext>(),
                    CardZone.Hand)),
        });
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);
        hero.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(3, max: 3));
        var played = new CardInstance(combat.CreateNextCardInstanceId(), cardId, HeroId, CardZone.Hand);
        combat.GetCardZones(HeroId).AddCard(played);

        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, played.Id, GoblinId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var zones = combat.GetCardZones(HeroId);
        // The original moved to discard on completion; the freshly created copy is in hand.
        var hand = zones.GetCardsInZone(CardZone.Hand);
        var copy = Assert.Single(hand);
        Assert.Equal(cardId, copy.DefinitionId);   // a copy of the played card's definition
        Assert.NotEqual(played.Id, copy.Id);       // a distinct new instance
        Assert.Contains(zones.GetCardsInZone(CardZone.DiscardPile), c => c.Id == played.Id);
    }
}
