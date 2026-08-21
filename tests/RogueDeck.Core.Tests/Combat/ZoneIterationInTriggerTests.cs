using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// A rule that answers "a card was played" by reaching into a ZONE — "after your fourth card, discard the
// oldest one still in hand" — is a shape content wants often. It is worth its own test because a trigger's
// program runs in a different context from a card's, and a zone iteration has to find the owner from that
// context, not from the card that happened to be played.
public class ZoneIterationInTriggerTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");
    private static readonly CardDefinitionId PlainId = new("test.plain");

    [Fact]
    public void A_card_played_trigger_can_reach_into_the_players_hand()
    {
        var registry = BuildRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.GetCombatant(HeroId).SetResource(StandardCombatIds.EnergyResource, new ValuePoolState(10, max: 10));

        // Four cards in hand; playing one should cost the hand TWO — the card played and the one the rule
        // reaches in and discards.
        var played = AddToHand(combat, PlainId);
        AddToHand(combat, PlainId);
        AddToHand(combat, PlainId);
        AddToHand(combat, PlainId);

        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, played.Id, GoblinId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(2, combat.GetCardZones(HeroId).Hand.Count);
    }

    private static CombatDefinitionRegistry BuildRegistry()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterCard(new CardDefinitionBuilder(PlainId, new PackageId("test"), "card.n", "card.d"));

        // The rule under test: when a card is played, send the first card still in hand to the discard pile.
        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.CardPlayed.Define(
                new TriggeredEffectDefinitionId("test.clear_a_seat"),
                new EffectProgram<CardPlayedTriggeredEffectContext>(
                    new ForEachCardInZoneNode<CardPlayedTriggeredEffectContext>(
                        CombatantTargetSelectors.Source, CardZone.Hand,
                        new MoveCardToZoneNode<CardPlayedTriggeredEffectContext>(
                            CombatantTargetSelectors.Source,
                            new IteratedCardExpression<CardPlayedTriggeredEffectContext>(),
                            CardZone.DiscardPile),
                        takeFirst: 1))));

        return builder.Build();
    }

    private static CardInstance AddToHand(CombatState combat, CardDefinitionId definition)
    {
        var card = new CardInstance(combat.CreateNextCardInstanceId(), definition, HeroId, CardZone.Hand);
        combat.GetCardZones(HeroId).AddCard(card);
        return card;
    }
}
