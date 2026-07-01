using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Tests for weighted draw-without-replacement: RunPool<T>.DrawMany and the DrawManyEffectsRunEffect that
// enqueues N distinct bundles ("pick N different rewards"). The core property is distinctness plus the same
// seed-driven determinism as single draws.
public class RunPoolDrawManyTests
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

    [Fact]
    public void DrawMany_returns_distinct_entries_in_bounds()
    {
        var pool = RunPool.Uniform("a", "b", "c", "d", "e");

        for (var seed = 0; seed < 30; seed++)
        {
            var run = NewRun(seed);
            var drawn = pool.DrawMany(run, 3);
            Assert.Equal(3, drawn.Count);
            Assert.Equal(3, drawn.Distinct().Count()); // no repeats
            Assert.All(drawn, v => Assert.Contains(v, new[] { "a", "b", "c", "d", "e" }));
        }
    }

    [Fact]
    public void DrawMany_zero_is_empty_and_full_count_is_a_permutation()
    {
        var pool = RunPool.Uniform(1, 2, 3);
        var run = NewRun(seed: 4);

        Assert.Empty(pool.DrawMany(run, 0));

        var all = pool.DrawMany(run, 3);
        Assert.Equal(new[] { 1, 2, 3 }, all.OrderBy(x => x).ToArray()); // every entry exactly once
    }

    [Fact]
    public void DrawMany_rejects_count_over_pool_size()
    {
        var pool = RunPool.Uniform(1, 2);
        var run = NewRun(seed: 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => pool.DrawMany(run, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => pool.DrawMany(run, -1));
    }

    [Fact]
    public void DrawMany_is_reproducible_for_a_seed()
    {
        var pool = RunPool.Weighted(("a", 5), ("b", 3), ("c", 2), ("d", 1));
        var seqA = pool.DrawMany(NewRun(seed: 77), 3);
        var seqB = pool.DrawMany(NewRun(seed: 77), 3);
        Assert.Equal(seqA, seqB);
    }

    [Fact]
    public void DrawManyEffects_enqueues_all_drawn_bundles()
    {
        var registry = BuildRegistry();
        var processor = new RunEffectProcessor();
        var run = NewRun(seed: 9);

        // Four distinct gold rewards; draw 3 of them. Because they are distinct and additive, the total gold
        // is the sum of three different entries — i.e. total minus exactly one omitted entry.
        var pool = RunPool.Uniform<IReadOnlyList<IRunEffectRequest>>(
            new IRunEffectRequest[] { new ChangeResourceRunEffect(Gold, 1) },
            new IRunEffectRequest[] { new ChangeResourceRunEffect(Gold, 10) },
            new IRunEffectRequest[] { new ChangeResourceRunEffect(Gold, 100) },
            new IRunEffectRequest[] { new ChangeResourceRunEffect(Gold, 1000) });

        run.EnqueueEffect(new DrawManyEffectsRunEffect(pool, 3));
        processor.ResolvePending(run, registry);

        var total = run.GetResource(Gold);
        var omitted = 1111 - total; // exactly one of {1,10,100,1000} was left out
        Assert.Contains(omitted, new[] { 1, 10, 100, 1000 });
    }
}
