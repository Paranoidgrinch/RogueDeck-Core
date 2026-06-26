using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Combat Engine Closure — Commit 3: trigger parity matrix after legacy wrapper removal.
//
// The 24 concrete *TriggeredEffectDefinition wrapper classes were removed and replaced by
// the generic TriggeredProgramDefinition<TEventContext> direction. This file proves that no
// event family was silently dropped: every adapter in TriggeredProgramContextAdapters still
// dispatches its program when the matching event is raised.
//
// Cross-cutting behaviors (priority order, filters, re-entry suppression, depth limit,
// event-target / source attribution) are handled identically for every family by the single
// generic TriggeredProgramCombatEventHandler<TEvent, TEventContext>, so they are proven once
// here (deterministic ordering) and per-representative-family elsewhere.
public class TriggeredProgramParityTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    // Universal firing probe: register a side-effect program on the adapter, raise the event,
    // and assert the program ran exactly once. Uses the test-only AllowUnsafeSideEffects opt-in
    // so the probe is independent of each family's target/source semantics.
    private static void AssertFiresOnce<TEvent, TEventContext>(
        TriggeredProgramAdapter<TEvent, TEventContext> adapter,
        Func<CombatDefinitionRegistry, TEvent> makeEvent,
        Action<CombatState>? setup = null)
        where TEvent : class, ICombatEvent
        where TEventContext : class
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.AllowUnsafeSideEffects = true;

        var fired = 0;
        builder.RegisterTriggeredEffectDefinition(
            adapter.Define(
                new TriggeredEffectDefinitionId("test.parity.fires"),
                new EffectProgram<TEventContext>(
                    new SideEffectNode<TEventContext>((_, _) => fired++))));
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        setup?.Invoke(combat);
        combat.EnqueueEvent(makeEvent(registry));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(1, fired);
    }

    // ── Firing parity matrix — one assertion per supported event family ───────────

    [Fact]
    public void TurnStarted_Fires() =>
        AssertFiresOnce(
            TriggeredProgramContextAdapters.TurnStarted,
            _ => new TurnStartedCombatEvent(HeroId, Round: 1, Turn: 1));

    [Fact]
    public void TurnEnded_Fires() =>
        AssertFiresOnce(
            TriggeredProgramContextAdapters.TurnEnded,
            _ => new TurnEndedCombatEvent(HeroId, Round: 1, Turn: 1));

    [Fact]
    public void RoundStarted_Fires() =>
        AssertFiresOnce(
            TriggeredProgramContextAdapters.RoundStarted,
            _ => new RoundStartedCombatEvent(Round: 1),
            setup: c => c.SetActiveCombatant(HeroId));

    [Fact]
    public void RoundEnded_Fires() =>
        AssertFiresOnce(
            TriggeredProgramContextAdapters.RoundEnded,
            _ => new RoundEndedCombatEvent(Round: 1, LastActiveCombatantId: HeroId));

    [Fact]
    public void DamageDealt_Fires() =>
        AssertFiresOnce(
            TriggeredProgramContextAdapters.DamageDealt,
            _ => new DamageDealtCombatEvent(GoblinId, HealthDamage: 5, BlockedDamage: 0, RequestedAmount: 5, SourceCombatantId: HeroId));

    [Fact]
    public void DamageReceived_Fires() =>
        AssertFiresOnce(
            TriggeredProgramContextAdapters.DamageReceived,
            _ => new DamageReceivedCombatEvent(GoblinId, HealthDamage: 5, BlockedDamage: 0, RequestedAmount: 5, SourceCombatantId: HeroId));

    [Fact]
    public void Healed_Fires() =>
        AssertFiresOnce(
            TriggeredProgramContextAdapters.Healed,
            _ => new HealedCombatEvent(HeroId, HealedAmount: 3, RequestedAmount: 3, SourceCombatantId: HeroId));

    [Fact]
    public void StatusApplied_Fires() =>
        AssertFiresOnce(
            TriggeredProgramContextAdapters.StatusApplied,
            _ => new StatusAppliedCombatEvent(
                GoblinId, new StatusInstanceId("si.1"), new StatusDefinitionId("test.status"),
                Stacks: 1, DurationTurns: 0, Charges: 0, SourceCombatantId: HeroId));

    [Fact]
    public void StatusApplicationBlocked_Fires() =>
        AssertFiresOnce(
            TriggeredProgramContextAdapters.StatusApplicationBlocked,
            _ => new StatusApplicationBlockedCombatEvent(
                GoblinId, new StatusDefinitionId("test.blocked"),
                new StatusInstanceId("si.block"), new StatusDefinitionId("test.blocker")));

    [Fact]
    public void StatusesRemovedByPolarity_Fires() =>
        AssertFiresOnce(
            TriggeredProgramContextAdapters.StatusesRemovedByPolarity,
            _ => new StatusesRemovedByPolarityCombatEvent(
                GoblinId, [new StatusInstanceId("si.1")], StatusPolarity.Debuff));

    [Fact]
    public void StatusRemoved_Fires() =>
        AssertFiresOnce(
            TriggeredProgramContextAdapters.StatusRemoved,
            _ => new StatusRemovedCombatEvent(
                GoblinId, [new StatusInstanceId("si.1")], new StatusDefinitionId("test.status")));

    [Fact]
    public void StatusChargesReduced_Fires() =>
        AssertFiresOnce(
            TriggeredProgramContextAdapters.StatusChargesReduced,
            _ => new StatusChargesReducedCombatEvent(
                GoblinId, new StatusInstanceId("si.1"), new StatusDefinitionId("test.status"),
                OldCharges: 3, NewCharges: 2));

    [Fact]
    public void StatusExpired_Fires() =>
        AssertFiresOnce(
            TriggeredProgramContextAdapters.StatusExpired,
            _ => new StatusExpiredCombatEvent(
                GoblinId, new StatusInstanceId("si.1"), new StatusDefinitionId("test.status")));

    [Fact]
    public void StatusMerged_Fires() =>
        AssertFiresOnce(
            TriggeredProgramContextAdapters.StatusMerged,
            _ => new StatusMergedCombatEvent(
                GoblinId, new StatusInstanceId("si.1"), new StatusDefinitionId("test.status"),
                Stacks: 2, DurationTurns: 0, Charges: 0, SourceCombatantId: HeroId));

    [Fact]
    public void ResourceGained_Fires() =>
        AssertFiresOnce(
            TriggeredProgramContextAdapters.ResourceGained,
            _ => new ResourceGainedCombatEvent(
                HeroId, new ResourceId("test.resource"),
                PreviousCurrent: 0, NewCurrent: 1, GainedAmount: 1, Max: 3));

    [Fact]
    public void ResourceModified_Fires() =>
        AssertFiresOnce(
            TriggeredProgramContextAdapters.ResourceModified,
            _ => new ResourceModifiedCombatEvent(
                HeroId, new ResourceId("test.resource"),
                PreviousCurrent: 3, NewCurrent: 1, AppliedDelta: -2, Max: 3));

    [Fact]
    public void CardPlayed_Fires() =>
        AssertFiresOnce(
            TriggeredProgramContextAdapters.CardPlayed,
            _ => new CardPlayedCombatEvent(StandardCombatIds.StrikeCard, HeroId, GoblinId));

    [Fact]
    public void CardCostPaid_Fires() =>
        AssertFiresOnce(
            TriggeredProgramContextAdapters.CardCostPaid,
            _ => new CardCostPaidCombatEvent(
                HeroId, StandardCombatIds.StrikeCard, new CardInstanceId("ci.1"),
                [new CalculatedResourceCost(StandardCombatIds.EnergyResource, 1)]));

    [Fact]
    public void CardInstanceCreated_Fires() =>
        AssertFiresOnce(
            TriggeredProgramContextAdapters.CardInstanceCreated,
            _ => new CardInstanceCreatedCombatEvent(
                HeroId, StandardCombatIds.StrikeCard, [new CardInstanceId("ci.1")], CardZone.Hand));

    [Fact]
    public void CombatantDowned_Fires() =>
        AssertFiresOnce(
            TriggeredProgramContextAdapters.CombatantDowned,
            _ => new CombatantLifecycleChangedCombatEvent(
                GoblinId, CombatantLifecycleState.Alive, CombatantLifecycleState.Downed));

    [Fact]
    public void EnemyActionExecuted_Fires() =>
        AssertFiresOnce(
            TriggeredProgramContextAdapters.EnemyActionExecuted,
            _ => new EnemyActionExecutedCombatEvent(
                new EnemyActionDefinitionId("test.action"), GoblinId, HeroId));

    // ── P0.5 — completing the triggerability matrix ──────────────────────────────

    [Fact]
    public void ResourceRefilled_Fires() =>
        AssertFiresOnce(
            TriggeredProgramContextAdapters.ResourceRefilled,
            _ => new ResourceRefilledCombatEvent(
                HeroId, new ResourceId("test.resource"),
                PreviousCurrent: 0, NewCurrent: 3, Max: 3));

    [Fact]
    public void CardsDrawn_Fires() =>
        AssertFiresOnce(
            TriggeredProgramContextAdapters.CardsDrawn,
            _ => new CardsDrawnCombatEvent(HeroId, [new CardInstanceId("ci.1")]));

    [Fact]
    public void CardMovedToZone_Fires() =>
        AssertFiresOnce(
            TriggeredProgramContextAdapters.CardMovedToZone,
            _ => new CardMovedToZoneCombatEvent(
                HeroId, new CardInstanceId("ci.1"), CardZone.Hand, CardZone.DiscardPile));

    [Fact]
    public void HandDiscarded_Fires() =>
        AssertFiresOnce(
            TriggeredProgramContextAdapters.HandDiscarded,
            _ => new HandDiscardedCombatEvent(HeroId, [new CardInstanceId("ci.1")]));

    [Fact]
    public void DiscardPileShuffled_Fires() =>
        AssertFiresOnce(
            TriggeredProgramContextAdapters.DiscardPileShuffled,
            _ => new DiscardPileShuffledIntoDrawPileCombatEvent(HeroId, [new CardInstanceId("ci.1")]));

    [Fact]
    public void StatusStacksChanged_Fires() =>
        AssertFiresOnce(
            TriggeredProgramContextAdapters.StatusStacksChanged,
            _ => new StatusStacksChangedCombatEvent(
                GoblinId, new StatusInstanceId("si.1"), new StatusDefinitionId("test.status"),
                OldStacks: 1, NewStacks: 3));

    [Fact]
    public void StatusDurationChanged_Fires() =>
        AssertFiresOnce(
            TriggeredProgramContextAdapters.StatusDurationChanged,
            _ => new StatusDurationChangedCombatEvent(
                GoblinId, new StatusInstanceId("si.1"), new StatusDefinitionId("test.status"),
                OldDuration: 2, NewDuration: 1));

    [Fact]
    public void StatusChargesChanged_Fires() =>
        AssertFiresOnce(
            TriggeredProgramContextAdapters.StatusChargesChanged,
            _ => new StatusChargesChangedCombatEvent(
                GoblinId, new StatusInstanceId("si.1"), new StatusDefinitionId("test.status"),
                OldCharges: 3, NewCharges: 2));

    [Fact]
    public void CombatantLifecycleChanged_Fires_OnAnyTransition() =>
        AssertFiresOnce(
            TriggeredProgramContextAdapters.CombatantLifecycleChanged,
            _ => new CombatantLifecycleChangedCombatEvent(
                GoblinId, CombatantLifecycleState.Downed, CombatantLifecycleState.Alive));

    [Fact]
    public void TemporaryRuleActivated_Fires() =>
        AssertFiresOnce(
            TriggeredProgramContextAdapters.TemporaryRuleActivated,
            _ => new TemporaryRuleActivatedCombatEvent(
                new TriggeredEffectDefinitionId("temp.rule"), typeof(TurnStartedCombatEvent), HeroId));

    [Fact]
    public void CombatantDowned_DoesNotFire_OnNonDownedTransition() =>
        AssertNeverFires(
            TriggeredProgramContextAdapters.CombatantDowned,
            _ => new CombatantLifecycleChangedCombatEvent(
                GoblinId, CombatantLifecycleState.Alive, CombatantLifecycleState.Alive));

    // ── Event-target attribution for families not otherwise covered ──────────────

    [Fact]
    public void ResourceGained_TargetsGainingCombatant() =>
        AssertEventTargetIs(
            TriggeredProgramContextAdapters.ResourceGained,
            _ => new ResourceGainedCombatEvent(
                HeroId, new ResourceId("test.resource"),
                PreviousCurrent: 0, NewCurrent: 1, GainedAmount: 1, Max: 3),
            expectedTarget: HeroId);

    [Fact]
    public void CardInstanceCreated_TargetsOwningCombatant() =>
        AssertEventTargetIs(
            TriggeredProgramContextAdapters.CardInstanceCreated,
            _ => new CardInstanceCreatedCombatEvent(
                HeroId, StandardCombatIds.StrikeCard, [new CardInstanceId("ci.1")], CardZone.Hand),
            expectedTarget: HeroId);

    [Fact]
    public void StatusRemoved_TargetsAffectedCombatant() =>
        AssertEventTargetIs(
            TriggeredProgramContextAdapters.StatusRemoved,
            _ => new StatusRemovedCombatEvent(
                GoblinId, [new StatusInstanceId("si.1")], new StatusDefinitionId("test.status")),
            expectedTarget: GoblinId);

    [Fact]
    public void StatusChargesReduced_TargetsAffectedCombatant() =>
        AssertEventTargetIs(
            TriggeredProgramContextAdapters.StatusChargesReduced,
            _ => new StatusChargesReducedCombatEvent(
                GoblinId, new StatusInstanceId("si.1"), new StatusDefinitionId("test.status"),
                OldCharges: 3, NewCharges: 2),
            expectedTarget: GoblinId);

    [Fact]
    public void StatusApplicationBlocked_TargetsBlockedCombatant() =>
        AssertEventTargetIs(
            TriggeredProgramContextAdapters.StatusApplicationBlocked,
            _ => new StatusApplicationBlockedCombatEvent(
                GoblinId, new StatusDefinitionId("test.blocked"),
                new StatusInstanceId("si.block"), new StatusDefinitionId("test.blocker")),
            expectedTarget: GoblinId);

    [Fact]
    public void StatusesRemovedByPolarity_TargetsAffectedCombatant() =>
        AssertEventTargetIs(
            TriggeredProgramContextAdapters.StatusesRemovedByPolarity,
            _ => new StatusesRemovedByPolarityCombatEvent(
                GoblinId, [new StatusInstanceId("si.1")], StatusPolarity.Debuff),
            expectedTarget: GoblinId);

    // ── Deterministic ordering — equal priority resolves by Id, not registration order ──

    [Fact]
    public void EqualPriority_FiresInDeterministicIdOrder_RegardlessOfRegistrationOrder()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.AllowUnsafeSideEffects = true;

        var order = new List<string>();

        // Register in reverse Id order to prove the handler sorts by Id, not insertion order.
        builder.RegisterTriggeredEffectDefinition(MakeRecorder("test.zzz", order, priority: 0));
        builder.RegisterTriggeredEffectDefinition(MakeRecorder("test.aaa", order, priority: 0));
        builder.RegisterTriggeredEffectDefinition(MakeRecorder("test.mmm", order, priority: 0));
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.EnqueueEvent(new DamageDealtCombatEvent(GoblinId, 5, 0, 5, SourceCombatantId: HeroId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(["test.aaa", "test.mmm", "test.zzz"], order);
    }

    [Fact]
    public void Priority_OverridesIdOrder()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.AllowUnsafeSideEffects = true;

        var order = new List<string>();

        // "test.zzz" has lower priority number → fires before "test.aaa" despite later Id.
        builder.RegisterTriggeredEffectDefinition(MakeRecorder("test.aaa", order, priority: 5));
        builder.RegisterTriggeredEffectDefinition(MakeRecorder("test.zzz", order, priority: 0));
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.EnqueueEvent(new DamageDealtCombatEvent(GoblinId, 5, 0, 5, SourceCombatantId: HeroId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(["test.zzz", "test.aaa"], order);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static void AssertNeverFires<TEvent, TEventContext>(
        TriggeredProgramAdapter<TEvent, TEventContext> adapter,
        Func<CombatDefinitionRegistry, TEvent> makeEvent)
        where TEvent : class, ICombatEvent
        where TEventContext : class
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.AllowUnsafeSideEffects = true;

        var fired = 0;
        builder.RegisterTriggeredEffectDefinition(
            adapter.Define(
                new TriggeredEffectDefinitionId("test.parity.never"),
                new EffectProgram<TEventContext>(
                    new SideEffectNode<TEventContext>((_, _) => fired++))));
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.EnqueueEvent(makeEvent(registry));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(0, fired);
    }

    private static void AssertEventTargetIs<TEvent, TEventContext>(
        TriggeredProgramAdapter<TEvent, TEventContext> adapter,
        Func<CombatDefinitionRegistry, TEvent> makeEvent,
        CombatantId expectedTarget)
        where TEvent : class, ICombatEvent
        where TEventContext : class
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        var statusId = RegisterTestStatus(builder);

        builder.RegisterTriggeredEffectDefinition(
            adapter.Define(
                new TriggeredEffectDefinitionId("test.parity.target"),
                new EffectProgram<TEventContext>(
                    new ApplyStatusNode<TEventContext>(
                        CombatantTargetSelectors.EventTarget,
                        statusId,
                        stacks: new ConstantExpression<TEventContext>(1)))));
        var registry = builder.Build();

        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat.EnqueueEvent(makeEvent(registry));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Single(combat.GetCombatant(expectedTarget).Statuses, s => s.DefinitionId == statusId);
        var other = expectedTarget == HeroId ? GoblinId : HeroId;
        Assert.Empty(combat.GetCombatant(other).Statuses);
    }

    private static StatusDefinitionId RegisterTestStatus(CombatDefinitionRegistryBuilder builder)
    {
        var id = new StatusDefinitionId("test.parity_status");
        builder.RegisterStatus(new StatusDefinition(
            id,
            new PackageId("test"),
            displayNameKey: "status.test.name",
            descriptionKey: "status.test.description",
            polarity: StatusPolarity.Buff,
            usesStacks: true,
            showStacksInUi: true,
            stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance));
        return id;
    }

    private static ITriggeredEffectDefinition MakeRecorder(
        string id,
        List<string> order,
        int priority) =>
        TriggeredProgramContextAdapters.DamageDealt.Define(
            new TriggeredEffectDefinitionId(id),
            new EffectProgram<DamageDealtTriggeredEffectContext>(
                new SideEffectNode<DamageDealtTriggeredEffectContext>((_, _) => order.Add(id))),
            priority: priority);
}
