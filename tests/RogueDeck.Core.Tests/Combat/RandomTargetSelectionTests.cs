using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Stage-1 composition substrate, 🌀 RNG-driven target selection (battery probes #29, #51 Chain
// Lightning, building block for #37). RandomTargetSelectionNode picks N distinct random targets from
// a candidate pool and runs its body once per chosen target. The RNG mutation lives in the executor
// (selectors stay pure), so replays with the same seed are deterministic.
public class RandomTargetSelectionTests
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
                new HealthState(current: 12, max: 12)));
        return combat;
    }

    private static CardDefinitionId BuildRandomStrike(CombatDefinitionRegistryBuilder builder, int count)
    {
        var cardId = new CardDefinitionId($"challenge.random_strike_{count}");
        var card = new CardDefinitionBuilder(cardId, new PackageId("challenge"),
            $"card.{cardId}.name", $"card.{cardId}.desc")
        {
            Program = new EffectProgram<CardPlayContext>(
                new RandomTargetSelectionNode<CardPlayContext>(
                    CombatantTargetSelectors.AllEnemiesOfSource,
                    new ConstantExpression<CardPlayContext>(count),
                    new DealDamageNode<CardPlayContext>(
                        CombatantTargetSelectors.IterationTarget,
                        new ConstantExpression<CardPlayContext>(5)))),
        };
        builder.RegisterCard(card);
        return cardId;
    }

    private static void Play(CombatState combat, CombatDefinitionRegistry registry, CardDefinitionId cardId)
    {
        var hero = combat.GetCombatant(HeroId);
        if (!hero.Resources.ContainsKey(StandardCombatIds.EnergyResource))
            hero.AddResource(StandardCombatIds.EnergyResource, new ValuePoolState(1, max: 1));
        var inst = new CardInstance(combat.CreateNextCardInstanceId(), cardId, HeroId, CardZone.Hand);
        combat.GetCardZones(HeroId).AddCard(inst);
        combat.EnqueueEffect(new PlayCardEffectRequest(HeroId, inst.Id, null));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    private static IReadOnlyList<CombatantId> DamagedEnemies(CombatState combat) =>
        combat.Combatants
            .Where(c => c.TeamId == StandardCombatIds.EnemyTeam && c.Health.Current < c.Health.Max)
            .Select(c => c.Id)
            .ToList();

    [Fact]
    public void PicksRequestedCountOfDistinctTargets()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var cardId = BuildRandomStrike(builder, count: 2);
        var registry = builder.Build();

        var combat = CombatWithEnemies(3);
        Play(combat, registry, cardId);

        // Exactly two of the three enemies took 5 damage; the third is untouched.
        Assert.Equal(2, DamagedEnemies(combat).Count);
    }

    [Fact]
    public void AdvancesRandomStepExactlyOnce()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var cardId = BuildRandomStrike(builder, count: 2);
        var registry = builder.Build();

        var combat = CombatWithEnemies(3);
        var stepBefore = combat.RandomStep;
        Play(combat, registry, cardId);

        Assert.Equal(stepBefore + 1, combat.RandomStep);
    }

    [Fact]
    public void SameSeedAndProgram_PicksSameTargets()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var cardId = BuildRandomStrike(builder, count: 2);
        var registry = builder.Build();

        var first = CombatWithEnemies(3);
        Play(first, registry, cardId);

        var second = CombatWithEnemies(3);
        Play(second, registry, cardId);

        Assert.Equal(DamagedEnemies(first), DamagedEnemies(second));
    }

    [Fact]
    public void CountAbovePoolSize_HitsEveryTargetOnce_AndStillAdvancesStep()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var cardId = BuildRandomStrike(builder, count: 10);
        var registry = builder.Build();

        var combat = CombatWithEnemies(3);
        var stepBefore = combat.RandomStep;
        Play(combat, registry, cardId);

        Assert.Equal(3, DamagedEnemies(combat).Count);
        Assert.Equal(stepBefore + 1, combat.RandomStep);
    }

    [Fact]
    public void ZeroCount_IsNoOp_AndDoesNotAdvanceStep()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var cardId = BuildRandomStrike(builder, count: 0);
        var registry = builder.Build();

        var combat = CombatWithEnemies(3);
        var stepBefore = combat.RandomStep;
        Play(combat, registry, cardId);

        Assert.Empty(DamagedEnemies(combat));
        Assert.Equal(stepBefore, combat.RandomStep);
    }
}
