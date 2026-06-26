using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class DamageOverTimeLifecycleTests
{
    [Fact]
    public void StartCurrentTurnDealsDamageForDamageOverTimeStatusStacks()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CreateCombatWithHeroAndGoblin();

        var heroId = new CombatantId("hero_001");

        var resolver = new CombatEffectResolver();

        resolver.Resolve(
            combat,
            registry,
            new ApplyStatusEffectRequest(
                TargetCombatantId: heroId,
                StatusDefinitionId: new StatusDefinitionId("standard.poison"),
                Stacks: 4));

        var processor = new CombatTurnProcessor();

        processor.StartCurrentTurn(combat, registry);

        var hero = combat.GetCombatant(heroId);

        Assert.Equal(16, hero.Health.Current);
        Assert.Single(hero.Statuses);
        Assert.Contains(combat.CombatLog, entry => entry.Type == "DamageDealt");
        Assert.Equal(0, combat.PendingEventCount);
        Assert.Equal(0, combat.PendingEffectCount);
    }

    [Fact]
    public void StartCurrentTurnDoesNotDealDamageForNonDamageOverTimeStatus()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CreateCombatWithHeroAndGoblin();

        var heroId = new CombatantId("hero_001");

        var resolver = new CombatEffectResolver();

        resolver.Resolve(
            combat,
            registry,
            new ApplyStatusEffectRequest(
                TargetCombatantId: heroId,
                StatusDefinitionId: new StatusDefinitionId("standard.strength"),
                Stacks: 4));

        var processor = new CombatTurnProcessor();

        processor.StartCurrentTurn(combat, registry);

        var hero = combat.GetCombatant(heroId);

        Assert.Equal(20, hero.Health.Current);
        Assert.Single(hero.Statuses);
        Assert.DoesNotContain(combat.CombatLog, entry => entry.Type == "DamageDealt");
        Assert.Equal(0, combat.PendingEventCount);
        Assert.Equal(0, combat.PendingEffectCount);
    }

    [Fact]
    public void DamageOverTimeHappensAfterTurnStartBlockClear()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CreateCombatWithHeroAndGoblin();

        var heroId = new CombatantId("hero_001");
        var hero = combat.GetCombatant(heroId);

        hero.AddDefensivePool(
            StandardCombatIds.BlockDefensivePool,
            new ValuePoolState(current: 3));

        var resolver = new CombatEffectResolver();

        resolver.Resolve(
            combat,
            registry,
            new ApplyStatusEffectRequest(
                TargetCombatantId: heroId,
                StatusDefinitionId: new StatusDefinitionId("standard.poison"),
                Stacks: 5));

        var processor = new CombatTurnProcessor();

        processor.StartCurrentTurn(combat, registry);

        var block = hero.DefensivePools[StandardCombatIds.BlockDefensivePool];

        Assert.Equal(15, hero.Health.Current);
        Assert.Equal(0, block.Current);
        Assert.Contains(combat.CombatLog, entry => entry.Type == "DefensivePoolCleared");
        Assert.Contains(combat.CombatLog, entry => entry.Type == "DamageDealt");
    }

    private static CombatState CreateCombatWithHeroAndGoblin()
    {
        var combat = new CombatState(
            new CombatId("combat_001"),
            randomSeed: 12345);

        var hero = new CombatantState(
            new CombatantId("hero_001"),
            new CombatantDefinitionId("standard.hero"),
            "combatant.hero",
            new TeamId("player"),
            new HealthState(current: 20, max: 20));

        var goblin = new CombatantState(
            new CombatantId("goblin_001"),
            new CombatantDefinitionId("standard.goblin"),
            "combatant.goblin",
            new TeamId("enemy"),
            new HealthState(current: 12, max: 12));

        combat.AddCombatant(hero);
        combat.AddCombatant(goblin);

        return combat;
    }
}

