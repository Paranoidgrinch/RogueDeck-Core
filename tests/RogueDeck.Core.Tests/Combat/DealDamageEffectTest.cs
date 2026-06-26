using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class DealDamageEffectTests
{
    [Fact]
    public void DealDamageReducesTargetHealth()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        var combat = new CombatState(
            new CombatId("combat_001"),
            randomSeed: 12345);

        var goblinId = new CombatantId("goblin_001");

        var goblin = new CombatantState(
            goblinId,
            new CombatantDefinitionId("standard.goblin"),
            "combatant.goblin",
            new TeamId("enemy"),
            new HealthState(current: 18, max: 24));

        combat.AddCombatant(goblin);

        var resolver = new CombatEffectResolver();

        resolver.Resolve(
            combat,
            registry,
            new DealDamageEffectRequest(
                TargetCombatantId: goblinId,
                Amount: 7));

        var storedGoblin = combat.GetCombatant(goblinId);

        Assert.Equal(11, storedGoblin.Health.Current);
        Assert.Single(combat.CombatLog);
        Assert.Equal("DamageDealt", combat.CombatLog[0].Type);
    }

    [Fact]
    public void DealDamageCannotReduceHealthBelowZero()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        var combat = new CombatState(
            new CombatId("combat_001"),
            randomSeed: 12345);

        var goblinId = new CombatantId("goblin_001");

        var goblin = new CombatantState(
            goblinId,
            new CombatantDefinitionId("standard.goblin"),
            "combatant.goblin",
            new TeamId("enemy"),
            new HealthState(current: 5, max: 24));

        combat.AddCombatant(goblin);

        var resolver = new CombatEffectResolver();

        resolver.Resolve(
            combat,
            registry,
            new DealDamageEffectRequest(
                TargetCombatantId: goblinId,
                Amount: 999));

        var storedGoblin = combat.GetCombatant(goblinId);

        Assert.Equal(0, storedGoblin.Health.Current);
        Assert.Single(combat.CombatLog);
        Assert.Equal("DamageDealt", combat.CombatLog[0].Type);
    }

    [Fact]
    public void DealDamageRejectsNegativeAmount()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        var combat = new CombatState(
            new CombatId("combat_001"),
            randomSeed: 12345);

        var goblinId = new CombatantId("goblin_001");

        var goblin = new CombatantState(
            goblinId,
            new CombatantDefinitionId("standard.goblin"),
            "combatant.goblin",
            new TeamId("enemy"),
            new HealthState(current: 18, max: 24));

        combat.AddCombatant(goblin);

        var resolver = new CombatEffectResolver();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            resolver.Resolve(
                combat,
                registry,
                new DealDamageEffectRequest(
                    TargetCombatantId: goblinId,
                    Amount: -1)));
    }

    [Fact]
    public void DealDamageConsumesBlockBeforeHealth()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        var combat = new CombatState(
            new CombatId("combat_001"),
            randomSeed: 12345);

        var goblinId = new CombatantId("goblin_001");

        var goblin = new CombatantState(
            goblinId,
            new CombatantDefinitionId("standard.goblin"),
            "combatant.goblin",
            new TeamId("enemy"),
            new HealthState(current: 18, max: 24));

        goblin.AddDefensivePool(
            new DefensivePoolId("standard.block"),
            new ValuePoolState(current: 6));

        combat.AddCombatant(goblin);

        var resolver = new CombatEffectResolver();

        resolver.Resolve(
            combat,
            registry,
            new DealDamageEffectRequest(
                TargetCombatantId: goblinId,
                Amount: 10));

        var storedGoblin = combat.GetCombatant(goblinId);
        var block = storedGoblin.DefensivePools[new DefensivePoolId("standard.block")];

        Assert.Equal(14, storedGoblin.Health.Current);
        Assert.Equal(0, block.Current);
        Assert.Single(combat.CombatLog);
        Assert.Equal("DamageDealt", combat.CombatLog[0].Type);
    }

    [Fact]
    public void DealDamageOnlyConsumesBlockWhenBlockIsEnough()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        var combat = new CombatState(
            new CombatId("combat_001"),
            randomSeed: 12345);

        var goblinId = new CombatantId("goblin_001");

        var goblin = new CombatantState(
            goblinId,
            new CombatantDefinitionId("standard.goblin"),
            "combatant.goblin",
            new TeamId("enemy"),
            new HealthState(current: 18, max: 24));

        goblin.AddDefensivePool(
            new DefensivePoolId("standard.block"),
            new ValuePoolState(current: 12));

        combat.AddCombatant(goblin);

        var resolver = new CombatEffectResolver();

        resolver.Resolve(
            combat,
            registry,
            new DealDamageEffectRequest(
                TargetCombatantId: goblinId,
                Amount: 7));

        var storedGoblin = combat.GetCombatant(goblinId);
        var block = storedGoblin.DefensivePools[new DefensivePoolId("standard.block")];

        Assert.Equal(18, storedGoblin.Health.Current);
        Assert.Equal(5, block.Current);
        Assert.Single(combat.CombatLog);
        Assert.Equal("DamageDealt", combat.CombatLog[0].Type);
    }
}
