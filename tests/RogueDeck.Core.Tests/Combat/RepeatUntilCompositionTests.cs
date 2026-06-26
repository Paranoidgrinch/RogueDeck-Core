using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Battery probe #22 Avalanche: deal escalating damage to all enemies each pass (+1 per pass) until a
// condition holds — a condition-bounded loop with a running accumulator. Closes the gap that the engine
// had Repeat-by-fixed-count and ForEach but no while/until construct. New RepeatUntilEffectNode runs its
// body at least once, repeats while the stop condition is false (capped by MaxIterations), and exposes
// the 0-based pass number to the body as IterationIndex so each pass can escalate.
public class RepeatUntilCompositionTests
{
    private static readonly CombatantId HeroId = new("hero_001");

    private static CombatState WithTwoEnemies(int firstHp, int secondHp)
    {
        var combat = CombatTestFactory.CreateCombatWithHero();
        combat.AddCombatant(new CombatantState(
            new CombatantId("goblin_000"), new CombatantDefinitionId("standard.goblin"),
            "combatant.goblin", StandardCombatIds.EnemyTeam, new HealthState(current: firstHp, max: firstHp)));
        combat.AddCombatant(new CombatantState(
            new CombatantId("goblin_001"), new CombatantDefinitionId("standard.goblin"),
            "combatant.goblin", StandardCombatIds.EnemyTeam, new HealthState(current: secondHp, max: secondHp)));
        return combat;
    }

    [Fact]
    public void Avalanche_EscalatingDamageEachPassUntilTargetDies()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var cardId = new CardDefinitionId("challenge.avalanche");
        builder.RegisterCard(new CardDefinitionBuilder(cardId, new PackageId("challenge"), "card.n", "card.d")
        {
            Program = new EffectProgram<CardPlayContext>(
                new RepeatUntilEffectNode<CardPlayContext>(
                    // stop once the targeted enemy is dead
                    new ComparisonExpression<CardPlayContext>(
                        new CombatantCurrentHealthExpression<CardPlayContext>(CombatantTargetSelectors.EventTarget),
                        ComparisonOperator.LessOrEqual, new ConstantExpression<CardPlayContext>(0)),
                    // each pass deals (pass# + 1) to all enemies → 1, 2, 3, …
                    new DealDamageNode<CardPlayContext>(
                        CombatantTargetSelectors.AllEnemiesOfSource,
                        new AddExpression<CardPlayContext>(
                            new IterationIndexExpression<CardPlayContext>(),
                            new ConstantExpression<CardPlayContext>(1))),
                    maxIterations: 10)),
        });
        var registry = builder.Build();

        var combat = WithTwoEnemies(firstHp: 21, secondHp: 100); // 1+2+3+4+5+6 = 21 → 6 passes
        var hero = combat.GetCombatant(HeroId);
        hero.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(3, max: 3));
        var inst = new CardInstance(combat.CreateNextCardInstanceId(), cardId, HeroId, CardZone.Hand);
        combat.GetCardZones(HeroId).AddCard(inst);
        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, inst.Id, new CombatantId("goblin_000")));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(0, combat.GetCombatant(new CombatantId("goblin_000")).Health.Current);
        // The second enemy took the same escalating passes: 1+2+3+4+5+6 = 21 over exactly 6 passes.
        Assert.Equal(79, combat.GetCombatant(new CombatantId("goblin_001")).Health.Current);
    }

    [Fact]
    public void RepeatUntil_StopsAtMaxIterationsWhenConditionNeverHolds()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var cardId = new CardDefinitionId("challenge.endless");
        builder.RegisterCard(new CardDefinitionBuilder(cardId, new PackageId("challenge"), "card.n", "card.d")
        {
            Program = new EffectProgram<CardPlayContext>(
                new RepeatUntilEffectNode<CardPlayContext>(
                    new ComparisonExpression<CardPlayContext>( // never stops on its own (0 == 1 is false)
                        new ConstantExpression<CardPlayContext>(0),
                        ComparisonOperator.Equal, new ConstantExpression<CardPlayContext>(1)),
                    new DealDamageNode<CardPlayContext>(
                        CombatantTargetSelectors.EventTarget, new ConstantExpression<CardPlayContext>(1)),
                    maxIterations: 4)),
        });
        var registry = builder.Build();

        var combat = WithTwoEnemies(firstHp: 100, secondHp: 100);
        var hero = combat.GetCombatant(HeroId);
        hero.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(3, max: 3));
        var inst = new CardInstance(combat.CreateNextCardInstanceId(), cardId, HeroId, CardZone.Hand);
        combat.GetCardZones(HeroId).AddCard(inst);
        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, inst.Id, new CombatantId("goblin_000")));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        // The safety cap stops the loop after exactly 4 passes (4 × 1 damage).
        Assert.Equal(96, combat.GetCombatant(new CombatantId("goblin_000")).Health.Current);
    }
}
