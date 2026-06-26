using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class HealEffectTests
{
    [Fact]
    public void HealIncreasesTargetHealth()
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
            new HealthState(current: 8, max: 20));

        combat.AddCombatant(hero);

        var resolver = new CombatEffectResolver();

        resolver.Resolve(
            combat,
            registry,
            new HealEffectRequest(
                TargetCombatantId: heroId,
                Amount: 5));

        var storedHero = combat.GetCombatant(heroId);

        Assert.Equal(13, storedHero.Health.Current);
        Assert.Single(combat.CombatLog);
        Assert.Equal("Healed", combat.CombatLog[0].Type);
    }

    [Fact]
    public void HealCannotIncreaseHealthAboveMax()
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
            new HealthState(current: 18, max: 20));

        combat.AddCombatant(hero);

        var resolver = new CombatEffectResolver();

        resolver.Resolve(
            combat,
            registry,
            new HealEffectRequest(
                TargetCombatantId: heroId,
                Amount: 999));

        var storedHero = combat.GetCombatant(heroId);

        Assert.Equal(20, storedHero.Health.Current);
        Assert.Single(combat.CombatLog);
        Assert.Equal("Healed", combat.CombatLog[0].Type);
    }

    [Fact]
    public void HealRejectsNegativeAmount()
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
            new HealthState(current: 8, max: 20));

        combat.AddCombatant(hero);

        var resolver = new CombatEffectResolver();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            resolver.Resolve(
                combat,
                registry,
                new HealEffectRequest(
                    TargetCombatantId: heroId,
                    Amount: -1)));
    }
}
