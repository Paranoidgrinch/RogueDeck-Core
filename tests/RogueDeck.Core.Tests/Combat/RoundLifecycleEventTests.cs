using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class RoundLifecycleEventTests
{
    [Fact]
    public void EndingLastCombatantsTurnEmitsRoundEndedAndRoundStartedEvents()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();

        builder.RegisterCombatEventHandler(new AddLogOnRoundEndedHandler());
        builder.RegisterCombatEventHandler(new AddLogOnRoundStartedHandler());
        var registry = builder.Build();

        var combat = CreateCombatWithHeroAndGoblin();

        combat.SetActiveCombatant(new CombatantId("goblin_001"));

        var processor = new CombatTurnProcessor();

        processor.StartCurrentTurn(combat, registry);
        processor.EndCurrentTurn(combat, registry);

        Assert.Equal(2, combat.CurrentRound);
        Assert.Equal(1, combat.CurrentTurn);
        Assert.Equal(new CombatantId("hero_001"), combat.ActiveCombatantId);
        Assert.Equal(0, combat.PendingEventCount);
        Assert.Equal(0, combat.PendingEffectCount);
        Assert.Contains(combat.CombatLog, entry => entry.Type == "RoundEndedEventHandled");
        Assert.Contains(combat.CombatLog, entry => entry.Type == "RoundStartedEventHandled");
    }

    [Fact]
    public void EndingNonLastCombatantsTurnDoesNotEmitRoundEvents()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();

        builder.RegisterCombatEventHandler(new AddLogOnRoundEndedHandler());
        builder.RegisterCombatEventHandler(new AddLogOnRoundStartedHandler());
        var registry = builder.Build();

        var combat = CreateCombatWithHeroAndGoblin();

        var processor = new CombatTurnProcessor();

        processor.StartCurrentTurn(combat, registry);
        processor.EndCurrentTurn(combat, registry);

        Assert.Equal(1, combat.CurrentRound);
        Assert.Equal(2, combat.CurrentTurn);
        Assert.Equal(new CombatantId("goblin_001"), combat.ActiveCombatantId);
        Assert.DoesNotContain(combat.CombatLog, entry => entry.Type == "RoundEndedEventHandled");
        Assert.DoesNotContain(combat.CombatLog, entry => entry.Type == "RoundStartedEventHandled");
    }

    [Fact]
    public void RoundEventsAreHandledInOrderAfterNextActiveCombatantIsSelected()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var snapshots = new List<RoundEventSnapshot>();

        builder.RegisterCombatEventHandler(new CaptureRoundEndedSnapshotHandler(snapshots));
        builder.RegisterCombatEventHandler(new CaptureRoundStartedSnapshotHandler(snapshots));
        var registry = builder.Build();

        var combat = CreateCombatWithHeroAndGoblin();
        var heroId = new CombatantId("hero_001");
        var goblinId = new CombatantId("goblin_001");

        combat.SetActiveCombatant(goblinId);

        var processor = new CombatTurnProcessor();

        processor.StartCurrentTurn(combat, registry);
        processor.EndCurrentTurn(combat, registry);

        Assert.Equal(2, snapshots.Count);

        Assert.Equal("RoundEnded", snapshots[0].EventType);
        Assert.Equal(1, snapshots[0].EventRound);
        Assert.Equal(2, snapshots[0].CurrentRound);
        Assert.Equal(1, snapshots[0].CurrentTurn);
        Assert.Equal(heroId, snapshots[0].ActiveCombatantId);
        Assert.Equal(CombatTurnPhase.WaitingToStartTurn, snapshots[0].TurnPhase);

        Assert.Equal("RoundStarted", snapshots[1].EventType);
        Assert.Equal(2, snapshots[1].EventRound);
        Assert.Equal(2, snapshots[1].CurrentRound);
        Assert.Equal(1, snapshots[1].CurrentTurn);
        Assert.Equal(heroId, snapshots[1].ActiveCombatantId);
        Assert.Equal(CombatTurnPhase.WaitingToStartTurn, snapshots[1].TurnPhase);
    }

    [Fact]
    public void RoundStartedHandlersCanQueueEffectsBeforeNextTurnStarts()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();

        var heroId = new CombatantId("hero_001");
        var goblinId = new CombatantId("goblin_001");

        builder.RegisterCombatEventHandler(
            new GainBlockOnRoundStartedHandler(
                heroId,
                amount: 4));
        var registry = builder.Build();

        var combat = CreateCombatWithHeroAndGoblin();
        combat.SetActiveCombatant(goblinId);

        var processor = new CombatTurnProcessor();

        processor.StartCurrentTurn(combat, registry);
        processor.EndCurrentTurn(combat, registry);

        Assert.Equal(2, combat.CurrentRound);
        Assert.Equal(1, combat.CurrentTurn);
        Assert.Equal(heroId, combat.ActiveCombatantId);
        Assert.Equal(CombatTurnPhase.WaitingToStartTurn, combat.TurnPhase);

        Assert.Equal(
            4,
            GetBlockOrZero(combat, heroId));
    }

    private sealed record RoundEventSnapshot(
        string EventType,
        int EventRound,
        int CurrentRound,
        int CurrentTurn,
        CombatantId? ActiveCombatantId,
        CombatTurnPhase TurnPhase);

    private sealed class CaptureRoundEndedSnapshotHandler
        : CombatEventHandler<RoundEndedCombatEvent>
    {
        private readonly List<RoundEventSnapshot> _snapshots;

        public CaptureRoundEndedSnapshotHandler(List<RoundEventSnapshot> snapshots)
        {
            _snapshots = snapshots;
        }

        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            RoundEndedCombatEvent combatEvent)
        {
            _snapshots.Add(new RoundEventSnapshot(
                EventType: "RoundEnded",
                EventRound: combatEvent.Round,
                CurrentRound: combat.CurrentRound,
                CurrentTurn: combat.CurrentTurn,
                ActiveCombatantId: combat.ActiveCombatantId,
                TurnPhase: combat.TurnPhase));
        }
    }

    private sealed class CaptureRoundStartedSnapshotHandler
        : CombatEventHandler<RoundStartedCombatEvent>
    {
        private readonly List<RoundEventSnapshot> _snapshots;

        public CaptureRoundStartedSnapshotHandler(List<RoundEventSnapshot> snapshots)
        {
            _snapshots = snapshots;
        }

        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            RoundStartedCombatEvent combatEvent)
        {
            _snapshots.Add(new RoundEventSnapshot(
                EventType: "RoundStarted",
                EventRound: combatEvent.Round,
                CurrentRound: combat.CurrentRound,
                CurrentTurn: combat.CurrentTurn,
                ActiveCombatantId: combat.ActiveCombatantId,
                TurnPhase: combat.TurnPhase));
        }
    }

    private sealed class GainBlockOnRoundStartedHandler
        : CombatEventHandler<RoundStartedCombatEvent>
    {
        private readonly CombatantId _targetCombatantId;
        private readonly int _amount;

        public GainBlockOnRoundStartedHandler(
            CombatantId targetCombatantId,
            int amount)
        {
            _targetCombatantId = targetCombatantId;
            _amount = amount;
        }

        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            RoundStartedCombatEvent combatEvent)
        {
            combat.EnqueueEffect(new GainBlockEffectRequest(
                TargetCombatantId: _targetCombatantId,
                Amount: _amount,
                SourceCombatantId: null,
                SourceCardId: null));
        }
    }

    private static int GetBlockOrZero(
        CombatState combat,
        CombatantId combatantId)
    {
        var combatant = combat.GetCombatant(combatantId);

        return combatant.DefensivePools.TryGetValue(
            StandardCombatIds.BlockDefensivePool,
            out var blockPool)
                ? blockPool.Current
                : 0;
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
            StandardCombatIds.PlayerTeam,
            new HealthState(current: 20, max: 20));

        var goblin = new CombatantState(
            new CombatantId("goblin_001"),
            new CombatantDefinitionId("standard.goblin"),
            "combatant.goblin",
            StandardCombatIds.EnemyTeam,
            new HealthState(current: 12, max: 12));

        combat.AddCombatant(hero);
        combat.AddCombatant(goblin);

        return combat;
    }

    private sealed class AddLogOnRoundEndedHandler : CombatEventHandler<RoundEndedCombatEvent>
    {
        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            RoundEndedCombatEvent combatEvent)
        {
            combat.AddLogEntry(
                "RoundEndedEventHandled",
                $"Handled round ended event for round {combatEvent.Round}.");
        }
    }

    private sealed class AddLogOnRoundStartedHandler : CombatEventHandler<RoundStartedCombatEvent>
    {
        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            RoundStartedCombatEvent combatEvent)
        {
            combat.AddLogEntry(
                "RoundStartedEventHandled",
                $"Handled round started event for round {combatEvent.Round}.");
        }
    }
}
