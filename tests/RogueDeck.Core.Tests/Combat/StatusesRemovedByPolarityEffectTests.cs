using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class StatusesRemovedByPolarityEffectTests
{
    private static readonly CombatantId HeroId = new("hero_001");

    [Fact]
    public void RemoveStatusesByPolarityRemovesOnlyMatchingPolarityAndEmitsSingleAggregateEvent()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var polarityEvents = new List<StatusesRemovedByPolarityEventSnapshot>();
        var statusRemovedEvents = new List<StatusRemovedEventSnapshot>();

        builder.RegisterCombatEventHandler(
            new CaptureStatusesRemovedByPolarityHandler(polarityEvents));

        builder.RegisterCombatEventHandler(
            new CaptureStatusRemovedHandler(statusRemovedEvents));
        var registry = builder.Build();

        ApplyStatus(
            combat,
            registry,
            HeroId,
            StandardCombatIds.WeakStatus,
            durationTurns: 2);

        ApplyStatus(
            combat,
            registry,
            HeroId,
            StandardCombatIds.VulnerableStatus,
            durationTurns: 2);

        ApplyStatus(
            combat,
            registry,
            HeroId,
            StandardCombatIds.StrengthStatus,
            stacks: 3);

        RemoveStatusesByPolarity(
            combat,
            registry,
            HeroId,
            StatusPolarity.Debuff);

        var hero = combat.GetCombatant(HeroId);

        Assert.DoesNotContain(
            hero.Statuses,
            status => status.DefinitionId == StandardCombatIds.WeakStatus);

        Assert.DoesNotContain(
            hero.Statuses,
            status => status.DefinitionId == StandardCombatIds.VulnerableStatus);

        Assert.Contains(
            hero.Statuses,
            status => status.DefinitionId == StandardCombatIds.StrengthStatus);

        var polarityEvent = Assert.Single(polarityEvents);

        Assert.Equal(HeroId, polarityEvent.TargetCombatantId);
        Assert.Equal(StatusPolarity.Debuff, polarityEvent.Polarity);
        Assert.Equal(2, polarityEvent.StatusInstanceIds.Count);

        Assert.Empty(statusRemovedEvents);
    }

    [Fact]
    public void RemoveStatusesByPolarityDoesNotEmitEventWhenNoMatchingStatusesAreRemoved()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var polarityEvents = new List<StatusesRemovedByPolarityEventSnapshot>();

        builder.RegisterCombatEventHandler(
            new CaptureStatusesRemovedByPolarityHandler(polarityEvents));
        var registry = builder.Build();

        ApplyStatus(
            combat,
            registry,
            HeroId,
            StandardCombatIds.StrengthStatus,
            stacks: 3);

        RemoveStatusesByPolarity(
            combat,
            registry,
            HeroId,
            StatusPolarity.Debuff);

        var hero = combat.GetCombatant(HeroId);

        Assert.Contains(
            hero.Statuses,
            status => status.DefinitionId == StandardCombatIds.StrengthStatus);

        Assert.Empty(polarityEvents);
    }

    [Fact]
    public void RemoveStatusesByPolarityCanRemoveBuffsAndLeaveDebuffs()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var polarityEvents = new List<StatusesRemovedByPolarityEventSnapshot>();

        builder.RegisterCombatEventHandler(
            new CaptureStatusesRemovedByPolarityHandler(polarityEvents));
        var registry = builder.Build();

        ApplyStatus(
            combat,
            registry,
            HeroId,
            StandardCombatIds.WeakStatus,
            durationTurns: 2);

        ApplyStatus(
            combat,
            registry,
            HeroId,
            StandardCombatIds.StrengthStatus,
            stacks: 3);

        RemoveStatusesByPolarity(
            combat,
            registry,
            HeroId,
            StatusPolarity.Buff);

        var hero = combat.GetCombatant(HeroId);

        Assert.Contains(
            hero.Statuses,
            status => status.DefinitionId == StandardCombatIds.WeakStatus);

        Assert.DoesNotContain(
            hero.Statuses,
            status => status.DefinitionId == StandardCombatIds.StrengthStatus);

        var polarityEvent = Assert.Single(polarityEvents);

        Assert.Equal(HeroId, polarityEvent.TargetCombatantId);
        Assert.Equal(StatusPolarity.Buff, polarityEvent.Polarity);
        Assert.Single(polarityEvent.StatusInstanceIds);
    }

    private static void ApplyStatus(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CombatantId targetId,
        StatusDefinitionId statusId,
        int stacks = 0,
        int durationTurns = 0,
        int charges = 0)
    {
        combat.EnqueueEffect(new ApplyStatusEffectRequest(
            TargetCombatantId: targetId,
            StatusDefinitionId: statusId,
            Stacks: stacks,
            DurationTurns: durationTurns,
            Charges: charges));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    private static void RemoveStatusesByPolarity(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CombatantId targetId,
        StatusPolarity polarity)
    {
        combat.EnqueueEffect(new RemoveStatusesByPolarityEffectRequest(
            TargetCombatantId: targetId,
            Polarity: polarity));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    private sealed record StatusesRemovedByPolarityEventSnapshot(
        CombatantId TargetCombatantId,
        IReadOnlyCollection<StatusInstanceId> StatusInstanceIds,
        StatusPolarity Polarity);

    private sealed record StatusRemovedEventSnapshot(
        CombatantId TargetCombatantId,
        IReadOnlyCollection<StatusInstanceId> StatusInstanceIds,
        StatusDefinitionId StatusDefinitionId);

    private sealed class CaptureStatusesRemovedByPolarityHandler
        : CombatEventHandler<StatusesRemovedByPolarityCombatEvent>
    {
        private readonly List<StatusesRemovedByPolarityEventSnapshot> _events;

        public CaptureStatusesRemovedByPolarityHandler(
            List<StatusesRemovedByPolarityEventSnapshot> events)
        {
            _events = events;
        }

        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            StatusesRemovedByPolarityCombatEvent combatEvent)
        {
            _events.Add(new StatusesRemovedByPolarityEventSnapshot(
                TargetCombatantId: combatEvent.TargetCombatantId,
                StatusInstanceIds: combatEvent.StatusInstanceIds.ToArray(),
                Polarity: combatEvent.Polarity));
        }
    }

    private sealed class CaptureStatusRemovedHandler
        : CombatEventHandler<StatusRemovedCombatEvent>
    {
        private readonly List<StatusRemovedEventSnapshot> _events;

        public CaptureStatusRemovedHandler(
            List<StatusRemovedEventSnapshot> events)
        {
            _events = events;
        }

        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            StatusRemovedCombatEvent combatEvent)
        {
            _events.Add(new StatusRemovedEventSnapshot(
                TargetCombatantId: combatEvent.TargetCombatantId,
                StatusInstanceIds: combatEvent.StatusInstanceIds.ToArray(),
                StatusDefinitionId: combatEvent.StatusDefinitionId));
        }
    }
}
