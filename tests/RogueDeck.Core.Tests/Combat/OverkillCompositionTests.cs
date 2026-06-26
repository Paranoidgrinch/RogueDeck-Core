using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Battery probe #41 Overkill: damage beyond what kills the target splashes to a random other enemy.
// Closes the gap that the "excess past a lethal hit" amount was not observable — DamageOutcome gains an
// Overkill field (post-block, post-modifier damage minus the HP actually lost). The splash then
// composes: deal X to the target, read its Overkill, and deal that to a random other enemy.
public class OverkillCompositionTests
{
    private static readonly CombatantId HeroId = new("hero_001");

    private static CombatState WithEnemies(int targetHp)
    {
        var combat = CombatTestFactory.CreateCombatWithHero();
        combat.AddCombatant(new CombatantState(
            new CombatantId("goblin_000"), new CombatantDefinitionId("standard.goblin"),
            "combatant.goblin", StandardCombatIds.EnemyTeam, new HealthState(current: targetHp, max: 50)));
        for (var i = 1; i < 3; i++)
            combat.AddCombatant(new CombatantState(
                new CombatantId($"goblin_{i:D3}"), new CombatantDefinitionId("standard.goblin"),
                "combatant.goblin", StandardCombatIds.EnemyTeam, new HealthState(current: 50, max: 50)));
        return combat;
    }

    [Fact]
    public void Overkill_ExcessPastLethalSplashesToARandomOtherEnemy()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var dmg = new EffectResultKey<OrderedTargetOutcomes<DamageOutcome>>("overkill_dmg");
        var cardId = new CardDefinitionId("challenge.overkill");
        builder.RegisterCard(new CardDefinitionBuilder(cardId, new PackageId("challenge"), "card.n", "card.d")
        {
            Program = new EffectProgram<CardPlayContext>(
                new CausalSequenceEffectNode<CardPlayContext>([
                    new DealDamageNode<CardPlayContext>(
                        CombatantTargetSelectors.EventTarget,
                        new ConstantExpression<CardPlayContext>(10), resultKey: dmg),
                    new RandomTargetSelectionNode<CardPlayContext>(
                        CombatantTargetSelectors.Except(
                            CombatantTargetSelectors.AllEnemiesOfSource, CombatantTargetSelectors.EventTarget),
                        new ConstantExpression<CardPlayContext>(1),
                        new DealDamageNode<CardPlayContext>(
                            CombatantTargetSelectors.IterationTarget,
                            new PreviousOutcomeFieldExpression<CardPlayContext, DamageOutcome>(
                                dmg, o => o.Overkill))),
                ])),
        });
        var registry = builder.Build();

        var combat = WithEnemies(targetHp: 5); // target dies to 10 → 5 overkill splashes
        var hero = combat.GetCombatant(HeroId);
        hero.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(3, max: 3));
        var inst = new CardInstance(combat.CreateNextCardInstanceId(), cardId, HeroId, CardZone.Hand);
        combat.GetCardZones(HeroId).AddCard(inst);
        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, inst.Id, new CombatantId("goblin_000")));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(0, combat.GetCombatant(new CombatantId("goblin_000")).Health.Current); // target down
        // Exactly one of the two other enemies took the 5 overkill splash; total splash damage = 5.
        var other1 = combat.GetCombatant(new CombatantId("goblin_001")).Health.Current;
        var other2 = combat.GetCombatant(new CombatantId("goblin_002")).Health.Current;
        Assert.Equal(95, other1 + other2); // 100 − 5
        Assert.True(other1 == 45 || other2 == 45);
    }

    [Fact]
    public void Overkill_IsZeroWhenTheHitDoesNotKill()
    {
        var combat = WithEnemies(targetHp: 50);
        var target = combat.GetCombatant(new CombatantId("goblin_000"));
        var slot = new DamageOutcomeSlot();
        new CombatEffectResolver().Resolve(
            combat, CombatTestFactory.CreateStandardRegistry(),
            new DealDamageEffectRequest(target.Id, 10, OutcomeSlot: slot));

        Assert.Equal(0, slot.Value!.Overkill);   // 10 < 50 → no overkill
        Assert.Equal(10, slot.Value!.HealthLost);
    }
}
