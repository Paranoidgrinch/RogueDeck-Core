using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class CombatQueueProcessorTests
{
    [Fact]
    public void ResolvePendingQueuesResolvesEffects()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CreateCombatWithHero();

        var heroId = new CombatantId("hero_001");

        combat.EnqueueEffect(
            new GainBlockEffectRequest(
                TargetCombatantId: heroId,
                Amount: 5));

        var processor = new CombatQueueProcessor();

        processor.ResolvePendingQueues(combat, registry);

        var hero = combat.GetCombatant(heroId);
        var block = hero.DefensivePools[StandardCombatIds.BlockDefensivePool];

        Assert.Equal(5, block.Current);
        Assert.Equal(0, combat.PendingEffectCount);
        Assert.Equal(0, combat.PendingEventCount);
    }

    [Fact]
    public void ResolvePendingQueuesResolvesEventsThatCreateEffects()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();

        builder.RegisterCombatEventHandler(new GainBlockOnTestEventHandler());
        var registry = builder.Build();

        var combat = CreateCombatWithHero();

        combat.EnqueueEvent(new TestCombatEvent());

        var processor = new CombatQueueProcessor();

        processor.ResolvePendingQueues(combat, registry);

        var hero = combat.GetCombatant(new CombatantId("hero_001"));
        var block = hero.DefensivePools[StandardCombatIds.BlockDefensivePool];

        Assert.Equal(7, block.Current);
        Assert.Equal(0, combat.PendingEffectCount);
        Assert.Equal(0, combat.PendingEventCount);
    }

    [Fact]
    public void ResolvePendingQueuesResolvesEffectBeforeEventInSameCycle()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();

        builder.RegisterCombatEventHandler(new AddLogOnTestEventHandler());
        var registry = builder.Build();

        var combat = CreateCombatWithHero();

        combat.EnqueueEffect(
            new GainBlockEffectRequest(
                TargetCombatantId: new CombatantId("hero_001"),
                Amount: 3));

        combat.EnqueueEvent(new TestCombatEvent());

        var processor = new CombatQueueProcessor();

        processor.ResolvePendingQueues(combat, registry);

        Assert.Equal(2, combat.CombatLog.Count);
        Assert.Equal("BlockGained", combat.CombatLog[0].Type);
        Assert.Equal("TestEventHandled", combat.CombatLog[1].Type);
    }

    [Fact]
    public void ResolvePendingQueuesStopsAfterMaximumCycleLimit()
    {
        var builder = new CombatDefinitionRegistryBuilder();

        builder.RegisterCombatEventHandler(new EndlessEventHandler());
        var registry = builder.Build();

        var combat = new CombatState(
            new CombatId("combat_001"),
            randomSeed: 12345);

        combat.EnqueueEvent(new EndlessTestCombatEvent());

        var processor = new CombatQueueProcessor();

        Assert.Throws<InvalidOperationException>(() =>
            processor.ResolvePendingQueues(
                combat,
                registry,
                new CombatExecutionLimits(maxQueueCycles: 3)));
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

    private sealed record EndlessTestCombatEvent : ICombatEvent;

    private sealed class GainBlockOnTestEventHandler : CombatEventHandler<TestCombatEvent>
    {
        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            TestCombatEvent combatEvent)
        {
            combat.EnqueueEffect(
                new GainBlockEffectRequest(
                    TargetCombatantId: new CombatantId("hero_001"),
                    Amount: 7));
        }
    }

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

    private sealed class EndlessEventHandler : CombatEventHandler<EndlessTestCombatEvent>
    {
        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            EndlessTestCombatEvent combatEvent)
        {
            combat.EnqueueEvent(new EndlessTestCombatEvent());
        }
    }
}
