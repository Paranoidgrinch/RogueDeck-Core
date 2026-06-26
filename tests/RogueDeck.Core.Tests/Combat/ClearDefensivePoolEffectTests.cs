using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class ClearDefensivePoolEffectTests
{
    [Fact]
    public void ClearDefensivePoolSetsExistingPoolToZero()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        var combat = new CombatState(
            new CombatId("combat_001"),
            randomSeed: 12345);

        var heroId = new CombatantId("hero_001");

        var hero = new CombatantState(
            heroId,
            new CombatantDefinitionId("standard.hero"),
            "combatant.hero",
            new TeamId("player"),
            new HealthState(current: 20, max: 20));

        hero.AddDefensivePool(
            StandardCombatIds.BlockDefensivePool,
            new ValuePoolState(current: 9));

        combat.AddCombatant(hero);

        var resolver = new CombatEffectResolver();

        resolver.Resolve(
            combat,
            registry,
            new ClearDefensivePoolEffectRequest(
                TargetCombatantId: heroId,
                PoolId: StandardCombatIds.BlockDefensivePool));

        var storedHero = combat.GetCombatant(heroId);
        var block = storedHero.DefensivePools[StandardCombatIds.BlockDefensivePool];

        Assert.Equal(0, block.Current);
        Assert.Single(combat.CombatLog);
        Assert.Equal("DefensivePoolCleared", combat.CombatLog[0].Type);
    }

    [Fact]
    public void ClearDefensivePoolDoesNotCreateMissingPool()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        var combat = new CombatState(
            new CombatId("combat_001"),
            randomSeed: 12345);

        var heroId = new CombatantId("hero_001");

        var hero = new CombatantState(
            heroId,
            new CombatantDefinitionId("standard.hero"),
            "combatant.hero",
            new TeamId("player"),
            new HealthState(current: 20, max: 20));

        combat.AddCombatant(hero);

        var resolver = new CombatEffectResolver();

        resolver.Resolve(
            combat,
            registry,
            new ClearDefensivePoolEffectRequest(
                TargetCombatantId: heroId,
                PoolId: StandardCombatIds.BlockDefensivePool));

        var storedHero = combat.GetCombatant(heroId);

        Assert.False(storedHero.DefensivePools.ContainsKey(StandardCombatIds.BlockDefensivePool));
        Assert.Single(combat.CombatLog);
        Assert.Equal("DefensivePoolCleared", combat.CombatLog[0].Type);
    }
}