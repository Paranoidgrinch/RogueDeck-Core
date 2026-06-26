using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class GainBlockEffectTests
{
    [Fact]
    public void GainBlockCreatesBlockPoolWhenMissing()
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
            new GainBlockEffectRequest(
                TargetCombatantId: heroId,
                Amount: 6));

        var storedHero = combat.GetCombatant(heroId);
        var block = storedHero.DefensivePools[new DefensivePoolId("standard.block")];

        Assert.Equal(6, block.Current);
        Assert.Single(combat.CombatLog);
        Assert.Equal("BlockGained", combat.CombatLog[0].Type);
    }

    [Fact]
    public void GainBlockAddsToExistingBlockPool()
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
            new DefensivePoolId("standard.block"),
            new ValuePoolState(current: 4));

        combat.AddCombatant(hero);

        var resolver = new CombatEffectResolver();

        resolver.Resolve(
            combat,
            registry,
            new GainBlockEffectRequest(
                TargetCombatantId: heroId,
                Amount: 7));

        var storedHero = combat.GetCombatant(heroId);
        var block = storedHero.DefensivePools[new DefensivePoolId("standard.block")];

        Assert.Equal(11, block.Current);
        Assert.Single(combat.CombatLog);
        Assert.Equal("BlockGained", combat.CombatLog[0].Type);
    }

    [Fact]
    public void GainBlockRejectsNegativeAmount()
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

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            resolver.Resolve(
                combat,
                registry,
                new GainBlockEffectRequest(
                    TargetCombatantId: heroId,
                    Amount: -1)));
    }
}