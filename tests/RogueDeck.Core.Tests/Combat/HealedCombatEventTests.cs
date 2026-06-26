using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class HealedCombatEventTests
{
    [Fact]
    public void HealEnqueuesHealedCombatEvent()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CreateCombatWithDamagedHero();

        var heroId = new CombatantId("hero_001");

        var resolver = new CombatEffectResolver();

        resolver.Resolve(
            combat,
            registry,
            new HealEffectRequest(
                TargetCombatantId: heroId,
                Amount: 5));

        var combatEvent = Assert.Single(combat.PendingEvents);
        var healedEvent = Assert.IsType<HealedCombatEvent>(combatEvent);

        Assert.Equal(heroId, healedEvent.TargetCombatantId);
        Assert.Equal(5, healedEvent.HealedAmount);
        Assert.Equal(5, healedEvent.RequestedAmount);
    }

    [Fact]
    public void HealEventUsesActualHealedAmountWhenCappedByMaxHealth()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CreateCombatWithDamagedHero();

        var heroId = new CombatantId("hero_001");

        var resolver = new CombatEffectResolver();

        resolver.Resolve(
            combat,
            registry,
            new HealEffectRequest(
                TargetCombatantId: heroId,
                Amount: 99));

        var combatEvent = Assert.Single(combat.PendingEvents);
        var healedEvent = Assert.IsType<HealedCombatEvent>(combatEvent);

        Assert.Equal(10, healedEvent.HealedAmount);
        Assert.Equal(99, healedEvent.RequestedAmount);
    }

    [Fact]
    public void CombatQueueProcessorProcessesHealedCombatEventHandlers()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();

        builder.RegisterCombatEventHandler(new AddLogOnHealedHandler());
        var registry = builder.Build();

        var combat = CreateCombatWithDamagedHero();

        combat.EnqueueEffect(
            new HealEffectRequest(
                TargetCombatantId: new CombatantId("hero_001"),
                Amount: 4));

        var processor = new CombatQueueProcessor();

        processor.ResolvePendingQueues(combat, registry);

        Assert.Equal(0, combat.PendingEffectCount);
        Assert.Equal(0, combat.PendingEventCount);
        Assert.Contains(combat.CombatLog, entry => entry.Type == "Healed");
        Assert.Contains(combat.CombatLog, entry => entry.Type == "HealedEventHandled");
    }

    private static CombatState CreateCombatWithDamagedHero()
    {
        var combat = new CombatState(
            new CombatId("combat_001"),
            randomSeed: 12345);

        var hero = new CombatantState(
            new CombatantId("hero_001"),
            new CombatantDefinitionId("standard.hero"),
            "combatant.hero",
            new TeamId("player"),
            new HealthState(current: 10, max: 20));

        combat.AddCombatant(hero);

        return combat;
    }

    private sealed class AddLogOnHealedHandler : CombatEventHandler<HealedCombatEvent>
    {
        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            HealedCombatEvent combatEvent)
        {
            combat.AddLogEntry(
                "HealedEventHandled",
                $"Handled healed event for '{combatEvent.TargetCombatantId}'.");
        }
    }
}
