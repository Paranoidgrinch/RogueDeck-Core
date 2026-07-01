using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Tests for reward sources as data (R6) and the catalog-gap effects: max-HP change and relic removal.
public class RewardTableAndCatalogTests
{
    private sealed class FirstNChooser : IRunEntityChooser
    {
        public IReadOnlyList<T> ChooseEntities<T>(IReadOnlyList<T> candidates, int count, string purpose) =>
            candidates.Take(count).ToArray();
    }

    private static readonly RewardId Chest = new("chest");
    private static readonly RunResourceId Gold = StandardRunIds.Gold;

    private static RunDefinitionRegistry BuildRegistry()
    {
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        return builder.Build();
    }

    private static RunState NewRun(int current = 25, int max = 40, int seed = 1)
    {
        var map = new RunMap(Array.Empty<Node>());
        return new RunState(new RunId("run"), new HealthState(current, max), map, randomSeed: seed);
    }

    private static void Drain(RunState run, RunDefinitionRegistry registry) =>
        new RunEffectProcessor().ResolvePending(run, registry);

    [Fact]
    public void RewardTable_FromPool_generates_distinct_offers()
    {
        var registry = BuildRegistry();
        var run = NewRun(seed: 5);
        run.SetEntityChooser(new FirstNChooser());

        var pool = RunPool.Uniform(
            Rewards.Card(new CardDefinitionId("a")),
            Rewards.Card(new CardDefinitionId("b")),
            Rewards.Card(new CardDefinitionId("c")));
        run.EnqueueEffect(new OfferRewardRunEffect(Chest, RewardTable.FromPool(pool, 2), 2));
        Drain(run, registry);

        Assert.Equal(2, run.Deck.Count);
        Assert.Equal(2, run.Deck.Select(c => c.DefinitionId).Distinct().Count());
    }

    [Fact]
    public void RewardTable_Of_offers_a_fixed_set()
    {
        var registry = BuildRegistry();
        var run = NewRun();
        run.SetEntityChooser(new FirstNChooser());

        run.EnqueueEffect(new OfferRewardRunEffect(
            Chest, RewardTable.Of(Rewards.Gold(30), Rewards.Gold(5)), 1));
        Drain(run, registry);

        Assert.Equal(30, run.GetResource(Gold));
    }

    [Fact]
    public void GainMaxHealth_raises_max_and_heals()
    {
        var registry = BuildRegistry();
        var run = NewRun(current: 25, max: 40);

        run.EnqueueEffect(new ChangeMaxHealthRunEffect(10));
        Drain(run, registry);

        Assert.Equal(50, run.Health.Max);
        Assert.Equal(35, run.Health.Current); // gained max also healed by 10
        Assert.Single(run.EventHistory.OfType<RunMaxHealthChangedRunEvent>());
    }

    [Fact]
    public void LoseMaxHealth_lowers_max_and_caps_current()
    {
        var registry = BuildRegistry();
        var run = NewRun(current: 38, max: 40);

        run.EnqueueEffect(new ChangeMaxHealthRunEffect(-30)); // max 40 -> 10
        Drain(run, registry);

        Assert.Equal(10, run.Health.Max);
        Assert.Equal(10, run.Health.Current); // capped down from 38
    }

    [Fact]
    public void Max_health_never_drops_below_one()
    {
        var registry = BuildRegistry();
        var run = NewRun(current: 5, max: 5);

        run.EnqueueEffect(new ChangeMaxHealthRunEffect(-100));
        Drain(run, registry);

        Assert.Equal(1, run.Health.Max);
    }

    [Fact]
    public void RemoveRelic_removes_and_raises()
    {
        var registry = BuildRegistry();
        var run = NewRun();
        run.AddRelic(new RelicInstance(StandardRelics.Bloodstone()));
        run.AddRelic(new RelicInstance(StandardRelics.Leech()));

        run.EnqueueEffect(new RemoveRelicRunEffect(new RelicId("bloodstone")));
        Drain(run, registry);

        Assert.DoesNotContain(run.Relics, r => r.Id == new RelicId("bloodstone"));
        Assert.Contains(run.Relics, r => r.Id == new RelicId("leech"));
        Assert.Single(run.EventHistory.OfType<RelicRemovedRunEvent>());
    }
}
