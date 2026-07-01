using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Tests for the composable run-effect programs: the expression vocabulary (RunExpressions) and the two
// program effects that consume it (ComputedResourceRunEffect / ConditionalRunEffect). The expression tests
// pin evaluation semantics; the effect tests pin the substrate property that matters — a program effect is
// an ordinary queued request that resolves into primitive effects through the same RunEffectProcessor.
public class RunEffectProgramTests
{
    private static readonly RunResourceId Gold = StandardRunIds.Gold;

    private static RunDefinitionRegistry BuildRegistry()
    {
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        return builder.Build();
    }

    private static RunState NewRun(int current = 30, int max = 40)
    {
        var map = new RunMap(Array.Empty<Node>());
        return new RunState(new RunId("run"), new HealthState(current, max), map);
    }

    // ── Value expressions ─────────────────────────────────────────────────────────

    [Fact]
    public void Value_leaves_read_run_state()
    {
        var run = NewRun(current: 30, max: 40);
        run.SetResource(Gold, 7);
        run.AddDeckCard(new CardDefinitionId("a"));
        run.AddDeckCard(new CardDefinitionId("b"));

        Assert.Equal(5, RunExpr.Const(5).Evaluate(run));
        Assert.Equal(7, RunExpr.Resource(Gold).Evaluate(run));
        Assert.Equal(30, RunExpr.CurrentHealth.Evaluate(run));
        Assert.Equal(40, RunExpr.MaxHealth.Evaluate(run));
        Assert.Equal(10, RunExpr.MissingHealth.Evaluate(run));
        Assert.Equal(2, RunExpr.DeckSize.Evaluate(run));
        Assert.Equal(0, RunExpr.RelicCount.Evaluate(run));
    }

    [Fact]
    public void Arithmetic_composes()
    {
        var run = NewRun(current: 30, max: 40);

        // (missing_health * 2) then min with 15, then max with 5.
        var expr = RunExpr.Max(
            RunExpr.Min(RunExpr.Multiply(RunExpr.MissingHealth, RunExpr.Const(2)), RunExpr.Const(15)),
            RunExpr.Const(5));

        Assert.Equal(15, expr.Evaluate(run)); // 10*2=20 -> min 15 -> max 5 = 15
        Assert.Equal(3, RunExpr.Subtract(RunExpr.Const(10), RunExpr.Const(7)).Evaluate(run));
    }

    [Fact]
    public void Clamp_bounds_the_value_and_rejects_inverted_range()
    {
        var run = NewRun();
        Assert.Equal(10, RunExpr.Clamp(RunExpr.Const(25), RunExpr.Const(0), RunExpr.Const(10)).Evaluate(run));
        Assert.Equal(0, RunExpr.Clamp(RunExpr.Const(-5), RunExpr.Const(0), RunExpr.Const(10)).Evaluate(run));

        var inverted = RunExpr.Clamp(RunExpr.Const(5), RunExpr.Const(10), RunExpr.Const(0));
        Assert.Throws<InvalidOperationException>(() => inverted.Evaluate(run));
    }

    // ── Condition expressions ──────────────────────────────────────────────────────

    [Fact]
    public void Comparisons_and_boolean_logic()
    {
        var run = NewRun(current: 30, max: 40);
        run.SetResource(Gold, 5);

        Assert.True(RunExpr.GreaterOrEqual(RunExpr.Resource(Gold), RunExpr.Const(5)).Evaluate(run));
        Assert.False(RunExpr.GreaterThan(RunExpr.Resource(Gold), RunExpr.Const(5)).Evaluate(run));
        Assert.True(RunExpr.LessThan(RunExpr.CurrentHealth, RunExpr.MaxHealth).Evaluate(run));

        var hurtAndRich = RunExpr.And(
            RunExpr.LessThan(RunExpr.CurrentHealth, RunExpr.MaxHealth),
            RunExpr.HasResource(Gold, 5));
        Assert.True(hurtAndRich.Evaluate(run));

        Assert.False(RunExpr.Not(hurtAndRich).Evaluate(run));
        Assert.True(RunExpr.Or(RunExpr.False, RunExpr.HasResource(Gold, 3)).Evaluate(run));
    }

    // ── Program effects, drained through the processor ─────────────────────────────

    [Fact]
    public void ComputedResourceRunEffect_gains_amount_from_state()
    {
        var registry = BuildRegistry();
        var processor = new RunEffectProcessor();
        var run = NewRun(current: 30, max: 40); // missing 10

        // Gain gold equal to missing health.
        run.EnqueueEffect(new ComputedResourceRunEffect(Gold, RunExpr.MissingHealth));
        processor.ResolvePending(run, registry);

        Assert.Equal(10, run.GetResource(Gold));
        // It routed through the primitive ChangeResourceRunEffect, so the change was logged + raised.
        Assert.Contains(run.EventHistory, e => e is ResourceChangedRunEvent);
    }

    [Fact]
    public void ConditionalRunEffect_takes_the_true_branch()
    {
        var registry = BuildRegistry();
        var processor = new RunEffectProcessor();
        var run = NewRun(current: 10, max: 40); // below half

        // If below half health (current*2 < max), heal 8; otherwise gain 5 gold.
        var effect = new ConditionalRunEffect(
            RunExpr.LessThan(RunExpr.Multiply(RunExpr.CurrentHealth, RunExpr.Const(2)), RunExpr.MaxHealth),
            new IRunEffectRequest[] { new HealRunEffect(8) },
            new IRunEffectRequest[] { new ChangeResourceRunEffect(Gold, 5) });

        run.EnqueueEffect(effect);
        processor.ResolvePending(run, registry);

        Assert.Equal(18, run.Health.Current);
        Assert.Equal(0, run.GetResource(Gold));
    }

    [Fact]
    public void ConditionalRunEffect_takes_the_false_branch()
    {
        var registry = BuildRegistry();
        var processor = new RunEffectProcessor();
        var run = NewRun(current: 40, max: 40); // full health

        var effect = new ConditionalRunEffect(
            RunExpr.LessThan(RunExpr.CurrentHealth, RunExpr.MaxHealth),
            new IRunEffectRequest[] { new HealRunEffect(8) },
            new IRunEffectRequest[] { new ChangeResourceRunEffect(Gold, 5) });

        run.EnqueueEffect(effect);
        processor.ResolvePending(run, registry);

        Assert.Equal(40, run.Health.Current);
        Assert.Equal(5, run.GetResource(Gold));
    }

    [Fact]
    public void Nested_program_effects_resolve_transitively()
    {
        var registry = BuildRegistry();
        var processor = new RunEffectProcessor();
        var run = NewRun(current: 25, max: 40); // missing 15

        // A conditional whose true branch is itself a computed effect: prove program effects nest and the
        // processor drives them all to a fixed point.
        var effect = new ConditionalRunEffect(
            RunExpr.HasResource(Gold, 0),
            new IRunEffectRequest[]
            {
                new ComputedResourceRunEffect(Gold, RunExpr.Min(RunExpr.MissingHealth, RunExpr.Const(10))),
            });

        run.EnqueueEffect(effect);
        processor.ResolvePending(run, registry);

        Assert.Equal(10, run.GetResource(Gold)); // min(15,10)
    }
}
