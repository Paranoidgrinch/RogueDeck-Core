using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class CombatTurnProcessorTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    [Fact]
    public void StartCurrentTurnAddsTurnStartedLogEntry()
    {
        var builder = CreateEmptyBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var processor = new CombatTurnProcessor();

        processor.StartCurrentTurn(combat, builder.Build());

        Assert.Single(combat.CombatLog);
        Assert.Equal(StandardCombatLogTypes.TurnStarted, combat.CombatLog[0].Type);
        Assert.Equal(HeroId, combat.ActiveCombatantId);
        Assert.Equal(CombatTurnPhase.TurnInProgress, combat.TurnPhase);
    }

    [Fact]
    public void StartCurrentTurnCannotBeCalledTwice()
    {
        var builder = CreateEmptyBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var processor = new CombatTurnProcessor();

        processor.StartCurrentTurn(combat, builder.Build());

        Assert.Throws<InvalidOperationException>(() =>
            processor.StartCurrentTurn(combat, builder.Build()));
    }

    [Fact]
    public void EndCurrentTurnRequiresStartedTurn()
    {
        var builder = CreateEmptyBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var processor = new CombatTurnProcessor();

        Assert.Throws<InvalidOperationException>(() =>
            processor.EndCurrentTurn(combat, builder.Build()));
    }

    [Fact]
    public void EndCurrentTurnMovesToNextCombatant()
    {
        var builder = CreateEmptyBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var processor = new CombatTurnProcessor();

        processor.StartCurrentTurn(combat, builder.Build());
        processor.EndCurrentTurn(combat, builder.Build());

        Assert.Equal(GoblinId, combat.ActiveCombatantId);
        Assert.Equal(1, combat.CurrentRound);
        Assert.Equal(2, combat.CurrentTurn);
        Assert.Equal(CombatTurnPhase.WaitingToStartTurn, combat.TurnPhase);

        Assert.Equal(
            new[] { StandardCombatLogTypes.TurnStarted, StandardCombatLogTypes.TurnEnded },
            combat.CombatLog.Select(entry => entry.Type).ToArray());
    }

    [Fact]
    public void EndCurrentTurnAdvancesRoundAfterLastCombatant()
    {
        var builder = CreateEmptyBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.SetActiveCombatant(GoblinId);

        var processor = new CombatTurnProcessor();

        processor.StartCurrentTurn(combat, builder.Build());
        processor.EndCurrentTurn(combat, builder.Build());

        Assert.Equal(HeroId, combat.ActiveCombatantId);
        Assert.Equal(2, combat.CurrentRound);
        Assert.Equal(1, combat.CurrentTurn);
        Assert.Equal(CombatTurnPhase.WaitingToStartTurn, combat.TurnPhase);
    }

    [Fact]
    public void StartCurrentTurnProcessesTurnStartedEventHandlersAndFollowUpEffects()
    {
        var builder = CreateEmptyBuilder();
        builder.RegisterEffectRequestHandler(new GainBlockEffectHandler());
        builder.RegisterCombatEventHandler(new GainBlockOnTurnStartedHandler(amount: 4));

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var processor = new CombatTurnProcessor();

        processor.StartCurrentTurn(combat, builder.Build());

        var hero = combat.GetCombatant(HeroId);
        var block = hero.DefensivePools[StandardCombatIds.BlockDefensivePool];

        Assert.Equal(4, block.Current);
        Assert.Equal(0, combat.PendingEventCount);
        Assert.Equal(0, combat.PendingEffectCount);

        Assert.Equal(StandardCombatLogTypes.TurnStarted, combat.CombatLog[0].Type);
        Assert.Equal(StandardCombatLogTypes.BlockGained, combat.CombatLog[1].Type);
    }

    [Fact]
    public void EndCurrentTurnProcessesTurnEndedEventHandlers()
    {
        var builder = CreateEmptyBuilder();
        builder.RegisterEffectRequestHandler(new GainBlockEffectHandler());
        builder.RegisterCombatEventHandler(new GainBlockOnTurnEndedHandler(amount: 3));

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var processor = new CombatTurnProcessor();

        processor.StartCurrentTurn(combat, builder.Build());
        processor.EndCurrentTurn(combat, builder.Build());

        var hero = combat.GetCombatant(HeroId);
        var block = hero.DefensivePools[StandardCombatIds.BlockDefensivePool];

        Assert.Equal(3, block.Current);
        Assert.Equal(0, combat.PendingEventCount);
        Assert.Equal(0, combat.PendingEffectCount);

        Assert.Equal(StandardCombatLogTypes.TurnStarted, combat.CombatLog[0].Type);
        Assert.Equal(StandardCombatLogTypes.TurnEnded, combat.CombatLog[1].Type);
        Assert.Equal(StandardCombatLogTypes.BlockGained, combat.CombatLog[2].Type);
    }

    [Fact]
    public void EndCurrentTurnResolvesPendingEffectsBeforeAdvancing()
    {
        var builder = CreateEmptyBuilder();
        builder.RegisterEffectRequestHandler(new GainBlockEffectHandler());
        builder.RegisterEffectRequestHandler(new DealDamageEffectHandler());

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var processor = new CombatTurnProcessor();

        processor.StartCurrentTurn(combat, builder.Build());

        combat.EnqueueEffect(new GainBlockEffectRequest(
            TargetCombatantId: HeroId,
            Amount: 6));

        combat.EnqueueEffect(new DealDamageEffectRequest(
            TargetCombatantId: HeroId,
            Amount: 10));

        processor.EndCurrentTurn(combat, builder.Build());

        var hero = combat.GetCombatant(HeroId);
        var block = hero.DefensivePools[StandardCombatIds.BlockDefensivePool];

        Assert.Equal(16, hero.Health.Current);
        Assert.Equal(0, block.Current);
        Assert.Equal(0, combat.PendingEffectCount);
        Assert.Equal(GoblinId, combat.ActiveCombatantId);

        Assert.Equal(StandardCombatLogTypes.TurnStarted, combat.CombatLog[0].Type);
        Assert.Equal(StandardCombatLogTypes.TurnEnded, combat.CombatLog[1].Type);
        Assert.Equal(StandardCombatLogTypes.BlockGained, combat.CombatLog[2].Type);
        Assert.Equal(StandardCombatLogTypes.DamageDealt, combat.CombatLog[3].Type);
    }

    [Fact]
    public void EndCurrentTurnAndStartNextTurnEndsCurrentTurnThenStartsNextTurn()
    {
        var builder = CreateEmptyBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var processor = new CombatTurnProcessor();

        processor.StartCurrentTurn(combat, builder.Build());
        processor.EndCurrentTurnAndStartNextTurn(combat, builder.Build());

        Assert.Equal(GoblinId, combat.ActiveCombatantId);
        Assert.Equal(1, combat.CurrentRound);
        Assert.Equal(2, combat.CurrentTurn);
        Assert.Equal(CombatTurnPhase.TurnInProgress, combat.TurnPhase);

        Assert.Equal(
            new[]
            {
                StandardCombatLogTypes.TurnStarted,
                StandardCombatLogTypes.TurnEnded,
                StandardCombatLogTypes.TurnStarted
            },
            combat.CombatLog.Select(entry => entry.Type).ToArray());
    }

    [Fact]
    public void StartCurrentTurnRejectsCombatWithoutTurnOrder()
    {
        var builder = CreateEmptyBuilder();
        var combat = new CombatState(
            new CombatId("combat_001"),
            randomSeed: 12345);

        var processor = new CombatTurnProcessor();

        Assert.Throws<InvalidOperationException>(() =>
            processor.StartCurrentTurn(combat, builder.Build()));
    }

    [Fact]
    public void StartCurrentTurnPublishesTurnStartedEventToHandlers()
    {
        var builder = CreateEmptyBuilder();
        var eventHandler = new CaptureTurnStartedEventHandler();
        builder.RegisterCombatEventHandler(eventHandler);

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var processor = new CombatTurnProcessor();

        processor.StartCurrentTurn(combat, builder.Build());

        var handledEvent = Assert.Single(eventHandler.HandledEvents);

        Assert.Equal(HeroId, handledEvent.CombatantId);
        Assert.Equal(1, handledEvent.Round);
        Assert.Equal(1, handledEvent.Turn);
        Assert.Equal(0, combat.PendingEventCount);
    }

    [Fact]
    public void EndCurrentTurnPublishesTurnEndedEventToHandlers()
    {
        var builder = CreateEmptyBuilder();
        var eventHandler = new CaptureTurnEndedEventHandler();
        builder.RegisterCombatEventHandler(eventHandler);

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var processor = new CombatTurnProcessor();

        processor.StartCurrentTurn(combat, builder.Build());
        processor.EndCurrentTurn(combat, builder.Build());

        var handledEvent = Assert.Single(eventHandler.HandledEvents);

        Assert.Equal(HeroId, handledEvent.CombatantId);
        Assert.Equal(1, handledEvent.Round);
        Assert.Equal(1, handledEvent.Turn);
        Assert.Equal(0, combat.PendingEventCount);
    }

    [Fact]
    public void EndCurrentTurnAndStartNextTurnDoesNotStartNextTurnWhenCombatEndsAtTurnEnd()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterCombatEventHandler(new DealLethalDamageToGoblinOnTurnEndedHandler());
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var processor = new CombatTurnProcessor();

        processor.StartCurrentTurn(combat, builder.Build());
        processor.EndCurrentTurnAndStartNextTurn(combat, builder.Build());

        Assert.Equal(CombatResult.Victory, combat.Result);
        Assert.Equal(CombatTurnPhase.WaitingToStartTurn, combat.TurnPhase);
    }

    private static CombatDefinitionRegistryBuilder CreateEmptyBuilder()
    {
        return new CombatDefinitionRegistryBuilder();
    }

    private sealed class GainBlockOnTurnStartedHandler
        : CombatEventHandler<TurnStartedCombatEvent>
    {
        private readonly int _amount;

        public GainBlockOnTurnStartedHandler(int amount)
        {
            _amount = amount;
        }

        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            TurnStartedCombatEvent combatEvent)
        {
            combat.EnqueueEffect(new GainBlockEffectRequest(
                TargetCombatantId: combatEvent.CombatantId,
                Amount: _amount));
        }
    }

    private sealed class GainBlockOnTurnEndedHandler
        : CombatEventHandler<TurnEndedCombatEvent>
    {
        private readonly int _amount;

        public GainBlockOnTurnEndedHandler(int amount)
        {
            _amount = amount;
        }

        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            TurnEndedCombatEvent combatEvent)
        {
            combat.EnqueueEffect(new GainBlockEffectRequest(
                TargetCombatantId: combatEvent.CombatantId,
                Amount: _amount));
        }
    }

    private sealed class CaptureTurnStartedEventHandler
        : CombatEventHandler<TurnStartedCombatEvent>
    {
        public List<TurnStartedCombatEvent> HandledEvents { get; } = new();

        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            TurnStartedCombatEvent combatEvent)
        {
            HandledEvents.Add(combatEvent);
        }
    }

    private sealed class CaptureTurnEndedEventHandler
        : CombatEventHandler<TurnEndedCombatEvent>
    {
        public List<TurnEndedCombatEvent> HandledEvents { get; } = new();

        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            TurnEndedCombatEvent combatEvent)
        {
            HandledEvents.Add(combatEvent);
        }
    }

    private sealed class DealLethalDamageToGoblinOnTurnEndedHandler
        : CombatEventHandler<TurnEndedCombatEvent>
    {
        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            TurnEndedCombatEvent combatEvent)
        {
            combat.EnqueueEffect(new DealDamageEffectRequest(
                TargetCombatantId: GoblinId,
                Amount: 999));
        }
    }
}
