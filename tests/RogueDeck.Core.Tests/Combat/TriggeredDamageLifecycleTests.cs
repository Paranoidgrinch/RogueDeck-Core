using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class TriggeredDamageLifecycleTests
{
    [Fact]
    public void DamageDealtTriggersReflectedDamageFromTriggeredDamageStatus()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CreateCombatWithHeroAndGoblin();

        var heroId = new CombatantId("hero_001");
        var goblinId = new CombatantId("goblin_001");

        var resolver = new CombatEffectResolver();

        resolver.Resolve(
            combat,
            registry,
            new ApplyStatusEffectRequest(
                TargetCombatantId: goblinId,
                StatusDefinitionId: new StatusDefinitionId("standard.thorns"),
                Stacks: 3));

        combat.EnqueueEffect(
            new DealDamageEffectRequest(
                TargetCombatantId: goblinId,
                Amount: 4,
                SourceCombatantId: heroId));

        var processor = new CombatQueueProcessor();

        processor.ResolvePendingQueues(combat, registry);

        var hero = combat.GetCombatant(heroId);
        var goblin = combat.GetCombatant(goblinId);

        Assert.Equal(17, hero.Health.Current);
        Assert.Equal(8, goblin.Health.Current);
        Assert.Equal(0, combat.PendingEffectCount);
        Assert.Equal(0, combat.PendingEventCount);
        Assert.Equal(2, combat.CombatLog.Count(entry => entry.Type == "DamageDealt"));
    }

    [Fact]
    public void ReflectedDamageDoesNotTriggerMoreReflectedDamage()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CreateCombatWithHeroAndGoblin();

        var heroId = new CombatantId("hero_001");
        var goblinId = new CombatantId("goblin_001");

        var resolver = new CombatEffectResolver();

        resolver.Resolve(
            combat,
            registry,
            new ApplyStatusEffectRequest(
                TargetCombatantId: heroId,
                StatusDefinitionId: new StatusDefinitionId("standard.thorns"),
                Stacks: 9));

        resolver.Resolve(
            combat,
            registry,
            new ApplyStatusEffectRequest(
                TargetCombatantId: goblinId,
                StatusDefinitionId: new StatusDefinitionId("standard.thorns"),
                Stacks: 3));

        combat.EnqueueEffect(
            new DealDamageEffectRequest(
                TargetCombatantId: goblinId,
                Amount: 4,
                SourceCombatantId: heroId));

        var processor = new CombatQueueProcessor();

        processor.ResolvePendingQueues(combat, registry);

        var hero = combat.GetCombatant(heroId);
        var goblin = combat.GetCombatant(goblinId);

        Assert.Equal(17, hero.Health.Current);
        Assert.Equal(8, goblin.Health.Current);
        Assert.Equal(2, combat.CombatLog.Count(entry => entry.Type == "DamageDealt"));
    }

    [Fact]
    public void BlockedDamageDoesNotTriggerReflectedDamage()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CreateCombatWithHeroAndGoblin();

        var heroId = new CombatantId("hero_001");
        var goblinId = new CombatantId("goblin_001");
        var goblin = combat.GetCombatant(goblinId);

        goblin.AddDefensivePool(
            StandardCombatIds.BlockDefensivePool,
            new ValuePoolState(current: 10));

        var resolver = new CombatEffectResolver();

        resolver.Resolve(
            combat,
            registry,
            new ApplyStatusEffectRequest(
                TargetCombatantId: goblinId,
                StatusDefinitionId: new StatusDefinitionId("standard.thorns"),
                Stacks: 3));

        combat.EnqueueEffect(
            new DealDamageEffectRequest(
                TargetCombatantId: goblinId,
                Amount: 4,
                SourceCombatantId: heroId));

        var processor = new CombatQueueProcessor();

        processor.ResolvePendingQueues(combat, registry);

        var hero = combat.GetCombatant(heroId);

        Assert.Equal(20, hero.Health.Current);
        Assert.Equal(1, combat.CombatLog.Count(entry => entry.Type == "DamageDealt"));
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
