using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Tests for the reward system (Phase H1): offering a reward generates offers, the player picks, and the
// chosen offers' grants apply. Covers chooser pick, no-chooser fallback, pool generation, and guaranteed
// (pick-all) rewards.
public class RewardTests
{
    private sealed class FirstNChooser : IRunEntityChooser
    {
        public IReadOnlyList<T> ChooseEntities<T>(IReadOnlyList<T> candidates, int count, string purpose) =>
            candidates.Take(count).ToArray();
    }

    private static readonly RewardId ChestReward = new("chest");
    private static readonly RunResourceId Gold = StandardRunIds.Gold;

    private static RunDefinitionRegistry BuildRegistry()
    {
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        return builder.Build();
    }

    private static RunState NewRun(int seed = 1)
    {
        var map = new RunMap(Array.Empty<Node>());
        return new RunState(new RunId("run"), new HealthState(30, 40), map, randomSeed: seed);
    }

    private static void Drain(RunState run, RunDefinitionRegistry registry) =>
        new RunEffectProcessor().ResolvePending(run, registry);

    private static IEnumerable<string> Kinds(RunState run) => run.Deck.Select(c => c.DefinitionId.ToString());

    private static readonly RewardOffer[] CardOffers =
    {
        Rewards.Card(new CardDefinitionId("strike")),
        Rewards.Card(new CardDefinitionId("shield")),
        Rewards.Card(new CardDefinitionId("blast")),
    };

    [Fact]
    public void OfferReward_grants_the_players_pick()
    {
        var registry = BuildRegistry();
        var run = NewRun();
        run.SetEntityChooser(new FirstNChooser());

        run.EnqueueEffect(new OfferRewardRunEffect(ChestReward, CardOffers, pickCount: 1));
        Drain(run, registry);

        Assert.Equal(new[] { "strike" }, Kinds(run)); // first-N chooser picks the first offer
        Assert.Single(run.EventHistory.OfType<RewardOfferedRunEvent>());
        Assert.Single(run.EventHistory.OfType<RewardChosenRunEvent>());
    }

    [Fact]
    public void Without_a_chooser_the_first_offers_are_taken()
    {
        var registry = BuildRegistry();
        var run = NewRun(); // no chooser set

        run.EnqueueEffect(new OfferRewardRunEffect(ChestReward, CardOffers, pickCount: 1));
        Drain(run, registry);

        Assert.Equal(new[] { "strike" }, Kinds(run));
    }

    [Fact]
    public void PickCount_beyond_offers_grants_all_a_guaranteed_reward()
    {
        var registry = BuildRegistry();
        var run = NewRun();
        run.SetEntityChooser(new FirstNChooser());

        run.EnqueueEffect(new OfferRewardRunEffect(ChestReward, CardOffers, pickCount: 99));
        Drain(run, registry);

        Assert.Equal(new[] { "strike", "shield", "blast" }, Kinds(run));
    }

    [Fact]
    public void Gold_offer_grants_gold()
    {
        var registry = BuildRegistry();
        var run = NewRun();
        run.SetEntityChooser(new FirstNChooser());

        run.EnqueueEffect(new OfferRewardRunEffect(
            ChestReward, new[] { Rewards.Gold(50), Rewards.Gold(10) }, pickCount: 1));
        Drain(run, registry);

        Assert.Equal(50, run.GetResource(Gold));
    }

    [Fact]
    public void FromPool_generates_distinct_offers_at_resolve_time()
    {
        var registry = BuildRegistry();
        var run = NewRun(seed: 7);
        run.SetEntityChooser(new FirstNChooser());

        var pool = RunPool.Uniform(CardOffers);
        // Offer 2 distinct card offers drawn from the pool; take both.
        run.EnqueueEffect(new OfferRewardRunEffect(ChestReward, RewardTable.FromPool(pool, 2), 2));
        Drain(run, registry);

        Assert.Equal(2, run.Deck.Count);
        Assert.Equal(2, run.Deck.Select(c => c.DefinitionId).Distinct().Count()); // distinct kinds
    }

    [Fact]
    public void Builder_sugar_offers_a_reward()
    {
        var registry = BuildRegistry();
        var processor = new RunEffectProcessor();
        var run = NewRun();
        run.SetEntityChooser(new FirstNChooser());

        var script = new EventScriptBuilder("chest")
            .Situation("chest", "t", s => s
                .Choice("open", c => c.OfferReward(ChestReward, CardOffers, pickCount: 1)))
            .Build();

        var node = new Node(new NodeId("n"), StandardRunIds.EventNode, script);
        var context = new NodeResolveContext(run, new ScriptedChoiceProvider("open"), registry, processor);
        new EventNodeResolver().Resolve(context, node);
        processor.ResolvePending(run, registry);

        Assert.Equal(new[] { "strike" }, Kinds(run));
    }
}
