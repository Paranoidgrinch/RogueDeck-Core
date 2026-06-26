using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class EffectProgramLifecycleTests
{
    private static readonly CombatantId HeroId = new("hero_001");
    private static readonly CombatantId GoblinId = new("goblin_001");

    [Fact]
    public void SetCombatantLifecycleStateNodeChangesState()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);

        var program = new EffectProgram<Ctx>(new SetCombatantLifecycleStateNode<Ctx>(
            CombatantTargetSelectors.EventTarget,
            CombatantLifecycleState.Downed));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(CombatantLifecycleState.Downed,
            combat.GetCombatant(GoblinId).LifecycleState);
    }

    [Fact]
    public void SetCombatantLifecycleStateNodeOutcomeRecordsChange()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);
        var resultKey = new EffectResultKey<OrderedTargetOutcomes<SetCombatantLifecycleStateOutcome>>("lifecycle");

        var program = new EffectProgram<Ctx>(new SetCombatantLifecycleStateNode<Ctx>(
            CombatantTargetSelectors.EventTarget,
            CombatantLifecycleState.Downed,
            resultKey));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var outcome = ctx.Get(resultKey).Single();
        Assert.True(outcome.WasChanged);
        Assert.Equal(CombatantLifecycleState.Alive, outcome.PreviousState);
        Assert.Equal(CombatantLifecycleState.Downed, outcome.NewState);
    }

    [Fact]
    public void SetCombatantLifecycleStateNodeOutcomeRecordsNoChangeWhenAlreadyInState()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var ctx = MakeContext(combat);
        var resultKey = new EffectResultKey<OrderedTargetOutcomes<SetCombatantLifecycleStateOutcome>>("lifecycle");

        var program = new EffectProgram<Ctx>(new SetCombatantLifecycleStateNode<Ctx>(
            CombatantTargetSelectors.EventTarget,
            CombatantLifecycleState.Alive,
            resultKey));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        var outcome = ctx.Get(resultKey).Single();
        Assert.False(outcome.WasChanged);
        Assert.Equal(CombatantLifecycleState.Alive, outcome.PreviousState);
        Assert.Equal(CombatantLifecycleState.Alive, outcome.NewState);
    }

    // ── Composite: SetLifecycle → DealDamage ─────────────────────────────────
    //
    // CausalSequence [
    //   SetLifecycleState(goblin, Downed)   → resultKey=lifecycle
    //   DealDamage(hero, RemovedCount=0)    — constant 5 to prove causal ordering
    // ]
    //
    // Goblin is downed, then hero takes 5 damage in the same causal chain.
    // Two goblins ensure combat stays Ongoing after one is downed.

    [Fact]
    public void DownAndPunishDealsDamageToHeroAfterGoblinIsNewlyDowned()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();
        var combat = CombatTestFactory.CreateCombatWithHeroAndTwoGoblins();
        var ctx = MakeContext(combat);
        var lifecycleKey = new EffectResultKey<OrderedTargetOutcomes<SetCombatantLifecycleStateOutcome>>("lifecycle");

        var program = new EffectProgram<Ctx>(new CausalSequenceEffectNode<Ctx>([
            new SetCombatantLifecycleStateNode<Ctx>(
                CombatantTargetSelectors.EventTarget,
                CombatantLifecycleState.Downed,
                lifecycleKey),
            new DealDamageNode<Ctx>(
                CombatantTargetSelectors.Source,
                new ConstantExpression<Ctx>(5)),
        ]));

        EffectProgramExecutor.Execute(program, ctx, combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(CombatantLifecycleState.Downed, combat.GetCombatant(GoblinId).LifecycleState);
        Assert.Equal(CombatResult.Ongoing, combat.Result);

        var outcome = ctx.Get(lifecycleKey).Single();
        Assert.True(outcome.WasChanged);

        Assert.Equal(20 - 5, combat.GetCombatant(HeroId).Health.Current);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static EffectExecutionContext<Ctx> MakeContext(CombatState combat) =>
        new(new Ctx(),
            new TriggeredEffectActionBuildContext(
                new CombatantTargetSelectionContext(
                    Combat: combat,
                    Source: combat.GetCombatant(HeroId),
                    EventTargetId: GoblinId),
                new TriggeredEffectActionSource(SourceCombatantId: HeroId)));

    private sealed record Ctx;
}
