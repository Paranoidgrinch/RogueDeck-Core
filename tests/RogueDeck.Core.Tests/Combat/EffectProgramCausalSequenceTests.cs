using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

public class EffectProgramCausalSequenceTests
{
    // ── Structural validation ─────────────────────────────────────────────────

    [Fact]
    public void EmptyCausalSequenceIsValid()
    {
        var program = new EffectProgram<Ctx>(new CausalSequenceEffectNode<Ctx>([]));

        Assert.IsType<CausalSequenceEffectNode<Ctx>>(program.Root);
    }

    [Fact]
    public void CausalSequencePreservesChildOrder()
    {
        var first = new NoOpEffectNode<Ctx>();
        var second = new NoOpEffectNode<Ctx>();

        var node = new CausalSequenceEffectNode<Ctx>([first, second]);
        var program = new EffectProgram<Ctx>(node);

        var root = Assert.IsType<CausalSequenceEffectNode<Ctx>>(program.Root);
        Assert.Equal(2, root.Children.Count);
        Assert.Same(first, root.Children[0]);
        Assert.Same(second, root.Children[1]);
    }

    [Fact]
    public void NullChildInCausalSequenceIsRejectedWithPathInMessage()
    {
        var node = new CausalSequenceEffectNode<Ctx>(
            new IEffectNode<Ctx>[] { new NoOpEffectNode<Ctx>(), null! });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new EffectProgram<Ctx>(node));

