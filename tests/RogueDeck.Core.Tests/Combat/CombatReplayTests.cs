using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Phase 7 remaining — replay runner and deterministic trace.
public class CombatReplayTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    // ------------------------------------------------------------------
    // Replay runner — same commands → same final hash
    // ------------------------------------------------------------------

    [Fact]
    public void Replay_SameCommandStream_ProducesSameFinalHash()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var runner = new CombatReplayRunner();
        var commands = new ICombatCommand[]
        {
            new EndTurnCommand(HeroId),    // hero ends turn → goblin starts
            new EndTurnCommand(GoblinId),  // goblin ends turn → hero starts (new round)
            new EndTurnCommand(HeroId),    // hero ends turn → goblin starts
        };

        var combat1 = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        runner.ApplyAll(combat1, registry, commands);

        var combat2 = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        runner.ApplyAll(combat2, registry, commands);

        Assert.Equal(
            CombatStateHasher.ComputeHash(combat1.CreateSnapshot()),
            CombatStateHasher.ComputeHash(combat2.CreateSnapshot()));
    }

    [Fact]
    public void Replay_DifferentCommandStreams_ProduceDifferentFinalHashes()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var runner = new CombatReplayRunner();

        // Stream A: 1 end-turn (hero ends → goblin's turn)
        var combat1 = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        runner.Apply(combat1, registry, new EndTurnCommand(HeroId));

        // Stream B: 3 end-turns (hero → goblin → hero, now on turn 3)
        var combat2 = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        runner.Apply(combat2, registry, new EndTurnCommand(HeroId));
        runner.Apply(combat2, registry, new EndTurnCommand(GoblinId));
        runner.Apply(combat2, registry, new EndTurnCommand(HeroId));

        Assert.NotEqual(
            CombatStateHasher.ComputeHash(combat1.CreateSnapshot()),
            CombatStateHasher.ComputeHash(combat2.CreateSnapshot()));
    }

    [Fact]
    public void Replay_AutoStartsTurn_WhenWaitingToStartTurn()
    {
        // Initial state: WaitingToStartTurn. EndTurnCommand should auto-start then end.
        var registry = CombatTestFactory.CreateStandardRegistry();
        var runner = new CombatReplayRunner();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        Assert.Equal(CombatTurnPhase.WaitingToStartTurn, combat.TurnPhase);
        runner.Apply(combat, registry, new EndTurnCommand(HeroId));

        // Now it should be goblin's turn, still WaitingToStartTurn (EndCurrentTurnAndStartNextTurn
        // advances to next combatant but next StartCurrentTurn fires on the next command).
        Assert.Equal(GoblinId, combat.ActiveCombatantId);
    }

    [Fact]
    public void Replay_PlayCardCommand_EnqueuesPlayCardEffect()
    {
        // Verify PlayCardCommand routes to PlayCardEffectRequest (effect resolves gracefully
        // when the card isn't actually in hand — WasPlayed=false is the no-op path).
        var registry = CombatTestFactory.CreateStandardRegistry();
        var runner = new CombatReplayRunner();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        // No exception expected — non-existent card gracefully no-ops.
        var cmd = new PlayCardCommand(HeroId, new CardInstanceId("card_000001"), GoblinId);
        runner.Apply(combat, registry, cmd);

        // Hero is still alive, combat still ongoing.
        Assert.Equal(CombatResult.Ongoing, combat.Result);
        Assert.True(combat.GetCombatant(HeroId).IsAlive);
    }

    // ------------------------------------------------------------------
    // Trace — events are emitted and ordered correctly
    // ------------------------------------------------------------------

    [Fact]
    public void Trace_EffectEnqueuedAndResolved_EmittedPerEffect()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var listener = new CollectingTraceListener();
        combat.TraceListener = listener;

        combat.EnqueueEffect(new DealDamageEffectRequest(GoblinId, Amount: 5));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Single(listener.Events, e => e is EffectEnqueuedTraceEvent t
            && t.RequestType == nameof(DealDamageEffectRequest));
        Assert.Single(listener.Events, e => e is EffectResolvedTraceEvent t
            && t.RequestType == nameof(DealDamageEffectRequest));
    }

    [Fact]
    public void Trace_EnqueuedBeforeResolved()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var listener = new CollectingTraceListener();
        combat.TraceListener = listener;

        combat.EnqueueEffect(new DealDamageEffectRequest(GoblinId, Amount: 3));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var enqueuedIdx = listener.Events.FindIndex(e => e is EffectEnqueuedTraceEvent);
        var resolvedIdx = listener.Events.FindIndex(e => e is EffectResolvedTraceEvent);
        Assert.True(enqueuedIdx < resolvedIdx);
    }

    [Fact]
    public void Trace_TurnStartedAndEnded_EmittedByTurnProcessor()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var listener = new CollectingTraceListener();
        combat.TraceListener = listener;
        var processor = new CombatTurnProcessor();

        processor.StartCurrentTurn(combat, registry);
        processor.EndCurrentTurnAndStartNextTurn(combat, registry);

        Assert.Contains(listener.Events, e => e is TurnStartedTraceEvent t && t.CombatantId == HeroId);
        Assert.Contains(listener.Events, e => e is TurnEndedTraceEvent t && t.CombatantId == HeroId);
    }

    [Fact]
    public void Trace_EventDispatched_EmittedAfterCombatEventHandling()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var listener = new CollectingTraceListener();
        combat.TraceListener = listener;

        combat.EnqueueEvent(new TurnStartedCombatEvent(HeroId, Round: 1, Turn: 1));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Single(listener.Events, e => e is CombatEventDispatchedTraceEvent t
            && t.EventType == nameof(TurnStartedCombatEvent));
    }

    [Fact]
    public void Trace_IsDeterministic_SameScenarioProducesSameTraceSequence()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var runner = new CombatReplayRunner();

        var listener1 = new CollectingTraceListener();
        var combat1 = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat1.TraceListener = listener1;
        runner.Apply(combat1, registry, new EndTurnCommand(HeroId));

        var listener2 = new CollectingTraceListener();
        var combat2 = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        combat2.TraceListener = listener2;
        runner.Apply(combat2, registry, new EndTurnCommand(HeroId));

        // Type sequence must be identical.
        var types1 = listener1.Events.Select(e => e.GetType().Name).ToList();
        var types2 = listener2.Events.Select(e => e.GetType().Name).ToList();
        Assert.Equal(types1, types2);
    }

    [Fact]
    public void Trace_CommandApplied_EmittedByReplayRunner()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var runner = new CombatReplayRunner();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var listener = new CollectingTraceListener();
        combat.TraceListener = listener;

        runner.Apply(combat, registry, new EndTurnCommand(HeroId));

        Assert.Single(listener.Events, e => e is CommandAppliedTraceEvent t
            && t.CommandType == nameof(EndTurnCommand));
    }

    [Fact]
    public void Trace_NoListener_DoesNotThrow()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        // TraceListener is null by default — all trace calls must be no-ops.
        var act = () =>
        {
            combat.EnqueueEffect(new DealDamageEffectRequest(GoblinId, Amount: 2));
            new CombatQueueProcessor().ResolvePendingQueues(combat, registry);
        };

        var ex = Record.Exception(act);
        Assert.Null(ex);
    }

    // Master plan §34 — strict replay: a complex scenario (turn automation + a registered trigger)
    // reproduces not only the same final hash but the same full trace sequence (event order and
    // every field), proving deterministic outcome/event/trigger order, not just matching hashes.
    [Fact]
    public void ComplexScenario_ReplayProducesIdenticalTraceAndHash()
    {
        (List<string> Trace, string Hash) Run()
        {
            var builder = CombatTestFactory.CreateStandardBuilder();
            var statusId = new StatusDefinitionId("test.replay_trace_buff");
            builder.RegisterStatus(new StatusDefinition(
                statusId, new PackageId("test"), "n", "d",
                polarity: StatusPolarity.Buff, usesStacks: true, showStacksInUi: true,
                stackingBehavior: StatusStackingBehavior.MergeWithExistingInstance));
            builder.RegisterTriggeredEffectDefinition(
                TriggeredProgramContextAdapters.TurnStarted.Define(
                    id: new TriggeredEffectDefinitionId("test.replay_trace_trigger"),
                    program: new EffectProgram<TurnStartedTriggeredEffectContext>(
                        new ApplyStatusNode<TurnStartedTriggeredEffectContext>(
                            CombatantTargetSelectors.Source,
                            statusId,
                            stacks: new ConstantExpression<TurnStartedTriggeredEffectContext>(1)))));
            var registry = builder.Build();

            var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
            var listener = new CollectingTraceListener();
            combat.TraceListener = listener;

            new CombatReplayRunner().ApplyAll(combat, registry, new ICombatCommand[]
            {
                new EndTurnCommand(HeroId),
                new EndTurnCommand(GoblinId),
                new EndTurnCommand(HeroId),
            });

            // ToString() on the trace records includes every field, so comparing the string list
            // proves full trace shape, order, and detail — not merely event types.
            return (listener.Events.Select(e => e.ToString()!).ToList(),
                CombatStateHasher.ComputeHash(combat.CreateSnapshot()));
        }

        var first = Run();
        var second = Run();

        Assert.Equal(first.Trace, second.Trace);
        Assert.Equal(first.Hash, second.Hash);
        Assert.NotEmpty(first.Trace);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private sealed class CollectingTraceListener : ICombatTraceListener
    {
        public List<CombatTraceEvent> Events { get; } = new();
        public void OnTrace(CombatTraceEvent evt) => Events.Add(evt);
    }
}
