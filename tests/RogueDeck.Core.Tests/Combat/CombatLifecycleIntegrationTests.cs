using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class CombatLifecycleIntegrationTests
{
    [Fact]
    public void PoisonAtTurnStartCanDownEnemyAndSetVictory()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var goblinId = new CombatantId("goblin_001");

        var resolver = new CombatEffectResolver();

        resolver.Resolve(
            combat,
            registry,
            new ApplyStatusEffectRequest(
                TargetCombatantId: goblinId,
                StatusDefinitionId: StandardCombatIds.PoisonStatus,
                Stacks: 12));

        combat.DequeueNextEvent();

        combat.SetActiveCombatant(goblinId);

        var turnProcessor = new CombatTurnProcessor();

        turnProcessor.StartCurrentTurn(combat, registry);

        var goblin = combat.GetCombatant(goblinId);

        Assert.Equal(0, goblin.Health.Current);
        Assert.Equal(CombatantLifecycleState.Downed, goblin.LifecycleState);
        Assert.Equal(CombatResult.Victory, combat.Result);
        Assert.Equal(0, combat.PendingEffectCount);
        Assert.Equal(0, combat.PendingEventCount);

        Assert.Contains(combat.CombatLog, entry => entry.Type == StandardCombatLogTypes.TurnStarted);
        Assert.Contains(combat.CombatLog, entry => entry.Type == StandardCombatLogTypes.DamageDealt);
        Assert.Contains(combat.CombatLog, entry => entry.Type == StandardCombatLogTypes.CombatantLifecycleChanged);
        Assert.Contains(combat.CombatLog, entry => entry.Type == StandardCombatLogTypes.CombatResultChanged);
    }

    [Fact]
    public void DirectDamageCanTriggerThornsThenStillSetVictory()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var heroId = new CombatantId("hero_001");
        var goblinId = new CombatantId("goblin_001");

        var resolver = new CombatEffectResolver();

        resolver.Resolve(
            combat,
            registry,
            new ApplyStatusEffectRequest(
                TargetCombatantId: goblinId,
                StatusDefinitionId: StandardCombatIds.ThornsStatus,
                Stacks: 3));

        combat.DequeueNextEvent();

        combat.EnqueueEffect(
            new DealDamageEffectRequest(
                TargetCombatantId: goblinId,
                Amount: 12,
                SourceCombatantId: heroId));

        var queueProcessor = new CombatQueueProcessor();

        queueProcessor.ResolvePendingQueues(combat, registry);

        var hero = combat.GetCombatant(heroId);
        var goblin = combat.GetCombatant(goblinId);

        Assert.Equal(17, hero.Health.Current);
        Assert.Equal(0, goblin.Health.Current);
        Assert.Equal(CombatantLifecycleState.Downed, goblin.LifecycleState);
        Assert.Equal(CombatResult.Victory, combat.Result);
        Assert.Equal(0, combat.PendingEffectCount);
        Assert.Equal(0, combat.PendingEventCount);

        Assert.Equal(2, combat.CombatLog.Count(entry => entry.Type == StandardCombatLogTypes.DamageDealt));
        Assert.Contains(combat.CombatLog, entry => entry.Type == StandardCombatLogTypes.CombatResultChanged);
    }

    [Fact]
    public void TimedStatusExpiresAtTurnEndThroughLifecycleQueues()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var heroId = new CombatantId("hero_001");

        var resolver = new CombatEffectResolver();

        resolver.Resolve(
            combat,
            registry,
            new ApplyStatusEffectRequest(
                TargetCombatantId: heroId,
                StatusDefinitionId: StandardCombatIds.WeakStatus,
                DurationTurns: 1));

        combat.DequeueNextEvent();

        var turnProcessor = new CombatTurnProcessor();

        turnProcessor.StartCurrentTurn(combat, registry);
        turnProcessor.EndCurrentTurn(combat, registry);

        var hero = combat.GetCombatant(heroId);

        Assert.Empty(hero.Statuses);
        Assert.Equal(CombatResult.Ongoing, combat.Result);
        Assert.Equal(0, combat.PendingEffectCount);
        Assert.Equal(0, combat.PendingEventCount);

        Assert.Contains(combat.CombatLog, entry => entry.Type == StandardCombatLogTypes.StatusDurationReduced);
        Assert.Contains(combat.CombatLog, entry => entry.Type == StandardCombatLogTypes.StatusExpired);
    }
}

