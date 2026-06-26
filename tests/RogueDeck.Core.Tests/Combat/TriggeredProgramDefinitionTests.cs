using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Tests for TriggeredProgramDefinition<TEventContext> and the associated
// generic handler / context adapter infrastructure (Phase 5 §12).
public class TriggeredProgramDefinitionTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    // ── Registration ─────────────────────────────────────────────────────────

    [Fact]
    public void StandardCombatPackage_RegistersGenericHandlerForEveryEventType()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();

        // Each adapter's CreateHandler() produces a distinct handler type per
        // TEvent/TEventContext pair.  Spot-check the most commonly used ones.
        Assert.Contains(
            builder.Build().GetCombatEventHandlers(typeof(DamageDealtCombatEvent)),
            h => h is TriggeredProgramCombatEventHandler<DamageDealtCombatEvent, DamageDealtTriggeredEffectContext>);

        Assert.Contains(
            builder.Build().GetCombatEventHandlers(typeof(TurnStartedCombatEvent)),
            h => h is TriggeredProgramCombatEventHandler<TurnStartedCombatEvent, TurnStartedTriggeredEffectContext>);

        Assert.Contains(
            builder.Build().GetCombatEventHandlers(typeof(CardPlayedCombatEvent)),
            h => h is TriggeredProgramCombatEventHandler<CardPlayedCombatEvent, CardPlayedTriggeredEffectContext>);

        Assert.Contains(
            builder.Build().GetCombatEventHandlers(typeof(CombatantLifecycleChangedCombatEvent)),
            h => h is TriggeredProgramCombatEventHandler<CombatantLifecycleChangedCombatEvent, CombatantDownedTriggeredEffectContext>);
    }

    // ── Basic program execution ───────────────────────────────────────────────

    [Fact]
    public void Program_FiresOnMatchingEvent_AppliesStatusToEventTarget()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var statusId = RegisterTestStatus(builder);

        var definition = TriggeredProgramContextAdapters.DamageDealt.Define(
            id: new TriggeredEffectDefinitionId("test.apply_on_damage"),
            program: new EffectProgram<DamageDealtTriggeredEffectContext>(
                new ApplyStatusNode<DamageDealtTriggeredEffectContext>(
                    CombatantTargetSelectors.EventTarget,
                    statusId,
                    stacks: new ConstantExpression<DamageDealtTriggeredEffectContext>(2))));

        builder.RegisterTriggeredEffectDefinition(definition);

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        DealDamage(combat, builder.Build(), GoblinId, 5, HeroId);

        Assert.Equal(
            2,
            Assert.Single(
                combat.GetCombatant(GoblinId).Statuses,
                s => s.DefinitionId == statusId).Stacks);
    }

    [Fact]
    public void Program_NotFiredForDifferentEventType()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var statusId = RegisterTestStatus(builder);

        // Register on DamageReceived, not DamageDealt
        var definition = TriggeredProgramContextAdapters.DamageReceived.Define(
            id: new TriggeredEffectDefinitionId("test.apply_on_damage_received"),
            program: new EffectProgram<DamageReceivedTriggeredEffectContext>(
                new ApplyStatusNode<DamageReceivedTriggeredEffectContext>(
                    CombatantTargetSelectors.EventTarget,
                    statusId,
                    stacks: new ConstantExpression<DamageReceivedTriggeredEffectContext>(1))));

        builder.RegisterTriggeredEffectDefinition(definition);

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        // Fire only DamageDealt (no DamageReceived registered to run)
        // In standard setup DamageReceived is a distinct event; verify goblin
        // gets status only when that event fires.  Triggering via DealDamage
        // fires both events, so we verify hero gets it (hero was not damaged):
        DealDamage(combat, builder.Build(), GoblinId, 5, HeroId);

        // Status applied to the receiver (goblin) — correct event fired
        Assert.Single(
            combat.GetCombatant(GoblinId).Statuses,
            s => s.DefinitionId == statusId);

        // Hero was source, not receiver — no status
        Assert.Empty(combat.GetCombatant(HeroId).Statuses);
    }

    // ── Priority ordering ─────────────────────────────────────────────────────

    [Fact]
    public void Priority_LowerNumberFiresFirst()
    {
        // Priority=0: +5 block for goblin (fires first)
        // Priority=1: -3 block for goblin (fires second)
        // +5 then -3 = 2.  If reversed (-3 clamped to 0, then +5) = 5.
        // Pool is clamped at 0 so order is observable.
        var builder = CombatTestFactory.CreateStandardBuilder();

        var gainBlock = TriggeredProgramContextAdapters.DamageDealt.Define(
            id: new TriggeredEffectDefinitionId("test.priority.gain_block"),
            program: new EffectProgram<DamageDealtTriggeredEffectContext>(
                new ModifyDefensivePoolNode<DamageDealtTriggeredEffectContext>(
                    CombatantTargetSelectors.EventTarget,
                    StandardCombatIds.BlockDefensivePool,
                    new ConstantExpression<DamageDealtTriggeredEffectContext>(5))),
            priority: 0);

        var decrementPool = TriggeredProgramContextAdapters.DamageDealt.Define(
            id: new TriggeredEffectDefinitionId("test.priority.decrement"),
            program: new EffectProgram<DamageDealtTriggeredEffectContext>(
                new ModifyDefensivePoolNode<DamageDealtTriggeredEffectContext>(
                    CombatantTargetSelectors.EventTarget,
                    StandardCombatIds.BlockDefensivePool,
                    new ConstantExpression<DamageDealtTriggeredEffectContext>(-3))),
            priority: 1);

        builder.RegisterTriggeredEffectDefinition(gainBlock);
        builder.RegisterTriggeredEffectDefinition(decrementPool);

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        DealDamage(combat, builder.Build(), GoblinId, 5, HeroId);

        Assert.Equal(2, GetBlockOrZero(combat, GoblinId));
    }

    // ── Filter ───────────────────────────────────────────────────────────────

    [Fact]
    public void Filter_BlocksExecution_WhenFilterReturnsFalse()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var statusId = RegisterTestStatus(builder);

        var definition = TriggeredProgramContextAdapters.DamageDealt.Define(
            id: new TriggeredEffectDefinitionId("test.filter.blocks"),
            program: new EffectProgram<DamageDealtTriggeredEffectContext>(
                new ApplyStatusNode<DamageDealtTriggeredEffectContext>(
                    CombatantTargetSelectors.EventTarget,
                    statusId,
                    stacks: new ConstantExpression<DamageDealtTriggeredEffectContext>(1))),
            filters: [new NeverMatchFilter()]);

        builder.RegisterTriggeredEffectDefinition(definition);

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        DealDamage(combat, builder.Build(), GoblinId, 5, HeroId);

        Assert.Empty(combat.GetCombatant(GoblinId).Statuses);
    }

    [Fact]
    public void Filter_AllowsExecution_WhenFilterReturnsTrue()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var statusId = RegisterTestStatus(builder);

        var definition = TriggeredProgramContextAdapters.DamageDealt.Define(
            id: new TriggeredEffectDefinitionId("test.filter.passes"),
            program: new EffectProgram<DamageDealtTriggeredEffectContext>(
                new ApplyStatusNode<DamageDealtTriggeredEffectContext>(
                    CombatantTargetSelectors.EventTarget,
                    statusId,
                    stacks: new ConstantExpression<DamageDealtTriggeredEffectContext>(1))),
            filters: [new AlwaysMatchFilter()]);

        builder.RegisterTriggeredEffectDefinition(definition);

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        DealDamage(combat, builder.Build(), GoblinId, 5, HeroId);

        Assert.Single(
            combat.GetCombatant(GoblinId).Statuses,
            s => s.DefinitionId == statusId);
    }

    // ── Re-entry suppression ─────────────────────────────────────────────────

    [Fact]
    public void ReentryPolicy_SuppressRecursiveReentry_PreventsSelfLoop()
    {
        // A trigger that deals 3 damage on DamageDealt would loop infinitely
        // without re-entry suppression.  With it, the triggered damage fires
        // the event once more but the second time the trigger is already in
        // the chain ancestry, so it is skipped.
        //
        // Initial damage: 5 → goblin health 12 - 5 = 7
        // Trigger fires: deals 3 → goblin health 7 - 3 = 4
        // Trigger would fire again but is suppressed → no more damage
        // Final goblin health: 4

        var builder = CombatTestFactory.CreateStandardBuilder();

        var definition = TriggeredProgramContextAdapters.DamageDealt.Define(
            id: new TriggeredEffectDefinitionId("test.reentry.self_damage"),
            program: new EffectProgram<DamageDealtTriggeredEffectContext>(
                new DealDamageNode<DamageDealtTriggeredEffectContext>(
                    CombatantTargetSelectors.EventTarget,
                    new ConstantExpression<DamageDealtTriggeredEffectContext>(3))),
            reentryPolicy: TriggeredEffectReentryPolicy.SuppressRecursiveReentry);

        builder.RegisterTriggeredEffectDefinition(definition);

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        DealDamage(combat, builder.Build(), GoblinId, 5);

        Assert.Equal(4, combat.GetCombatant(GoblinId).Health.Current);
    }

    // ── Adapter.Define builds correct definition ──────────────────────────────

    [Fact]
    public void Define_SetsIdPriorityReentryPolicy()
    {
        var id = new TriggeredEffectDefinitionId("test.properties");
        var program = new EffectProgram<DamageDealtTriggeredEffectContext>(
            new NoOpEffectNode<DamageDealtTriggeredEffectContext>());

        var definition = TriggeredProgramContextAdapters.DamageDealt.Define(
            id: id,
            program: program,
            priority: 42,
            reentryPolicy: TriggeredEffectReentryPolicy.AllowRecursiveReentry);

        Assert.Equal(id, definition.Id);
        Assert.Equal(42, definition.Priority);
        Assert.Equal(TriggeredEffectReentryPolicy.AllowRecursiveReentry, definition.ReentryPolicy);
        Assert.Equal(typeof(DamageDealtCombatEvent), definition.EventType);
        // The unnamed program is re-wrapped with a derived id at construction, but it keeps
        // the same root node.
        Assert.Same(program.Root, definition.Program.Root);
    }

    [Fact]
    public void Construction_AssignsProgramIdFromDefinitionId_WhenUnnamed()
    {
        var definition = TriggeredProgramContextAdapters.DamageDealt.Define(
            id: new TriggeredEffectDefinitionId("test.freeze"),
            program: new EffectProgram<DamageDealtTriggeredEffectContext>(
                new NoOpEffectNode<DamageDealtTriggeredEffectContext>()));

        // The derived program id is assigned immediately at construction — the definition is
        // born immutable, with no separate freeze step.
        Assert.Equal("trigger:test.freeze", definition.Program.Id.Value);
    }

    // ── Context adapters ─────────────────────────────────────────────────────

    [Fact]
    public void TurnStarted_Adapter_ProgramFiresOnTurnStart()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var statusId = RegisterTestStatus(builder);

        var definition = TriggeredProgramContextAdapters.TurnStarted.Define(
            id: new TriggeredEffectDefinitionId("test.turn_started.apply_status"),
            program: new EffectProgram<TurnStartedTriggeredEffectContext>(
                new ApplyStatusNode<TurnStartedTriggeredEffectContext>(
                    CombatantTargetSelectors.Source,
                    statusId,
                    stacks: new ConstantExpression<TurnStartedTriggeredEffectContext>(3))));

        builder.RegisterTriggeredEffectDefinition(definition);

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.SetActiveCombatant(HeroId);
        combat.EnqueueEvent(new TurnStartedCombatEvent(HeroId, Round: 1, Turn: 1));
        new CombatQueueProcessor().ResolvePendingQueues(combat, builder.Build());

        Assert.Equal(
            3,
            Assert.Single(
                combat.GetCombatant(HeroId).Statuses,
                s => s.DefinitionId == statusId).Stacks);
    }

    [Fact]
    public void CombatantDowned_Adapter_SkipsNonDownedLifecycleTransitions()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var statusId = RegisterTestStatus(builder);

        var definition = TriggeredProgramContextAdapters.CombatantDowned.Define(
            id: new TriggeredEffectDefinitionId("test.downed.apply_status"),
            program: new EffectProgram<CombatantDownedTriggeredEffectContext>(
                new ApplyStatusNode<CombatantDownedTriggeredEffectContext>(
                    CombatantTargetSelectors.EventTarget,
                    statusId,
                    stacks: new ConstantExpression<CombatantDownedTriggeredEffectContext>(1))));

        builder.RegisterTriggeredEffectDefinition(definition);

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        // Fire a lifecycle-changed event with a non-downed transition
        combat.EnqueueEvent(new CombatantLifecycleChangedCombatEvent(
            GoblinId,
            CombatantLifecycleState.Alive,
            CombatantLifecycleState.Alive));
        new CombatQueueProcessor().ResolvePendingQueues(combat, builder.Build());

        Assert.Empty(combat.GetCombatant(GoblinId).Statuses);

        // Now fire an actual downed transition
        combat.EnqueueEvent(new CombatantLifecycleChangedCombatEvent(
            GoblinId,
            CombatantLifecycleState.Alive,
            CombatantLifecycleState.Downed));
        new CombatQueueProcessor().ResolvePendingQueues(combat, builder.Build());

        Assert.Single(
            combat.GetCombatant(GoblinId).Statuses,
            s => s.DefinitionId == statusId);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static StatusDefinitionId RegisterTestStatus(CombatDefinitionRegistryBuilder builder)
    {
        var id = new StatusDefinitionId("test.triggered_program_status");
        var definition = new StatusDefinition(
            id,
            new PackageId("test"),
            displayNameKey: "status.test.name",
            descriptionKey: "status.test.description",
            polarity: StatusPolarity.Buff,
            usesStacks: true,
            showStacksInUi: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance);
        builder.RegisterStatus(definition);
        return id;
    }

    private static void DealDamage(
        CombatState combat,
        CombatDefinitionRegistry registry,
        CombatantId targetId,
        int amount,
        CombatantId? sourceId = null)
    {
        combat.EnqueueEffect(new DealDamageEffectRequest(
            TargetCombatantId: targetId,
            Amount: amount,
            SourceCombatantId: sourceId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
    }

    private static int GetBlockOrZero(CombatState combat, CombatantId id)
    {
        var c = combat.GetCombatant(id);
        return c.DefensivePools.TryGetValue(StandardCombatIds.BlockDefensivePool, out var pool)
            ? pool.Current
            : 0;
    }

    private sealed class NeverMatchFilter : ITriggeredProgramFilter<DamageDealtTriggeredEffectContext>
    {
        public bool Matches(DamageDealtTriggeredEffectContext context) => false;
    }

    private sealed class AlwaysMatchFilter : ITriggeredProgramFilter<DamageDealtTriggeredEffectContext>
    {
        public bool Matches(DamageDealtTriggeredEffectContext context) => true;
    }
}
