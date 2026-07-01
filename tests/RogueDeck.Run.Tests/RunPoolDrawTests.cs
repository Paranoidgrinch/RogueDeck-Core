using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Tests for the pool-draw slice: RunPool<T> weighted draws, the random value expressions
// (RandomRange / Pool), and the DrawEffectsRunEffect random-outcome effect. The theme is determinism —
// a run seed reproduces every draw — plus correct weight mapping and range bounds.
public class RunPoolDrawTests
{
    private static readonly RunResourceId Gold = StandardRunIds.Gold;

    private static RunDefinitionRegistry BuildRegistry()
    {
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        return builder.Build();
    }

    private static RunState NewRun(int seed, int current = 30, int max = 40)
    {
        var map = new RunMap(Array.Empty<Node>());
        return new RunState(new RunId("run"), new HealthState(current, max), map, randomSeed: seed);
    }

    // ── RunPool<T> ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Pool_construction_rejects_empty_and_bad_weights()
    {
        Assert.Throws<ArgumentException>(() => new RunPool<int>(Array.Empty<RunPool<int>.Entry>()));
        Assert.Throws<ArgumentException>(() => RunPool.Weighted((1, 0)));
        Assert.Throws<ArgumentException>(() => RunPool.Weighted((1, -3)));
    }

    [Fact]
    public void Single_entry_pool_always_returns_it()
    {
        var run = NewRun(seed: 7);
        var pool = RunPool.Uniform(42);
        for (var i = 0; i < 5; i++)
            Assert.Equal(42, pool.Draw(run));
    }

    [Fact]
    public void Draw_is_reproducible_for_a_seed_and_advances_the_rng()
    {
        var pool = RunPool.Uniform("a", "b", "c", "d");

        var runA = NewRun(seed: 123);
        var runB = NewRun(seed: 123);
        var seqA = new[] { pool.Draw(runA), pool.Draw(runA), pool.Draw(runA) };
        var seqB = new[] { pool.Draw(runB), pool.Draw(runB), pool.Draw(runB) };
        Assert.Equal(seqA, seqB);

        // A different seed should (very likely) diverge — pin that the seed actually drives the draw.
        var runC = NewRun(seed: 999);
        var seqC = new[] { pool.Draw(runC), pool.Draw(runC), pool.Draw(runC) };
        Assert.NotEqual(seqA, seqC);
    }

    [Fact]
    public void Weighted_draw_only_yields_declared_values_and_skews_to_weight()
    {
        // 'common' weight 9, 'rare' weight 1 — over many draws 'common' dominates and 'rare' still appears.
        var pool = RunPool.Weighted(("common", 9), ("rare", 1));
        var run = NewRun(seed: 5);

        var counts = new Dictionary<string, int> { ["common"] = 0, ["rare"] = 0 };
        for (var i = 0; i < 400; i++)
            counts[pool.Draw(run)]++;

        Assert.Equal(400, counts["common"] + counts["rare"]); // only declared values
        Assert.True(counts["common"] > counts["rare"], "the weight-9 entry should dominate");
        Assert.True(counts["rare"] > 0, "the weight-1 entry should still appear over 400 draws");
    }

    // ── Random value expressions ────────────────────────────────────────────────────

    [Fact]
    public void RandomRange_stays_within_inclusive_bounds()
    {
        var run = NewRun(seed: 11);
        var expr = RunExpr.RandomRange(3, 6);
        for (var i = 0; i < 200; i++)
        {
            var v = expr.Evaluate(run);
            Assert.InRange(v, 3, 6);
        }
    }

    [Fact]
    public void RandomRange_with_equal_bounds_is_constant_and_rejects_inverted()
    {
        var run = NewRun(seed: 1);
        Assert.Equal(5, RunExpr.RandomRange(5, 5).Evaluate(run));

        var inverted = RunExpr.RandomRange(RunExpr.Const(10), RunExpr.Const(2));
        Assert.Throws<InvalidOperationException>(() => inverted.Evaluate(run));
    }

    [Fact]
    public void Pool_value_expression_draws_declared_values()
    {
        var run = NewRun(seed: 2);
        var expr = RunExpr.Pool(RunPool.Weighted((10, 1), (20, 1), (30, 1)));
        for (var i = 0; i < 100; i++)
            Assert.Contains(expr.Evaluate(run), new[] { 10, 20, 30 });
    }

    // ── DrawEffectsRunEffect, drained through the processor ──────────────────────────

    [Fact]
    public void DrawEffects_enqueues_one_bundle_and_reproduces_by_seed()
    {
        var registry = BuildRegistry();
        var processor = new RunEffectProcessor();

        // Two mutually exclusive outcomes: gain 100 gold, or take 100 damage. Exactly one fires per draw, so
        // the result is unambiguous, and the same seed picks the same outcome.
        RunPool<IReadOnlyList<IRunEffectRequest>> pool = RunPool.Uniform<IReadOnlyList<IRunEffectRequest>>(
            new IRunEffectRequest[] { new ChangeResourceRunEffect(Gold, 100) },
            new IRunEffectRequest[] { new ApplyRunDamageRunEffect(100) });

        static (int gold, int hp) Play(RunPool<IReadOnlyList<IRunEffectRequest>> pool, RunEffectProcessor proc, RunDefinitionRegistry reg, int seed)
        {
            var run = NewRun(seed);
            run.EnqueueEffect(new DrawEffectsRunEffect(pool));
            proc.ResolvePending(run, reg);
            return (run.GetResource(Gold), run.Health.Current);
        }

        var first = Play(pool, processor, registry, seed: 42);
        var again = Play(pool, processor, registry, seed: 42);
        Assert.Equal(first, again); // deterministic by seed

        // Exactly one outcome happened: either full gold with no damage, or no gold with damage.
        var goldOutcome = first is (100, 30);
        var hurtOutcome = first is (0, < 30);
        Assert.True(goldOutcome ^ hurtOutcome, "exactly one bundle should have fired");
    }
}
