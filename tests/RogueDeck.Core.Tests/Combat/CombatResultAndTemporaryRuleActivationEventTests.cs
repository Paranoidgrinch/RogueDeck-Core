using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// P0.6 — CombatResultChanged is an observable, NON-triggerable event (log + trace only).
// TemporaryRuleActivated is a fully triggerable meta-event fired when a temporary rule activates.
public class CombatResultAndTemporaryRuleActivationEventTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    private sealed class CapturingTraceListener : ICombatTraceListener
    {
        public List<CombatTraceEvent> Events { get; } = [];
        public void OnTrace(CombatTraceEvent evt) => Events.Add(evt);
    }

    // ── CombatResultChanged ──────────────────────────────────────────────────────

    [Fact]
    public void CombatResultChanged_LoggedAndTraced_OnTransition()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var listener = new CapturingTraceListener();
        combat.TraceListener = listener;

        combat.SetResult(CombatResult.Victory);

        Assert.Equal(1, combat.CombatLog.Count(e => e.Type == StandardCombatLogTypes.CombatResultChanged));
        var traced = Assert.Single(listener.Events.OfType<CombatResultChangedTraceEvent>());
        Assert.Equal(CombatResult.Ongoing, traced.PreviousResult);
        Assert.Equal(CombatResult.Victory, traced.NewResult);
    }

    [Fact]
    public void CombatResultChanged_NotEmitted_WhenResultUnchanged()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var listener = new CapturingTraceListener();
        combat.TraceListener = listener;

        combat.SetResult(CombatResult.Ongoing); // already Ongoing → no change

        Assert.DoesNotContain(combat.CombatLog, e => e.Type == StandardCombatLogTypes.CombatResultChanged);
        Assert.Empty(listener.Events.OfType<CombatResultChangedTraceEvent>());
    }

    [Fact]
    public void CombatResultChanged_HasNoGenericTriggerAdapter()
    {
        // The event is intentionally non-triggerable: there is no adapter for it on
        // TriggeredProgramContextAdapters. (Compile-time absence is the contract; this test documents
        // the decision and guards against an accidental future adapter being relied upon here.)
        var adapterProperties = typeof(TriggeredProgramContextAdapters)
            .GetFields()
            .Select(f => f.FieldType)
            .ToList();

        Assert.DoesNotContain(adapterProperties, t =>
            t.IsGenericType &&
            t.GetGenericArguments().Contains(typeof(CombatResultChangedCombatEvent)));
    }

    // ── TemporaryRuleActivated ───────────────────────────────────────────────────

    [Fact]
    public void TemporaryRuleActivated_FiresRegisteredMetaTrigger()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();

        var tempStatus = new StatusDefinitionId("test.temp_applied");
        var metaStatus = new StatusDefinitionId("test.meta_applied");
        foreach (var id in new[] { tempStatus, metaStatus })
            builder.RegisterStatus(new StatusDefinition(
                id, new PackageId("test"), "n", "d",
                polarity: StatusPolarity.Buff, usesStacks: true, showStacksInUi: true,
                stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance));

        // A registered meta-trigger: when ANY temporary rule activates, apply metaStatus to the
        // active combatant (the event target).
        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.TemporaryRuleActivated.Define(
                id: new TriggeredEffectDefinitionId("test.on_rule_activated"),
                program: new EffectProgram<TemporaryRuleActivatedTriggeredEffectContext>(
                    new ApplyStatusNode<TemporaryRuleActivatedTriggeredEffectContext>(
                        CombatantTargetSelectors.EventTarget,
                        metaStatus,
                        stacks: new ConstantExpression<TemporaryRuleActivatedTriggeredEffectContext>(1)))));
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.SetActiveCombatant(HeroId);

        // A temporary rule that activates on TurnStarted.
        combat.AddTemporaryTriggeredProgram(
            TriggeredProgramContextAdapters.TurnStarted.Define(
                id: new TriggeredEffectDefinitionId("temp.on_turn"),
                program: new EffectProgram<TurnStartedTriggeredEffectContext>(
                    new ApplyStatusNode<TurnStartedTriggeredEffectContext>(
                        CombatantTargetSelectors.Source,
                        tempStatus,
                        stacks: new ConstantExpression<TurnStartedTriggeredEffectContext>(1)))),
            TemporaryRuleLifetime.Unlimited);

        combat.EnqueueEvent(new TurnStartedCombatEvent(HeroId, Round: 1, Turn: 1));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var hero = combat.GetCombatant(HeroId);
        Assert.Single(hero.Statuses, s => s.DefinitionId == tempStatus);  // temp rule ran
        Assert.Single(hero.Statuses, s => s.DefinitionId == metaStatus);  // meta-trigger fired
        Assert.Contains(combat.CombatLog, e => e.Type == StandardCombatLogTypes.TemporaryRuleActivated);
    }

    [Fact]
    public void TemporaryRuleActivated_DoesNotFire_WhenNoTemporaryRuleActivates()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var metaStatus = new StatusDefinitionId("test.meta_applied");
        builder.RegisterStatus(new StatusDefinition(
            metaStatus, new PackageId("test"), "n", "d",
            polarity: StatusPolarity.Buff, usesStacks: true, showStacksInUi: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance));

        builder.RegisterTriggeredEffectDefinition(
            TriggeredProgramContextAdapters.TemporaryRuleActivated.Define(
                id: new TriggeredEffectDefinitionId("test.on_rule_activated"),
                program: new EffectProgram<TemporaryRuleActivatedTriggeredEffectContext>(
                    new ApplyStatusNode<TemporaryRuleActivatedTriggeredEffectContext>(
                        CombatantTargetSelectors.EventTarget,
                        metaStatus,
                        stacks: new ConstantExpression<TemporaryRuleActivatedTriggeredEffectContext>(1)))));
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.SetActiveCombatant(HeroId);

        // No temporary rule installed → TurnStarted activates nothing → no meta-trigger.
        combat.EnqueueEvent(new TurnStartedCombatEvent(HeroId, Round: 1, Turn: 1));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.DoesNotContain(combat.GetCombatant(HeroId).Statuses, s => s.DefinitionId == metaStatus);
    }
}
