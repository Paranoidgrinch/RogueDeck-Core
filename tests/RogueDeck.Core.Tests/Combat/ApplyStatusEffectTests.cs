using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class ApplyStatusEffectTests
{
    [Fact]
    public void ApplyStatusCreatesStatusInstanceOnTargetCombatant()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        var combat = new CombatState(
            new CombatId("combat_001"),
            randomSeed: 12345);

        var playerId = new CombatantId("player_001");
        var goblinId = new CombatantId("goblin_001");

        var player = new CombatantState(
            playerId,
            new CombatantDefinitionId("standard.player"),
            "combatant.player",
            new TeamId("player"),
            new HealthState(50, 50));

        var goblin = new CombatantState(
            goblinId,
            new CombatantDefinitionId("standard.goblin"),
            "combatant.goblin",
            new TeamId("enemy"),
            new HealthState(18, 24));

        combat.AddCombatant(player);
        combat.AddCombatant(goblin);

        var resolver = new CombatEffectResolver();

        var applyPoison = new ApplyStatusEffectRequest(
            TargetCombatantId: goblinId,
            StatusDefinitionId: new StatusDefinitionId("standard.poison"),
            SourceCombatantId: playerId,
            Stacks: 5);

        resolver.Resolve(combat, registry, applyPoison);

        var storedGoblin = combat.GetCombatant(goblinId);
        var poison = Assert.Single(storedGoblin.Statuses);

        Assert.Equal(new StatusDefinitionId("standard.poison"), poison.DefinitionId);
        Assert.Equal(goblinId, poison.OwnerCombatantId);
        Assert.Equal(playerId, poison.SourceCombatantId);
        Assert.Equal(5, poison.Stacks);
        Assert.Equal(StatusPolarity.Debuff, poison.Polarity);
        Assert.Contains(new TagId("damage_over_time"), poison.Tags);

        Assert.Single(combat.CombatLog);
        Assert.Equal("StatusApplied", combat.CombatLog[0].Type);
    }

    [Fact]
    public void ApplyStatusMergesWithExistingStatusWhenDefinitionUsesMergeBehavior()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        var combat = new CombatState(
            new CombatId("combat_001"),
            randomSeed: 12345);

        var playerId = new CombatantId("player_001");
        var goblinId = new CombatantId("goblin_001");

        var player = new CombatantState(
            playerId,
            new CombatantDefinitionId("standard.player"),
            "combatant.player",
            new TeamId("player"),
            new HealthState(50, 50));

        var goblin = new CombatantState(
            goblinId,
            new CombatantDefinitionId("standard.goblin"),
            "combatant.goblin",
            new TeamId("enemy"),
            new HealthState(18, 24));

        combat.AddCombatant(player);
        combat.AddCombatant(goblin);

        var resolver = new CombatEffectResolver();

        resolver.Resolve(
            combat,
            registry,
            new ApplyStatusEffectRequest(
                goblinId,
                new StatusDefinitionId("standard.poison"),
                SourceCombatantId: playerId,
                Stacks: 3));

        resolver.Resolve(
            combat,
            registry,
            new ApplyStatusEffectRequest(
                goblinId,
                new StatusDefinitionId("standard.poison"),
                SourceCombatantId: playerId,
                Stacks: 2));

        var storedGoblin = combat.GetCombatant(goblinId);
        var poison = Assert.Single(storedGoblin.Statuses);

        Assert.Equal(5, poison.Stacks);
        Assert.Equal(2, combat.CombatLog.Count);
        Assert.Equal("StatusApplied", combat.CombatLog[0].Type);
        Assert.Equal("StatusMerged", combat.CombatLog[1].Type);
    }
}
