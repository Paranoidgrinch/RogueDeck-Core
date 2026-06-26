using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class CombatResultLifecycleTests
{
    [Fact]
    public void DamageThatDownsLastEnemySetsCombatResultToVictory()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CreateCombatWithHeroAndGoblin();

        combat.EnqueueEffect(
            new DealDamageEffectRequest(
                TargetCombatantId: new CombatantId("goblin_001"),
                Amount: 99,
                SourceCombatantId: new CombatantId("hero_001")));

        var processor = new CombatQueueProcessor();

        processor.ResolvePendingQueues(combat, registry);

        Assert.Equal(CombatResult.Victory, combat.Result);
        Assert.Contains(combat.CombatLog, entry => entry.Type == "CombatantLifecycleChanged");
        Assert.Contains(combat.CombatLog, entry => entry.Type == "CombatResultChanged");
    }

    [Fact]
    public void DamageThatDownsLastPlayerSetsCombatResultToDefeat()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CreateCombatWithHeroAndGoblin();

        combat.EnqueueEffect(
            new DealDamageEffectRequest(
                TargetCombatantId: new CombatantId("hero_001"),
                Amount: 99,
                SourceCombatantId: new CombatantId("goblin_001")));

        var processor = new CombatQueueProcessor();

        processor.ResolvePendingQueues(combat, registry);

        Assert.Equal(CombatResult.Defeat, combat.Result);
        Assert.Contains(combat.CombatLog, entry => entry.Type == "CombatResultChanged");
    }

    [Fact]
    public void CombatResultRemainsOngoingWhenBothTeamsStillHaveLivingCombatants()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CreateCombatWithHeroAndTwoGoblins();

        combat.EnqueueEffect(
            new DealDamageEffectRequest(
                TargetCombatantId: new CombatantId("goblin_001"),
                Amount: 99,
                SourceCombatantId: new CombatantId("hero_001")));

        var processor = new CombatQueueProcessor();

        processor.ResolvePendingQueues(combat, registry);

        Assert.Equal(CombatResult.Ongoing, combat.Result);
        Assert.DoesNotContain(combat.CombatLog, entry => entry.Type == "CombatResultChanged");
    }

    [Fact]
    public void CombatResultIsNotChangedAgainAfterItHasEnded()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CreateCombatWithHeroAndGoblin();

        combat.SetResult(CombatResult.Victory);

        // The legitimate first transition (Ongoing → Victory) is now logged once by SetResult.
        Assert.Equal(1, combat.CombatLog.Count(entry => entry.Type == "CombatResultChanged"));

        combat.EnqueueEffect(
            new SetCombatantLifecycleStateEffectRequest(
                CombatantId: new CombatantId("hero_001"),
                LifecycleState: CombatantLifecycleState.Downed));

        var processor = new CombatQueueProcessor();

        processor.ResolvePendingQueues(combat, registry);

        Assert.Equal(CombatResult.Victory, combat.Result);
        // No *second* CombatResultChanged: combat already ended, so nothing changed it again.
        Assert.Equal(1, combat.CombatLog.Count(entry => entry.Type == "CombatResultChanged"));
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

    private static CombatState CreateCombatWithHeroAndTwoGoblins()
    {
        var combat = CreateCombatWithHeroAndGoblin();

        var secondGoblin = new CombatantState(
            new CombatantId("goblin_002"),
            new CombatantDefinitionId("standard.goblin"),
            "combatant.goblin",
            new TeamId("enemy"),
            new HealthState(current: 12, max: 12));

        combat.AddCombatant(secondGoblin);

        return combat;
    }
}
