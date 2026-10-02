using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Playtest feedback 2, C4: a card reward can turn up already improved, at a chance the source states.
public class RewardUpgradeChanceTests
{
    private static PoolRewardSource Cards(int chance) => new(new RunPool<RewardOffer>(
        Enumerable.Range(0, 10).Select(i => new RunPool<RewardOffer>.Entry(
            new RewardOffer($"card-c{i}", [new AddCardToDeckRunEffect(new CardDefinitionId($"c{i}"))]), 1)).ToList()), 3)
    {
        UpgradeChancePercent = chance,
    };

    private static RunState Run(int seed) =>
        new(new RunId("run"), new HealthState(30, 40), new RunMap(Array.Empty<Node>()), randomSeed: seed);

    [Theory]
    [InlineData(10)]
    [InlineData(40)]
    public void The_share_of_upgraded_offers_is_the_stated_chance(int chance)
    {
        var offers = Enumerable.Range(1, 2000).SelectMany(seed => Cards(chance).Generate(Run(seed))).ToList();
        var upgraded = offers.Count(o => o.Grant.OfType<AddCardToDeckRunEffect>().Single().UpgradeLevel == 1);
        var share = 100.0 * upgraded / offers.Count;
        Assert.InRange(share, chance - 2, chance + 2);
        Assert.All(offers.Where(o => o.Id.EndsWith('+')),
            o => Assert.Equal(1, o.Grant.OfType<AddCardToDeckRunEffect>().Single().UpgradeLevel));
    }

    [Fact]
    public void No_chance_rolls_nothing_and_writes_what_it_always_did()
    {
        var run = Run(7);
        Assert.All(Cards(0).Generate(run), o => Assert.Equal(0, o.Grant.OfType<AddCardToDeckRunEffect>().Single().UpgradeLevel));
        var options = RunJson.CreateOptions();
        Assert.DoesNotContain("UpgradeChancePercent", RunJson.ToJson<IRewardSource>(Cards(0), options));
        Assert.DoesNotContain("UpgradeLevel", RunJson.ToJson<IRunEffectRequest>(new AddCardToDeckRunEffect(new CardDefinitionId("x")), options));
        var back = RunJson.FromJson<IRewardSource>(RunJson.ToJson<IRewardSource>(Cards(30), options), options);
        Assert.Equal(30, Assert.IsType<PoolRewardSource>(back).UpgradeChancePercent);
    }

    [Fact]
    public void An_upgraded_offer_adds_an_upgraded_card()
    {
        var run = Run(1);
        run.EnqueueEffect(new AddCardToDeckRunEffect(new CardDefinitionId("x"), 1));
        new RunEffectProcessor().ResolvePending(run, Registry());
        Assert.Equal(1, Assert.Single(run.Deck).UpgradeLevel);
    }

    private static RunDefinitionRegistry Registry()
    {
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        return builder.Build();
    }
}
