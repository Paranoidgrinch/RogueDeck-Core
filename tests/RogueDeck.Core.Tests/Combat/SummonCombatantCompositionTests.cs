using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Battery probe #52 Summon Skeleton: add a new minion to a side mid-combat that integrates into turn
// order and can be targeted/act immediately. Closes the gap with a runtime SummonCombatant native op:
// it creates a combatant (deterministic id), and CombatState.AddCombatant already wires it into turn
// order and gives it card zones. The summoned id is produced as an outcome for follow-up targeting.
public class SummonCombatantCompositionTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");
    private static readonly CombatantId SummonedId = new("summoned_000001");

    [Fact]
    public void Summon_AddsLivingEnemyToTurnOrderAndItIsImmediatelyTargetable()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var cardId = new CardDefinitionId("challenge.summon");
        builder.RegisterCard(new CardDefinitionBuilder(cardId, new PackageId("challenge"), "card.n", "card.d")
        {
            // Summon a 10-HP skeleton onto the enemy side, then hit all enemies — the new minion is
            // already an enemy, proving it integrated into the combat immediately.
            Program = new EffectProgram<CardPlayContext>(
                new CausalSequenceEffectNode<CardPlayContext>([
                    new SummonCombatantNode<CardPlayContext>(
                        StandardCombatIds.EnemyTeam,
                        new ConstantExpression<CardPlayContext>(10),
                        new CombatantDefinitionId("challenge.skeleton"),
                        "combatant.skeleton"),
                    new DealDamageNode<CardPlayContext>(
                        CombatantTargetSelectors.AllEnemiesOfSource,
                        new ConstantExpression<CardPlayContext>(3)),
                ])),
        });
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var hero = combat.GetCombatant(HeroId);
        hero.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(3, max: 3));
        var inst = new CardInstance(combat.CreateNextCardInstanceId(), cardId, HeroId, CardZone.Hand);
        combat.GetCardZones(HeroId).AddCard(inst);
        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, inst.Id, GoblinId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // The minion exists, on the enemy team, in turn order, with card zones.
        Assert.True(combat.TryGetCombatant(SummonedId, out var minion));
        Assert.Equal(StandardCombatIds.EnemyTeam, minion!.TeamId);
        Assert.Contains(SummonedId, combat.TurnOrder);
        Assert.True(combat.CardZonesByCombatant.ContainsKey(SummonedId));
        // It was an enemy when the follow-up AoE resolved: 10 − 3 = 7.
        Assert.Equal(7, minion.Health.Current);
        Assert.True(minion.IsAlive);
    }
}
