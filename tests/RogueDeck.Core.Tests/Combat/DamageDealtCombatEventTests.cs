using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class DamageDealtCombatEventTests
{
    [Fact]
    public void DealDamageEnqueuesDamageDealtEvent()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CreateCombatWithHero();

        var heroId = new CombatantId("hero_001");
        var hero = combat.GetCombatant(heroId);

        hero.AddDefensivePool(
            StandardCombatIds.BlockDefensivePool,
            new ValuePoolState(current: 3));

        var resolver = new CombatEffectResolver();

        resolver.Resolve(
            combat,
            registry,
            new DealDamageEffectRequest(
                TargetCombatantId: heroId,
                Amount: 5));

        var damageEvent = Assert.IsType<DamageDealtCombatEvent>(
            Assert.Single(combat.PendingEvents.OfType<DamageDealtCombatEvent>()));

        Assert.Equal(heroId, damageEvent.TargetCombatantId);
        Assert.Equal(2, damageEvent.HealthDamage);
        Assert.Equal(3, damageEvent.BlockedDamage);
        Assert.Equal(5, damageEvent.RequestedAmount);
    }

    [Fact]
    public void DealDamageEnqueuesDamageReceivedEvent()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CreateCombatWithHero();

        var heroId = new CombatantId("hero_001");
        var sourceId = new CombatantId("enemy_001");
        var sourceCardId = new CardDefinitionId("card.enemy.strike");
        var hero = combat.GetCombatant(heroId);

        hero.AddDefensivePool(
            StandardCombatIds.BlockDefensivePool,
            new ValuePoolState(current: 3));

        var resolver = new CombatEffectResolver();

        resolver.Resolve(
            combat,
            registry,
            new DealDamageEffectRequest(
                TargetCombatantId: heroId,
                Amount: 5,
                SourceCombatantId: sourceId,
                SourceCardId: sourceCardId,
                Kind: DamageKind.Reflected));

        var damageEvent = Assert.IsType<DamageReceivedCombatEvent>(
            Assert.Single(combat.PendingEvents.OfType<DamageReceivedCombatEvent>()));

        Assert.Equal(heroId, damageEvent.ReceiverCombatantId);
        Assert.Equal(2, damageEvent.HealthDamage);
        Assert.Equal(3, damageEvent.BlockedDamage);
        Assert.Equal(5, damageEvent.RequestedAmount);
        Assert.Equal(DamageKind.Reflected, damageEvent.Kind);
        Assert.Equal(sourceId, damageEvent.SourceCombatantId);
        Assert.Equal(sourceCardId, damageEvent.SourceCardId);
    }

    [Fact]
    public void CombatQueueProcessorProcessesDamageDealtEventHandlers()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();

        builder.RegisterCombatEventHandler(new AddLogOnDamageDealtHandler());
        var registry = builder.Build();

        var combat = CreateCombatWithHero();

        combat.EnqueueEffect(
            new DealDamageEffectRequest(
                TargetCombatantId: new CombatantId("hero_001"),
                Amount: 4));

        var processor = new CombatQueueProcessor();

        processor.ResolvePendingQueues(combat, registry);

        Assert.Equal(0, combat.PendingEffectCount);
        Assert.Equal(0, combat.PendingEventCount);
        Assert.Contains(combat.CombatLog, entry => entry.Type == "DamageDealt");
        Assert.Contains(combat.CombatLog, entry => entry.Type == "DamageEventHandled");
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

    private sealed class AddLogOnDamageDealtHandler : CombatEventHandler<DamageDealtCombatEvent>
    {
        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            DamageDealtCombatEvent combatEvent)
        {
            combat.AddLogEntry(
                "DamageEventHandled",
                $"Handled damage event for '{combatEvent.TargetCombatantId}'.");
        }
    }
}
