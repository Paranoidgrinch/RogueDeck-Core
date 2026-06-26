using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class CombatQueueTerminationTests
{
    [Fact]
    public void CombatQueueProcessorDoesNotProcessEffectsWhenCombatAlreadyEnded()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CreateCombatWithHero();

        combat.SetResult(CombatResult.Victory);

        combat.EnqueueEffect(
            new DealDamageEffectRequest(
                TargetCombatantId: new CombatantId("hero_001"),
                Amount: 5));

        var processor = new CombatQueueProcessor();

        processor.ResolvePendingQueues(combat, registry);

        var hero = combat.GetCombatant(new CombatantId("hero_001"));

        Assert.Equal(20, hero.Health.Current);
        Assert.Equal(1, combat.PendingEffectCount);
    }

    [Fact]
    public void CombatQueueProcessorStopsProcessingEffectsAfterCombatResultChanges()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CreateCombatWithHero();

        combat.EnqueueEffect(new SetCombatResultEffectRequest(CombatResult.Victory));

        combat.EnqueueEffect(
            new DealDamageEffectRequest(
                TargetCombatantId: new CombatantId("hero_001"),
                Amount: 5));

        var processor = new CombatQueueProcessor();

        processor.ResolvePendingQueues(combat, registry);

        var hero = combat.GetCombatant(new CombatantId("hero_001"));

        Assert.Equal(CombatResult.Victory, combat.Result);
        Assert.Equal(20, hero.Health.Current);
        Assert.Equal(1, combat.PendingEffectCount);
    }

    [Fact]
    public void CombatQueueProcessorDoesNotProcessEventsAfterCombatResultChanges()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();

        builder.RegisterCombatEventHandler(new AddLogOnTestEventHandler());
        var registry = builder.Build();

        var combat = CreateCombatWithHero();

        combat.EnqueueEffect(new SetCombatResultEffectRequest(CombatResult.Victory));
        combat.EnqueueEvent(new TestCombatEvent());

        var processor = new CombatQueueProcessor();

        processor.ResolvePendingQueues(combat, registry);

        Assert.Equal(CombatResult.Victory, combat.Result);
        Assert.Equal(1, combat.PendingEventCount);
        Assert.DoesNotContain(combat.CombatLog, entry => entry.Type == "TestEventHandled");
    }

    private static CombatState CreateCombatWithHero()
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

        combat.AddCombatant(hero);

        return combat;
    }

    private sealed record TestCombatEvent : ICombatEvent;

    private sealed class AddLogOnTestEventHandler : CombatEventHandler<TestCombatEvent>
    {
        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            TestCombatEvent combatEvent)
        {
            combat.AddLogEntry(
                "TestEventHandled",
                "Handled test combat event.");
        }
    }
}
