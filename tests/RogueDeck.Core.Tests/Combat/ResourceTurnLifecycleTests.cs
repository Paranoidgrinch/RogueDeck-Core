using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class ResourceTurnLifecycleTests
{
    private static readonly CombatantId HeroId = new("hero_001");

    [Fact]
    public void RefillResourceEffectCreatesMissingResourceWithDefaultMax()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        combat.EnqueueEffect(new RefillResourceEffectRequest(
            HeroId,
            StandardCombatIds.EnergyResource,
            DefaultMax: 3));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var hero = combat.GetCombatant(HeroId);
        var energy = hero.Resources[StandardCombatIds.EnergyResource];

        Assert.Equal(3, energy.Current);
        Assert.Equal(3, energy.Max);

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.ResourceRefilled);
    }

    [Fact]
    public void RefillResourceEffectRefillsExistingResourceToExistingMax()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var hero = combat.GetCombatant(HeroId);
        hero.AddResource(
            StandardCombatIds.EnergyResource,
            new ValuePoolState(current: 1, max: 5));

        combat.EnqueueEffect(new RefillResourceEffectRequest(
            HeroId,
            StandardCombatIds.EnergyResource,
            DefaultMax: 3));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var energy = hero.Resources[StandardCombatIds.EnergyResource];

        Assert.Equal(5, energy.Current);
        Assert.Equal(5, energy.Max);
    }

    [Fact]
    public void RefillResourceEffectEmitsResourceRefilledEvent()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var eventHandler = new CaptureResourceRefilledEventHandler();
        builder.RegisterCombatEventHandler(eventHandler);
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var hero = combat.GetCombatant(HeroId);
        hero.AddResource(
            StandardCombatIds.EnergyResource,
            new ValuePoolState(current: 1, max: 3));

        combat.EnqueueEffect(new RefillResourceEffectRequest(
            HeroId,
            StandardCombatIds.EnergyResource,
            DefaultMax: 3));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var handledEvent = Assert.Single(eventHandler.HandledEvents);

        Assert.Equal(HeroId, handledEvent.CombatantId);
        Assert.Equal(StandardCombatIds.EnergyResource, handledEvent.ResourceId);
        Assert.Equal(1, handledEvent.PreviousCurrent);
        Assert.Equal(3, handledEvent.NewCurrent);
        Assert.Equal(3, handledEvent.Max);
    }

    [Fact]
    public void StandardCombatPackageRefillsEnergyWhenTurnStarts()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var hero = combat.GetCombatant(HeroId);
        hero.AddResource(
            StandardCombatIds.EnergyResource,
            new ValuePoolState(current: 0, max: 3));

        new CombatTurnProcessor().StartCurrentTurn(combat, registry);

        var energy = hero.Resources[StandardCombatIds.EnergyResource];

        Assert.Equal(3, energy.Current);
        Assert.Equal(3, energy.Max);

        Assert.Contains(
            combat.CombatLog,
            entry => entry.Type == StandardCombatLogTypes.ResourceRefilled);
    }

    [Fact]
    public void StandardCombatPackageCreatesEnergyWhenTurnStartsAndEnergyIsMissing()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        var hero = combat.GetCombatant(HeroId);

        Assert.False(hero.Resources.ContainsKey(StandardCombatIds.EnergyResource));

        new CombatTurnProcessor().StartCurrentTurn(combat, registry);

        var energy = hero.Resources[StandardCombatIds.EnergyResource];

        Assert.Equal(3, energy.Current);
        Assert.Equal(3, energy.Max);
    }

    [Fact]
    public void StandardCombatPackageRegistersResourceLifecycleHandlers()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        Assert.IsType<RefillResourceEffectHandler>(
            registry.GetEffectRequestHandler(typeof(RefillResourceEffectRequest)));

        Assert.Contains(
            registry.GetCombatEventHandlers(typeof(TurnStartedCombatEvent)),
            handler => handler is RefillResourceOnTurnStartedHandler);
    }

    private sealed class CaptureResourceRefilledEventHandler
        : CombatEventHandler<ResourceRefilledCombatEvent>
    {
        public List<ResourceRefilledCombatEvent> HandledEvents { get; } = new();

        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            ResourceRefilledCombatEvent combatEvent)
        {
            HandledEvents.Add(combatEvent);
        }
    }
}
