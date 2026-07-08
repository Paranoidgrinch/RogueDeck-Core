using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Party deckbuilding A1 (the enabler): the card-play machinery is per-combatant, not hero-bound — a player-team
// combatant beyond the hero can play a card from its own hand, spending its own energy, and its effect resolves
// like any card. (Draw/discard/play all key on the acting combatant; validators are skipped on the play path.)
public class PartyCardPlayTests
{
    private static readonly CombatantId AllyId = new("ally");
    private static readonly CombatantId GoblinId = new("goblin");

    private static CombatantState Combatant(CombatantId id, TeamId team, int hp) =>
        new(id, new CombatantDefinitionId("unit"), "combatant.unit", team, new HealthState(hp, hp));

    [Fact]
    public void A_non_hero_player_combatant_plays_a_card_from_its_own_hand()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var cardId = new CardDefinitionId("ally_strike");
        builder.RegisterCard(new CardDefinitionBuilder(cardId, new PackageId("test"),
            $"card.{cardId}.name", $"card.{cardId}.desc")
        {
            Program = new EffectProgram<CardPlayContext>(new DealDamageNode<CardPlayContext>(
                CombatantTargetSelectors.AllEnemiesOfSource, new ConstantExpression<CardPlayContext>(4))),
        });
        var registry = builder.Build();

        var combat = new CombatState(new CombatId("party"), randomSeed: 1);
        combat.AddCombatant(Combatant(AllyId, StandardCombatIds.PlayerTeam, 20));
        combat.AddCombatant(Combatant(GoblinId, StandardCombatIds.EnemyTeam, 20));

        // The ally has its own energy and the card in its own hand.
        combat.GetCombatant(AllyId).AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(1, max: 1));
        var card = new CardInstance(combat.CreateNextCardInstanceId(), cardId, AllyId, CardZone.Hand);
        combat.GetCardZones(AllyId).AddCard(card);

        combat.EnqueueEffect(new PlayCardEffectRequest(AllyId, card.Id, GoblinId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(16, combat.GetCombatant(GoblinId).Health.Current); // the ally's card struck for 4
        Assert.Contains(combat.GetCardZones(AllyId).DiscardPile, c => c.Id == card.Id); // and moved to its discard
    }
}
