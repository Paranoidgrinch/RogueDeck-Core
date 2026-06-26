using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class CombatEventHandlingTests
{
    [Fact]
    public void RegistryCanStoreMultipleCombatEventHandlersForSameEventType()
    {
        var builder = new CombatDefinitionRegistryBuilder();

        var first = new FirstTestCombatEventHandler();
        var second = new SecondTestCombatEventHandler();

        builder.RegisterCombatEventHandler(first);
        builder.RegisterCombatEventHandler(second);
        var registry = builder.Build();

        var handlers = registry.GetCombatEventHandlers(typeof(TestCombatEvent)).ToArray();

        Assert.Equal(2, handlers.Length);
        Assert.Contains(first, handlers);
        Assert.Contains(second, handlers);
    }

    [Fact]
    public void RegistryReturnsEmptyCollectionWhenNoCombatEventHandlersAreRegistered()
    {
        var registry = new CombatDefinitionRegistryBuilder().Build();

        var handlers = registry.GetCombatEventHandlers(typeof(TestCombatEvent));

        Assert.Empty(handlers);
    }

    [Fact]
    public void RegistryRejectsDuplicateCombatEventHandlerTypeForSameEventType()
    {
        var builder = new CombatDefinitionRegistryBuilder();

        builder.RegisterCombatEventHandler(new FirstTestCombatEventHandler());

        Assert.Throws<InvalidOperationException>(() =>
            builder.RegisterCombatEventHandler(new FirstTestCombatEventHandler()));
        var registry = builder.Build();
    }

    [Fact]
    public void CombatEventHandlerRejectsWrongEventType()
    {
        var handler = new FirstTestCombatEventHandler();

        var combat = new CombatState(
            new CombatId("combat_001"),
            randomSeed: 12345);

        var registry = new CombatDefinitionRegistryBuilder().Build();

        Assert.Throws<ArgumentException>(() =>
            handler.Handle(combat, registry, new OtherTestCombatEvent()));
    }

    [Fact]
    public void CombatEventQueueProcessorRunsRegisteredEventHandlers()
    {
        var builder = new CombatDefinitionRegistryBuilder();

        builder.RegisterCombatEventHandler(new AddLogOnTestEventHandler());
        var registry = builder.Build();

        var combat = new CombatState(
            new CombatId("combat_001"),
            randomSeed: 12345);

        combat.EnqueueEvent(new TestCombatEvent());

        var processor = new CombatEventQueueProcessor();

        processor.ResolvePendingEvents(combat, registry);

        Assert.Equal(0, combat.PendingEventCount);
        Assert.Single(combat.CombatLog);
        Assert.Equal("TestEventHandled", combat.CombatLog[0].Type);
    }

    [Fact]
    public void CombatEventQueueProcessorAllowsEventHandlersToEnqueueEffects()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();

        builder.RegisterCombatEventHandler(new GainBlockOnTestEventHandler());
        var registry = builder.Build();

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
        combat.EnqueueEvent(new TestCombatEvent());

        var eventProcessor = new CombatEventQueueProcessor();
        var effectProcessor = new CombatEffectQueueProcessor();

        eventProcessor.ResolvePendingEvents(combat, registry);
        effectProcessor.ResolvePendingEffects(combat, registry);

        var storedHero = combat.GetCombatant(heroId);
        var block = storedHero.DefensivePools[StandardCombatIds.BlockDefensivePool];

        Assert.Equal(7, block.Current);
    }

    [Fact]
    public void CombatEventQueueProcessorStopsAfterMaximumEventLimit()
    {
        var builder = new CombatDefinitionRegistryBuilder();

        builder.RegisterCombatEventHandler(new EndlessEventHandler());
        var registry = builder.Build();

        var combat = new CombatState(
            new CombatId("combat_001"),
            randomSeed: 12345);

        combat.EnqueueEvent(new EndlessTestCombatEvent());

        var processor = new CombatEventQueueProcessor();

        Assert.Throws<InvalidOperationException>(() =>
            processor.ResolvePendingEvents(
                combat,
                registry,
                new CombatExecutionLimits(maxEventsPerCycle: 3)));
    }

    private sealed record TestCombatEvent : ICombatEvent;

    private sealed record OtherTestCombatEvent : ICombatEvent;

    private sealed record EndlessTestCombatEvent : ICombatEvent;

    private sealed class FirstTestCombatEventHandler : CombatEventHandler<TestCombatEvent>
    {
        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            TestCombatEvent combatEvent)
        {
        }
    }

    private sealed class SecondTestCombatEventHandler : CombatEventHandler<TestCombatEvent>
    {
        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            TestCombatEvent combatEvent)
        {
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