        Assert.Contains("root.causal[1]", exception.Message);
    }

    // ── Observable causal ordering ────────────────────────────────────────────

    [Fact]
    public void NodeBObservesPostReactionStateFromNodeA()
    {
        // When hero takes damage, a reaction grants 10 block.
        // In causal mode: Node A fires and settles (damage + reaction block applied)
        //                 before Node B's damage is evaluated.
        // Node A: deal 5 damage to hero  → reaction enqueues GainBlock(10) → block=10
        // Node B: deal 10 damage to hero → absorbed by block → hero health = 20-5 = 15
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterCombatEventHandler(new GainBlockOnHeroDamageHandler());
        var registry = builder.Build();

        var heroId = new CombatantId("hero_001");
        var combat = CombatTestFactory.CreateCombatWithHero();
        var context = new Ctx();

        var program = new EffectProgram<Ctx>(new CausalSequenceEffectNode<Ctx>([
            new DealDamageNode<Ctx>(new ExplicitCombatantTargetSelector(heroId), new ConstantExpression<Ctx>(5)),
            new DealDamageNode<Ctx>(new ExplicitCombatantTargetSelector(heroId), new ConstantExpression<Ctx>(10)),
        ]));

        EffectProgramExecutor.Execute(program, context, MakeBuildContext(combat), combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(15, combat.GetCombatant(heroId).Health.Current);
    }

    [Fact]
    public void BatchSequenceNodeBDoesNotObserveReactionStateFromNodeA()
    {
        // Same setup, batch mode: both damages are enqueued before any reactions.
        // Node A: deal 5 damage  → damage event
        // Node B: deal 10 damage → damage event (no block yet)
        // Then reactions: 2×GainBlock(10) = 20 block (too late for either damage)
        // hero health = 20 - 5 - 10 = 5
        var builder = CombatTestFactory.CreateStandardBuilder();
        builder.RegisterCombatEventHandler(new GainBlockOnHeroDamageHandler());
        var registry = builder.Build();

        var heroId = new CombatantId("hero_001");
        var combat = CombatTestFactory.CreateCombatWithHero();
        var context = new Ctx();

        var program = new EffectProgram<Ctx>(new SequenceEffectNode<Ctx>([
            new DealDamageNode<Ctx>(new ExplicitCombatantTargetSelector(heroId), new ConstantExpression<Ctx>(5)),
            new DealDamageNode<Ctx>(new ExplicitCombatantTargetSelector(heroId), new ConstantExpression<Ctx>(10)),
        ]));

        EffectProgramExecutor.Execute(program, context, MakeBuildContext(combat), combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(5, combat.GetCombatant(heroId).Health.Current);
    }

    // ── Combat-end cancellation ───────────────────────────────────────────────

    [Fact]
    public void CombatEndDuringNodeACancelsNodeB()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        var heroId = new CombatantId("hero_001");
        var goblinId = new CombatantId("goblin_001");
        var combat = CombatTestFactory.CreateCombatWithHeroAndGoblin();
        var context = new Ctx();

        // Node A: lethal damage to goblin → combat ends (Victory)
        // Node B: deal 10 damage to hero → must NOT fire
        var program = new EffectProgram<Ctx>(new CausalSequenceEffectNode<Ctx>([
            new DealDamageNode<Ctx>(new ExplicitCombatantTargetSelector(goblinId), new ConstantExpression<Ctx>(100)),
            new DealDamageNode<Ctx>(new ExplicitCombatantTargetSelector(heroId), new ConstantExpression<Ctx>(10)),
        ]));

        EffectProgramExecutor.Execute(program, context, MakeBuildContext(combat), combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(CombatResult.Victory, combat.Result);
        Assert.Equal(20, combat.GetCombatant(heroId).Health.Current);
    }

    [Fact]
    public void TargetDiesInNodeAButCombatContinuesNodeBStillFires()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        var heroId = new CombatantId("hero_001");
        var goblin1Id = new CombatantId("goblin_001");
        var combat = CombatTestFactory.CreateCombatWithHeroAndTwoGoblins();
        var context = new Ctx();

        // Node A: kill goblin1 → goblin2 still alive, combat ongoing
        // Node B: deal 5 damage to hero → must fire
        var program = new EffectProgram<Ctx>(new CausalSequenceEffectNode<Ctx>([
            new DealDamageNode<Ctx>(new ExplicitCombatantTargetSelector(goblin1Id), new ConstantExpression<Ctx>(100)),
            new DealDamageNode<Ctx>(new ExplicitCombatantTargetSelector(heroId), new ConstantExpression<Ctx>(5)),
        ]));

        EffectProgramExecutor.Execute(program, context, MakeBuildContext(combat), combat);
        new CombatQueueProcessor().ResolvePendingQueues(combat, registry);

        Assert.Equal(CombatResult.Ongoing, combat.Result);
        Assert.Equal(15, combat.GetCombatant(heroId).Health.Current);
    }

    // ── Queue limits ──────────────────────────────────────────────────────────

    [Fact]
    public void QueueCycleLimitAppliesToContinuations()
    {
        // A two-node causal sequence needs at least 2 cycles.
        // MaxQueueCycles=1 must throw.
        var registry = CombatTestFactory.CreateStandardRegistry();

        var heroId = new CombatantId("hero_001");
        var combat = CombatTestFactory.CreateCombatWithHero();
        var context = new Ctx();

        var program = new EffectProgram<Ctx>(new CausalSequenceEffectNode<Ctx>([
            new DealDamageNode<Ctx>(new ExplicitCombatantTargetSelector(heroId), new ConstantExpression<Ctx>(1)),
            new DealDamageNode<Ctx>(new ExplicitCombatantTargetSelector(heroId), new ConstantExpression<Ctx>(1)),
        ]));

        EffectProgramExecutor.Execute(program, context, MakeBuildContext(combat), combat);

        Assert.Throws<InvalidOperationException>(() =>
            new CombatQueueProcessor().ResolvePendingQueues(
                combat, registry,
                new CombatExecutionLimits(maxQueueCycles: 1)));
    }

    [Fact]
    public void QueueCycleLimitSufficientForCausalSequenceSucceeds()
    {
        var registry = CombatTestFactory.CreateStandardRegistry();

        var heroId = new CombatantId("hero_001");
        var combat = CombatTestFactory.CreateCombatWithHero();
        var context = new Ctx();

        var program = new EffectProgram<Ctx>(new CausalSequenceEffectNode<Ctx>([
            new DealDamageNode<Ctx>(new ExplicitCombatantTargetSelector(heroId), new ConstantExpression<Ctx>(1)),
            new DealDamageNode<Ctx>(new ExplicitCombatantTargetSelector(heroId), new ConstantExpression<Ctx>(1)),
        ]));

        EffectProgramExecutor.Execute(program, context, MakeBuildContext(combat), combat);
        new CombatQueueProcessor().ResolvePendingQueues(
            combat, registry,
            new CombatExecutionLimits(maxQueueCycles: 2));

        Assert.Equal(18, combat.GetCombatant(heroId).Health.Current);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TriggeredEffectActionBuildContext MakeBuildContext(CombatState combat) =>
        new(new CombatantTargetSelectionContext(combat, Source: null),
            TriggeredEffectActionSource.None);

    private sealed record Ctx;

    // On every DamageReceivedCombatEvent targeting the hero, grant 10 block to the hero.
    private sealed class GainBlockOnHeroDamageHandler
        : CombatEventHandler<DamageReceivedCombatEvent>
    {
        private static readonly CombatantId HeroId = new("hero_001");

        protected override void Handle(
            CombatState combat,
            CombatDefinitionRegistry registry,
            DamageReceivedCombatEvent e)
        {
            if (e.ReceiverCombatantId == HeroId)
                combat.EnqueueEffect(new GainBlockEffectRequest(HeroId, 10));
        }
    }
}
