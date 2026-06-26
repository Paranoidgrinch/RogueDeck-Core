using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Master plan §7 — proof of program terminal lifecycle contracts.
//
// The card-play terminal cleanup, native-handler fault binding, combat-end cancellation, and
// stale-continuation rejection are already proven in EffectProgramTerminalStateTests,
// EffectProgramNativeFaultBindingTests, and CardProgramTerminalCleanupTests. This file adds the
// remaining explicit proofs: a frame reaches exactly one terminal state, and enemy-action programs
// have the same fault / cancel lifecycle as card programs.
public class ProgramTerminalLifecycleContractsTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    [Fact]
    public void ProgramFrameCompletesExactlyOnce()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var trace = new RecordingEffectProgramTraceSink();

        var frame = EffectProgramExecutor.Execute(
            new EffectProgram<Ctx>(new DealDamageNode<Ctx>(
                CombatantTargetSelectors.EventTarget, new ConstantExpression<Ctx>(1))),
            MakeContext(combat, trace), combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(EffectProgramExecutionState.Completed, frame.State);
        Assert.Single(trace.EventsOfKind(EffectProgramTraceEventKind.ProgramCompleted));
        Assert.Empty(trace.EventsOfKind(EffectProgramTraceEventKind.ProgramCancelled));
        Assert.Empty(trace.EventsOfKind(EffectProgramTraceEventKind.ProgramFaulted));
    }

    [Fact]
    public void EnemyActionProgram_Faults_WhenNodeThrows()
    {
        // Parity with card programs: a node throwing in a resumed slice faults the enemy-action
        // frame and propagates, after the earlier step's effects have settled.
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.AllowUnsafeSideEffects = true;
        var action = new EnemyActionDefinitionBuilder(
            new EnemyActionDefinitionId("test.fault"), new PackageId("test"),
            "action.fault.name", "action.fault.desc")
        {
            Program = new EffectProgram<EnemyActionContext>(
                new CausalSequenceEffectNode<EnemyActionContext>([
                    new DealDamageNode<EnemyActionContext>(
                        CombatantTargetSelectors.EventTarget,
                        new ConstantExpression<EnemyActionContext>(3)),
                    new SideEffectNode<EnemyActionContext>((_, _) => throw new InvalidOperationException("boom")),
                ])),
        };
        builder.RegisterEnemyAction(action);
        var registry = builder.Build();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        combat.EnqueueEffect(new ExecuteEnemyActionEffectRequest(GoblinId, action.Id, HeroId));
        Assert.Throws<InvalidOperationException>(
            () => new CombatQueueProcessor().ResolvePendingQueues(combat, registry));

        // Step 1 settled before the fault; the fault then unwound the action.
        Assert.Equal(17, combat.GetCombatant(HeroId).Health.Current);
    }

    [Fact]
    public void EnemyActionProgram_LaterStepSkipped_WhenCombatEnds()
    {
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.AllowUnsafeSideEffects = true;
        var laterStepRan = false;
        var action = new EnemyActionDefinitionBuilder(
            new EnemyActionDefinitionId("test.endcombat"), new PackageId("test"),
            "action.endcombat.name", "action.endcombat.desc")
        {
            Program = new EffectProgram<EnemyActionContext>(
                new CausalSequenceEffectNode<EnemyActionContext>([
                    new SetCombatResultNode<EnemyActionContext>(CombatResult.Defeat),
                    new SideEffectNode<EnemyActionContext>((_, _) => laterStepRan = true),
                ])),
        };
        builder.RegisterEnemyAction(action);
        var registry = builder.Build();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();

        combat.EnqueueEffect(new ExecuteEnemyActionEffectRequest(GoblinId, action.Id, HeroId));
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(CombatResult.Defeat, combat.Result);
        Assert.False(laterStepRan);
    }

    private static EffectExecutionContext<Ctx> MakeContext(
        CombatState combat, IEffectProgramTraceSink? trace = null)
    {
        var ctx = new EffectExecutionContext<Ctx>(
            new Ctx(),
            new TriggeredEffectActionBuildContext(
                new CombatantTargetSelectionContext(
                    Combat: combat, Source: combat.GetCombatant(HeroId), EventTargetId: GoblinId),
                new TriggeredEffectActionSource(SourceCombatantId: HeroId)));
        if (trace is not null)
            ctx.TraceSink = trace;
        return ctx;
    }

    private sealed record Ctx;
}
