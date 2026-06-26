using RogueDeck.Core.Combat;

namespace RogueDeck.Core.Tests;

// Final Closure — Work package 6: iteration targets live in lexical scopes.
//
// There is no public mutable IterationTarget setter; ForEach and aggregates push/pop a balanced
// iteration scope, so the outer target is restored automatically — even when an inner evaluation
// throws.
public class IterationTargetScopeTests
{
    private static readonly CombatantId HeroId = new("hero_001");

    [Fact]
    public void Aggregate_RestoresOuterIterationTarget_AfterCompletion()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndTwoGoblins();
        var ctx = MakeContext(combat);
        ctx.PushIterationTarget(HeroId);

        var sum = new SumOverTargetsExpression<Ctx>(
            CombatantTargetSelectors.AllEnemiesOfSource,
            new ConstantExpression<Ctx>(1));

        Assert.Equal(2, sum.Evaluate(ctx, combat));
        // The aggregate's per-target pushes/pops balanced; the outer target is intact.
        Assert.Equal(HeroId, ctx.IterationTarget);
    }

    [Fact]
    public void Aggregate_ExceptionDuringEvaluation_RestoresOuterIterationTarget()
    {
        var combat = CombatTestFactory.CreateCombatWithHeroAndTwoGoblins();
        var ctx = MakeContext(combat);
        ctx.PushIterationTarget(HeroId);

        var sum = new SumOverTargetsExpression<Ctx>(
            CombatantTargetSelectors.AllEnemiesOfSource,
            new ThrowingIntExpression());

        Assert.Throws<InvalidOperationException>(() => sum.Evaluate(ctx, combat));
        // The finally-pop ran despite the throw, restoring the outer iteration target.
        Assert.Equal(HeroId, ctx.IterationTarget);
    }

    [Fact]
    public void IterationTarget_IsNull_OutsideAnyScope()
    {
        var ctx = MakeContext(CombatTestFactory.CreateCombatWithHeroAndGoblin());
        Assert.Null(ctx.IterationTarget);
    }

    private sealed class ThrowingIntExpression : ICombatExpression<Ctx, int>
    {
        public int Evaluate(EffectExecutionContext<Ctx> context, CombatState combat) =>
            throw new InvalidOperationException("boom");
    }

    private static EffectExecutionContext<Ctx> MakeContext(CombatState combat) =>
        new(new Ctx(),
            new TriggeredEffectActionBuildContext(
                new CombatantTargetSelectionContext(
                    Combat: combat,
                    Source: combat.GetCombatant(HeroId),
                    EventTargetId: null),
                new TriggeredEffectActionSource(SourceCombatantId: HeroId)));

    private sealed record Ctx;
}
