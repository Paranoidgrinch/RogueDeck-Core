using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class CombatEffectQueueProcessorTests
{
    [Fact]
    public void ResolvePendingEffectsResolvesEffectsInQueueOrder()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

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

        combat.EnqueueEffect(
            new GainBlockEffectRequest(
                TargetCombatantId: heroId,
                Amount: 6));

        combat.EnqueueEffect(
            new DealDamageEffectRequest(
                TargetCombatantId: heroId,
                Amount: 10));

        var processor = new CombatEffectQueueProcessor();

        processor.ResolvePendingEffects(combat, registry);

        var storedHero = combat.GetCombatant(heroId);
        var block = storedHero.DefensivePools[StandardCombatIds.BlockDefensivePool];

        Assert.Equal(16, storedHero.Health.Current);
        Assert.Equal(0, block.Current);
        Assert.Empty(combat.PendingEffects);
        Assert.Equal(2, combat.CombatLog.Count);
        Assert.Equal("BlockGained", combat.CombatLog[0].Type);
        Assert.Equal("DamageDealt", combat.CombatLog[1].Type);
    }

    [Fact]
    public void ResolvePendingEffectsAlsoResolvesEffectsEnqueuedByHandlers()
    {
        var builder = new CombatDefinitionRegistryBuilder();

        builder.RegisterEffectRequestHandler(new EnqueueFollowUpEffectHandler());
        builder.RegisterEffectRequestHandler(new AddTestLogEffectHandler());
        var registry = builder.Build();

        var combat = new CombatState(
            new CombatId("combat_001"),
            randomSeed: 12345);

        combat.EnqueueEffect(new EnqueueFollowUpEffectRequest());

        var processor = new CombatEffectQueueProcessor();

        processor.ResolvePendingEffects(combat, registry);

        Assert.Empty(combat.PendingEffects);
        Assert.Single(combat.CombatLog);
        Assert.Equal("TestFollowUpResolved", combat.CombatLog[0].Type);
    }

    [Fact]
    public void ResolvePendingEffectsStopsAfterMaximumEffectLimit()
    {
        var builder = new CombatDefinitionRegistryBuilder();

        builder.RegisterEffectRequestHandler(new EndlessEffectHandler());
        var registry = builder.Build();

        var combat = new CombatState(
            new CombatId("combat_001"),
            randomSeed: 12345);

        combat.EnqueueEffect(new EndlessEffectRequest());

        var processor = new CombatEffectQueueProcessor();

        Assert.Throws<InvalidOperationException>(() =>
            processor.ResolvePendingEffects(
                combat,
                registry,
                new CombatExecutionLimits(maxEffectsPerCycle: 3)));
    }

    private sealed record EnqueueFollowUpEffectRequest : IEffectRequest;

    private sealed class EnqueueFollowUpEffectHandler : EffectRequestHandler<EnqueueFollowUpEffectRequest>
    {
        protected override void Resolve(
            CombatState combat,
            CombatDefinitionRegistry registry,
            EnqueueFollowUpEffectRequest request)
        {
            combat.EnqueueEffect(new AddTestLogEffectRequest());
        }
    }

    private sealed record AddTestLogEffectRequest : IEffectRequest;

    private sealed class AddTestLogEffectHandler : EffectRequestHandler<AddTestLogEffectRequest>
    {
        protected override void Resolve(
            CombatState combat,
            CombatDefinitionRegistry registry,
            AddTestLogEffectRequest request)
        {
            combat.AddLogEntry(
                "TestFollowUpResolved",
                "Resolved a follow-up test effect.");
        }
    }

    private sealed record EndlessEffectRequest : IEffectRequest;

    private sealed class EndlessEffectHandler : EffectRequestHandler<EndlessEffectRequest>
    {
        protected override void Resolve(
            CombatState combat,
            CombatDefinitionRegistry registry,
            EndlessEffectRequest request)
        {
            combat.EnqueueEffect(new EndlessEffectRequest());
        }
    }
}