using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class StatusCombatEventTests
{
    [Fact]
    public void ApplyStatusEnqueuesStatusAppliedEvent()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CreateCombatWithHero();

        var heroId = new CombatantId("hero_001");

        var resolver = new CombatEffectResolver();

        resolver.Resolve(
            combat,
            registry,
            new ApplyStatusEffectRequest(
                TargetCombatantId: heroId,
                StatusDefinitionId: new StatusDefinitionId("standard.weak"),
                DurationTurns: 2));

        var combatEvent = Assert.Single(combat.PendingEvents);
        var statusApplied = Assert.IsType<StatusAppliedCombatEvent>(combatEvent);

        Assert.Equal(heroId, statusApplied.TargetCombatantId);
        Assert.Equal(new StatusDefinitionId("standard.weak"), statusApplied.StatusDefinitionId);
        Assert.Equal(2, statusApplied.DurationTurns);
    }

    [Fact]
    public void MergeStatusEnqueuesStatusMergedEvent()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CreateCombatWithHero();

        var heroId = new CombatantId("hero_001");

        var resolver = new CombatEffectResolver();

        resolver.Resolve(
            combat,
            registry,
            new ApplyStatusEffectRequest(
                TargetCombatantId: heroId,
                StatusDefinitionId: new StatusDefinitionId("standard.poison"),
                Stacks: 3));

        combat.DequeueNextEvent();

        resolver.Resolve(
            combat,
            registry,
            new ApplyStatusEffectRequest(
                TargetCombatantId: heroId,
                StatusDefinitionId: new StatusDefinitionId("standard.poison"),
                Stacks: 2));

        var combatEvent = Assert.Single(combat.PendingEvents);
        var statusMerged = Assert.IsType<StatusMergedCombatEvent>(combatEvent);

        Assert.Equal(heroId, statusMerged.TargetCombatantId);
        Assert.Equal(new StatusDefinitionId("standard.poison"), statusMerged.StatusDefinitionId);
        Assert.Equal(5, statusMerged.Stacks);
    }

    [Fact]
    public void ExpiringStatusEnqueuesStatusExpiredEvent()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CreateCombatWithHero();

        var heroId = new CombatantId("hero_001");

        var resolver = new CombatEffectResolver();

        resolver.Resolve(
            combat,
            registry,
            new ApplyStatusEffectRequest(
                TargetCombatantId: heroId,
                StatusDefinitionId: new StatusDefinitionId("standard.weak"),
                DurationTurns: 1));

        combat.DequeueNextEvent();

        var status = Assert.Single(combat.GetCombatant(heroId).Statuses);

        resolver.Resolve(
            combat,
            registry,
            new DecreaseStatusDurationEffectRequest(
                TargetCombatantId: heroId,
                StatusInstanceId: status.Id));

        var combatEvent = Assert.Single(combat.PendingEvents);
        var statusExpired = Assert.IsType<StatusExpiredCombatEvent>(combatEvent);

        Assert.Equal(heroId, statusExpired.TargetCombatantId);
        Assert.Equal(status.Id, statusExpired.StatusInstanceId);
        Assert.Equal(new StatusDefinitionId("standard.weak"), statusExpired.StatusDefinitionId);
    }

    [Fact]
    public void CombatQueueProcessorProcessesStatusAppliedEventHandlers()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();

        builder.RegisterCombatEventHandler(new AddLogOnStatusAppliedHandler());
        var registry = builder.Build();

        var combat = CreateCombatWithHero();

        combat.EnqueueEffect(
            new ApplyStatusEffectRequest(
                TargetCombatantId: new CombatantId("hero_001"),
                StatusDefinitionId: new StatusDefinitionId("standard.weak"),
                DurationTurns: 2));

        var processor = new CombatQueueProcessor();

        processor.ResolvePendingQueues(combat, registry);

        Assert.Equal(0, combat.PendingEffectCount);
        Assert.Equal(0, combat.PendingEventCount);
        Assert.Contains(combat.CombatLog, entry => entry.Type == "StatusApplied");
        Assert.Contains(combat.CombatLog, entry => entry.Type == "StatusAppliedEventHandled");
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

    private sealed class AddLogOnStatusAppliedHandler : CombatEventHandler<StatusAppliedCombatEvent>
    {
        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            StatusAppliedCombatEvent combatEvent)
        {
            combat.AddLogEntry(
                "StatusAppliedEventHandled",
                $"Handled status applied event for '{combatEvent.StatusDefinitionId}'.");
        }
    }
}
