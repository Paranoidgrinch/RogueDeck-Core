using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class CombatantLifecycleEffectTests
{
    [Fact]
    public void SetCombatantLifecycleStateChangesLifecycleState()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CreateCombatWithHeroAndGoblin();

        var heroId = new CombatantId("hero_001");

        var resolver = new CombatEffectResolver();

        resolver.Resolve(
            combat,
            registry,
            new SetCombatantLifecycleStateEffectRequest(
                CombatantId: heroId,
                LifecycleState: CombatantLifecycleState.Downed));

        var hero = combat.GetCombatant(heroId);

        Assert.Equal(CombatantLifecycleState.Downed, hero.LifecycleState);
        Assert.False(hero.IsAlive);
        Assert.Contains(combat.CombatLog, entry => entry.Type == "CombatantLifecycleChanged");
    }

    [Fact]
    public void DamageThatReducesHealthToZeroMarksCombatantDowned()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CreateCombatWithHeroAndGoblin();

        var heroId = new CombatantId("hero_001");
        var goblinId = new CombatantId("goblin_001");

        combat.EnqueueEffect(
            new DealDamageEffectRequest(
                TargetCombatantId: goblinId,
                Amount: 99,
                SourceCombatantId: heroId));

        var processor = new CombatQueueProcessor();

        processor.ResolvePendingQueues(combat, registry);

        var goblin = combat.GetCombatant(goblinId);

        Assert.Equal(0, goblin.Health.Current);
        Assert.Equal(CombatantLifecycleState.Downed, goblin.LifecycleState);
        Assert.False(goblin.IsAlive);
        Assert.Equal(0, combat.PendingEffectCount);
        Assert.Equal(0, combat.PendingEventCount);
        Assert.Contains(combat.CombatLog, entry => entry.Type == "DamageDealt");
        Assert.Contains(combat.CombatLog, entry => entry.Type == "CombatantLifecycleChanged");
    }

    [Fact]
    public void FullyBlockedDamageDoesNotMarkCombatantDowned()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CreateCombatWithHeroAndGoblin();

        var heroId = new CombatantId("hero_001");
        var goblinId = new CombatantId("goblin_001");
        var goblin = combat.GetCombatant(goblinId);

        goblin.AddDefensivePool(
            StandardCombatIds.BlockDefensivePool,
            new ValuePoolState(current: 99));

        combat.EnqueueEffect(
            new DealDamageEffectRequest(
                TargetCombatantId: goblinId,
                Amount: 12,
                SourceCombatantId: heroId));

        var processor = new CombatQueueProcessor();

        processor.ResolvePendingQueues(combat, registry);

        Assert.Equal(12, goblin.Health.Current);
        Assert.Equal(CombatantLifecycleState.Alive, goblin.LifecycleState);
        Assert.True(goblin.IsAlive);
        Assert.DoesNotContain(combat.CombatLog, entry => entry.Type == "CombatantLifecycleChanged");
    }

    [Fact]
    public void DamageDoesNotMarkAlreadyNonAliveCombatantAgain()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CreateCombatWithHeroAndGoblin();

        var heroId = new CombatantId("hero_001");
        var goblinId = new CombatantId("goblin_001");
        var goblin = combat.GetCombatant(goblinId);

        goblin.SetLifecycleState(CombatantLifecycleState.Downed);

        combat.EnqueueEffect(
            new DealDamageEffectRequest(
                TargetCombatantId: goblinId,
                Amount: 99,
                SourceCombatantId: heroId));

        var processor = new CombatQueueProcessor();

        processor.ResolvePendingQueues(combat, registry);

        Assert.Equal(CombatantLifecycleState.Downed, goblin.LifecycleState);
        Assert.DoesNotContain(combat.CombatLog, entry => entry.Type == "CombatantLifecycleChanged");
    }

    [Fact]
    public void SetCombatantLifecycleStateEmitsLifecycleChangedEventAfterStateChange()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var combat = CreateCombatWithHeroAndGoblin();

        var snapshots = new List<LifecycleChangedSnapshot>();
        builder.RegisterCombatEventHandler(new CaptureLifecycleChangedSnapshotHandler(snapshots));
        var registry = builder.Build();

        var heroId = new CombatantId("hero_001");

        combat.EnqueueEffect(new SetCombatantLifecycleStateEffectRequest(
            CombatantId: heroId,
            LifecycleState: CombatantLifecycleState.Downed));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var snapshot = Assert.Single(snapshots);

        Assert.Equal(heroId, snapshot.CombatantId);
        Assert.Equal(CombatantLifecycleState.Alive, snapshot.OldState);
        Assert.Equal(CombatantLifecycleState.Downed, snapshot.NewState);
        Assert.Equal(CombatantLifecycleState.Downed, snapshot.ObservedCurrentState);
    }

    [Fact]
    public void LifecycleHandlerRegisteredBeforeResultHandlerCanQueueEffectsBeforeCombatResultChanges()
    {
        var builder = new CombatDefinitionRegistryBuilder();

        builder.RegisterEffectRequestHandler(new DealDamageEffectHandler());
        builder.RegisterEffectRequestHandler(new GainBlockEffectHandler());
        builder.RegisterEffectRequestHandler(new SetCombatantLifecycleStateEffectHandler());
        builder.RegisterEffectRequestHandler(new SetCombatResultEffectHandler());

        builder.RegisterCombatEventHandler(new MarkCombatantDownedOnZeroHealthHandler());
        builder.RegisterCombatEventHandler(
            new GainBlockWhenCombatantIsDownedHandler(
                new CombatantId("hero_001"),
                amount: 4));
        builder.RegisterCombatEventHandler(new UpdateStandardCombatResultOnLifecycleChangedHandler());
        var registry = builder.Build();

        var combat = CreateCombatWithHeroAndGoblin();

        combat.EnqueueEffect(new DealDamageEffectRequest(
            TargetCombatantId: new CombatantId("goblin_001"),
            Amount: 99,
            SourceCombatantId: new CombatantId("hero_001")));

        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(CombatResult.Victory, combat.Result);

        Assert.Equal(
            4,
            GetBlockOrZero(combat, new CombatantId("hero_001")));

        Assert.Equal(0, combat.PendingEffectCount);
        Assert.Equal(0, combat.PendingEventCount);
    }

    private sealed record LifecycleChangedSnapshot(
        CombatantId CombatantId,
        CombatantLifecycleState OldState,
        CombatantLifecycleState NewState,
        CombatantLifecycleState ObservedCurrentState);

    private sealed class CaptureLifecycleChangedSnapshotHandler
        : CombatEventHandler<CombatantLifecycleChangedCombatEvent>
    {
        private readonly List<LifecycleChangedSnapshot> _snapshots;

        public CaptureLifecycleChangedSnapshotHandler(List<LifecycleChangedSnapshot> snapshots)
        {
            _snapshots = snapshots;
        }

        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            CombatantLifecycleChangedCombatEvent combatEvent)
        {
            var combatant = combat.GetCombatant(combatEvent.CombatantId);

            _snapshots.Add(new LifecycleChangedSnapshot(
                CombatantId: combatEvent.CombatantId,
                OldState: combatEvent.OldState,
                NewState: combatEvent.NewState,
                ObservedCurrentState: combatant.LifecycleState));
        }
    }

    private sealed class GainBlockWhenCombatantIsDownedHandler
        : CombatEventHandler<CombatantLifecycleChangedCombatEvent>
    {
        private readonly CombatantId _targetCombatantId;
        private readonly int _amount;

        public GainBlockWhenCombatantIsDownedHandler(
            CombatantId targetCombatantId,
            int amount)
        {
            _targetCombatantId = targetCombatantId;
            _amount = amount;
        }

        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            CombatantLifecycleChangedCombatEvent combatEvent)
        {
            if (combatEvent.NewState != CombatantLifecycleState.Downed)
                return;

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
            new TeamId("player"),
            new HealthState(current: 20, max: 20));

        var goblin = new CombatantState(
            new CombatantId("goblin_001"),
            new CombatantDefinitionId("standard.goblin"),
            "combatant.goblin",
            new TeamId("enemy"),
            new HealthState(current: 12, max: 12));

        combat.AddCombatant(hero);
        combat.AddCombatant(goblin);

        return combat;
    }
}
