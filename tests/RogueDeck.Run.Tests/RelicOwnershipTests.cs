using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// A relic is carried once (playtest 2026-09-27): nothing that offers or grants a relic may hand over one the run
// already wears — not a treasure's pool, not a fixed offer, not the add itself.
public class RelicOwnershipTests
{
    private static readonly RelicDefinition Idol = new(new RelicId("idol"), "Idol");
    private static readonly RelicDefinition Charm = new(new RelicId("charm"), "Charm");

    private static RewardOffer Grants(RelicDefinition relic) =>
        new(relic.Id.Value, [new AddRelicRunEffect(new RelicInstance(relic))], RewardKinds.Relic);

    [Fact]
    public void A_pool_never_draws_a_relic_the_run_already_carries()
    {
        // Every seed: the pool holds the carried Idol and the Charm, and only the Charm may come out.
        for (var seed = 1; seed <= 40; seed++)
        {
            var run = Wearing(Idol, seed);
            var offered = Offer(run, new PoolRewardSource(RunPool.Uniform(Grants(Idol), Grants(Charm)), 1));
            Assert.Equal(["charm"], offered);
        }
    }

    [Fact]
    public void A_pool_keeps_its_size_while_it_has_uncarried_relics_to_fill_it()
    {
        var third = new RelicDefinition(new RelicId("third"), "Third");
        for (var seed = 1; seed <= 40; seed++)
        {
            var run = Wearing(Idol, seed);
            var offered = Offer(run, new PoolRewardSource(RunPool.Uniform(Grants(Idol), Grants(Charm), Grants(third)), 2));
            Assert.Equal(["charm", "third"], offered.Order());
        }
    }

    [Fact]
    public void A_fixed_offer_of_a_carried_relic_is_taken_off_the_table()
    {
        var run = Wearing(Idol, seed: 1);
        var offered = Offer(run, new FixedRewardSource([Grants(Idol), Grants(Charm)]));
        Assert.Equal(["charm"], offered);
    }

    [Fact]
    public void Granting_a_carried_relic_directly_does_not_add_a_second_one()
    {
        var run = Wearing(Idol, seed: 1);
        run.EnqueueEffect(new AddRelicRunEffect(new RelicInstance(Idol)));
        new RunEffectProcessor().ResolvePending(run, Registry());
        Assert.Single(run.Relics, r => r.Id == Idol.Id);
    }

    // ── harness ──────────────────────────────────────────────────────────────────────────────────────────

    private static RunState Wearing(RelicDefinition relic, int seed)
    {
        var run = new RunState(new RunId("run"), new HealthState(30, 40), new RunMap(Array.Empty<Node>()), randomSeed: seed);
        run.AddRelic(new RelicInstance(relic));
        return run;
    }

    private static RunDefinitionRegistry Registry()
    {
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        return builder.Build();
    }

    // What reached the table, by offer id.
    private static List<string> Offer(RunState run, IRewardSource source)
    {
        var seen = new SeesAndTakesNothing();
        run.SetEntityChooser(seen);
        run.EnqueueEffect(new OfferRewardRunEffect(new RewardId("reward"), source, 1));
        new RunEffectProcessor().ResolvePending(run, Registry());
        return seen.Offered;
    }

    private sealed class SeesAndTakesNothing : IRunEntityChooser
    {
        public List<string> Offered { get; } = [];

        public IReadOnlyList<T> ChooseEntities<T>(IReadOnlyList<T> candidates, int count, string purpose)
        {
            Offered.AddRange(candidates.OfType<RewardOffer>().Select(o => o.Id));
            return [];
        }
    }
}
