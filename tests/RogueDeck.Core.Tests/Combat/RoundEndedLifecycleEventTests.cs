using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class RoundEndedLifecycleEventTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    [Fact]
    public void RoundEndedEventCarriesLastActiveCombatantFromEndedRound()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var snapshots = new List<RoundEndedSnapshot>();

        builder.RegisterCombatEventHandler(
            new CaptureRoundEndedSnapshotHandler(snapshots));
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.SetActiveCombatant(GoblinId);

        var processor = new CombatTurnProcessor();

        processor.StartCurrentTurn(combat, registry);
        processor.EndCurrentTurn(combat, registry);

        var snapshot = Assert.Single(snapshots);

        Assert.Equal(1, snapshot.EventRound);
        Assert.Equal(GoblinId, snapshot.LastActiveCombatantId);

        Assert.Equal(2, snapshot.CurrentRoundObservedByHandler);
        Assert.Equal(HeroId, snapshot.ActiveCombatantIdObservedByHandler);
    }

    private sealed record RoundEndedSnapshot(
        int EventRound,
        CombatantId? LastActiveCombatantId,
        int CurrentRoundObservedByHandler,
        CombatantId? ActiveCombatantIdObservedByHandler);

    private sealed class CaptureRoundEndedSnapshotHandler
        : CombatEventHandler<RoundEndedCombatEvent>
    {
        private readonly List<RoundEndedSnapshot> _snapshots;

        public CaptureRoundEndedSnapshotHandler(List<RoundEndedSnapshot> snapshots)
        {
            _snapshots = snapshots;
        }

        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            RoundEndedCombatEvent combatEvent)
        {
            _snapshots.Add(new RoundEndedSnapshot(
                EventRound: combatEvent.Round,
                LastActiveCombatantId: combatEvent.LastActiveCombatantId,
                CurrentRoundObservedByHandler: combat.CurrentRound,
                ActiveCombatantIdObservedByHandler: combat.ActiveCombatantId));
        }
    }
}
