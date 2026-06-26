using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Stage-1 composition substrate: the iteration-index expression. Inside a ForEach /
// RandomTargetSelection body, IterationIndexExpression reads the 0-based position of the current
// iteration — letting a per-iteration amount scale by position (battery probes #51 Chain Lightning's
// descending 6/4/2, #10 Momentum's "+2 per earlier attack"). Outside any loop it reads 0.
public class IterationIndexTests
{
    private static readonly CombatantId HeroId = new("hero_001");

    private static CombatState CombatWithEnemies(int enemyCount)
    {
        var combat = CombatTestFactory.CreateCombatWithHero();
        for (var i = 0; i < enemyCount; i++)
            combat.AddCombatant(new CombatantState(
                new CombatantId($"goblin_{i:D3}"),
                new CombatantDefinitionId("standard.goblin"),
                "combatant.goblin",
                StandardCombatIds.EnemyTeam,
                new HealthState(current: 50, max: 50)));
        return combat;
    }

    private static void PlayProgram(CombatState combat, CombatDefinitionRegistryBuilder builder,
        EffectProgram<CardPlayContext> program)
    {
        var cardId = new CardDefinitionId("challenge.index_card");
        builder.RegisterCard(new CardDefinitionBuilder(cardId, new PackageId("challenge"),
            $"card.{cardId}.name", $"card.{cardId}.desc")
        {
            Program = program,
        });
        var registry = builder.Build();

        var hero = combat.GetCombatant(HeroId);
        hero.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(1, max: 1));
        var inst = new CardInstance(combat.CreateNextCardInstanceId(), cardId, HeroId, CardZone.Hand);
        combat.GetCardZones(HeroId).AddCard(inst);
        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, inst.Id, null));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    private static List<int> EnemyDamageTaken(CombatState combat) =>
        combat.Combatants
            .Where(c => c.TeamId == StandardCombatIds.EnemyTeam)
            .Select(c => c.Health.Max - c.Health.Current)
            .Where(d => d > 0)
            .OrderByDescending(d => d)
            .ToList();

    [Fact]
    public void ForEach_ScalesAmountByIterationIndex()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatWithEnemies(3);

        // Deal (index + 1) damage to each enemy → distinct amounts {1, 2, 3}.
        PlayProgram(combat, builder, new EffectProgram<CardPlayContext>(
            new ForEachTargetEffectNode<CardPlayContext>(
                CombatantTargetSelectors.AllEnemiesOfSource,
                new DealDamageNode<CardPlayContext>(
                    CombatantTargetSelectors.IterationTarget,
                    new AddExpression<CardPlayContext>(
                        new ConstantExpression<CardPlayContext>(1),
                        new IterationIndexExpression<CardPlayContext>())))));

        Assert.Equal(new List<int> { 3, 2, 1 }, EnemyDamageTaken(combat));
    }

    [Fact]
    public void RandomSelection_ChainLightningDescendingAmounts()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatWithEnemies(3);

        // Chain Lightning: pick all three in random order, deal 6 − 2×index → {6, 4, 2}.
        PlayProgram(combat, builder, new EffectProgram<CardPlayContext>(
            new RandomTargetSelectionNode<CardPlayContext>(
                CombatantTargetSelectors.AllEnemiesOfSource,
                new ConstantExpression<CardPlayContext>(3),
                new DealDamageNode<CardPlayContext>(
                    CombatantTargetSelectors.IterationTarget,
                    new SubtractExpression<CardPlayContext>(
                        new ConstantExpression<CardPlayContext>(6),
                        new MultiplyExpression<CardPlayContext>(
                            new ConstantExpression<CardPlayContext>(2),
                            new IterationIndexExpression<CardPlayContext>()))))));

        Assert.Equal(new List<int> { 6, 4, 2 }, EnemyDamageTaken(combat));
    }

    [Fact]
    public void OutsideLoop_IterationIndexReadsZero()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatWithEnemies(1);

        // No loop: amount = 10 + index → index defaults to 0 → 10 damage.
        PlayProgram(combat, builder, new EffectProgram<CardPlayContext>(
            new DealDamageNode<CardPlayContext>(
                CombatantTargetSelectors.AllEnemiesOfSource,
                new AddExpression<CardPlayContext>(
                    new ConstantExpression<CardPlayContext>(10),
                    new IterationIndexExpression<CardPlayContext>()))));

        Assert.Equal(new List<int> { 10 }, EnemyDamageTaken(combat));
    }
}
