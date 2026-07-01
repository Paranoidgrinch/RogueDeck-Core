using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Tests for reward modifiers (Phase H2): they reshape a reward's offers before the player picks, and expire
// after affecting the configured number of rewards.
public class RewardModifierTests
{
    // A chooser that picks the offer(s) with the given ids, else the first.
    private sealed class PickByIdChooser : IRunEntityChooser
    {
        private readonly string[] _ids;
        public PickByIdChooser(params string[] ids) => _ids = ids;

        public IReadOnlyList<T> ChooseEntities<T>(IReadOnlyList<T> candidates, int count, string purpose)
        {
            var offers = candidates.Cast<RewardOffer>().ToList();
            var picked = _ids
                .Select(id => offers.FirstOrDefault(o => o.Id == id))
                .Where(o => o is not null)
                .Take(count)
                .ToList();
            if (picked.Count == 0)
                picked.Add(offers[0]);
            return picked.Cast<T>().ToArray();
        }
    }

    private static readonly RewardId Chest = new("chest");
    private static readonly RunResourceId Gold = StandardRunIds.Gold;

    private static RunDefinitionRegistry BuildRegistry()
    {
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        return builder.Build();
    }

    private static RunState NewRun()
    {
        var map = new RunMap(Array.Empty<Node>());
        return new RunState(new RunId("run"), new HealthState(30, 40), map);
    }

    private static void Drain(RunState run, RunDefinitionRegistry registry) =>
        new RunEffectProcessor().ResolvePending(run, registry);

    private static RewardOffer[] BaseOffers() => new[]
    {
        Rewards.Card(new CardDefinitionId("strike")),
        Rewards.Card(new CardDefinitionId("shield")),
    };

    [Fact]
    public void AddOffer_modifier_injects_an_extra_choice()
    {
        var registry = BuildRegistry();
        var run = NewRun();
        // Player will take the injected cursed card so we can observe it was offered.
        run.SetEntityChooser(new PickByIdChooser("curse"));

        run.AddRewardModifier(
            RewardModifiers.AddOffer(Rewards.Card(new CardDefinitionId("curse"), "curse")), rewardCount: 1);

        run.EnqueueEffect(new OfferRewardRunEffect(Chest, BaseOffers(), pickCount: 1));
        Drain(run, registry);

        Assert.Contains(run.Deck, c => c.DefinitionId == new CardDefinitionId("curse"));
        var offered = run.EventHistory.OfType<RewardOfferedRunEvent>().Single();
        Assert.Equal(3, offered.OfferIds.Count); // 2 base + 1 injected
    }

    [Fact]
    public void Modifier_expires_after_its_reward_count()
    {
        var registry = BuildRegistry();
        var run = NewRun();
        run.SetEntityChooser(new PickByIdChooser("curse"));

        // Affects only the next 1 reward.
        run.AddRewardModifier(
            RewardModifiers.AddOffer(Rewards.Card(new CardDefinitionId("curse"), "curse")), rewardCount: 1);
        Assert.Equal(1, run.ActiveRewardModifierCount);

        run.EnqueueEffect(new OfferRewardRunEffect(Chest, BaseOffers(), pickCount: 1));
        Drain(run, registry);
        Assert.Equal(0, run.ActiveRewardModifierCount); // expired

        // A second reward no longer has the injected offer (only the 2 base offers).
        run.EnqueueEffect(new OfferRewardRunEffect(Chest, BaseOffers(), pickCount: 1));
        Drain(run, registry);
        var second = run.EventHistory.OfType<RewardOfferedRunEvent>().Last();
        Assert.Equal(2, second.OfferIds.Count);
    }

    [Fact]
    public void Modifier_lasts_for_multiple_rewards()
    {
        var registry = BuildRegistry();
        var run = NewRun();

        run.AddRewardModifier(RewardModifiers.AddOffer(Rewards.Gold(1)), rewardCount: 2);

        for (var i = 0; i < 2; i++)
        {
            run.EnqueueEffect(new OfferRewardRunEffect(Chest, BaseOffers(), pickCount: 1));
            Drain(run, registry);
        }
        // Both rewards saw 3 offers; after two, the modifier is gone.
        Assert.All(run.EventHistory.OfType<RewardOfferedRunEvent>(), e => Assert.Equal(3, e.OfferIds.Count));
        Assert.Equal(0, run.ActiveRewardModifierCount);
    }

    [Fact]
    public void TransformEach_reshapes_every_offer()
    {
        var registry = BuildRegistry();
        var run = NewRun();
        run.SetEntityChooser(new PickByIdChooser("bonus-gold"));

        // Turn every offered card into a gold offer instead.
        run.AddRewardModifier(RewardModifiers.TransformEach(_ => Rewards.Gold(25) with { Id = "bonus-gold" }),
            rewardCount: 1);

        run.EnqueueEffect(new OfferRewardRunEffect(Chest, BaseOffers(), pickCount: 1));
        Drain(run, registry);

        Assert.Equal(25, run.GetResource(Gold));
        Assert.Empty(run.Deck); // no card was actually offered anymore
    }
}
