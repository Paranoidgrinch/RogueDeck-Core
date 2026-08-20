using RogueDeck.Core.Combat;

namespace RogueDeck.Run.Tests;

// A resource change can be a SPREAD instead of one number: a procedural act's fights pay 25–40 gold rather
// than the same baked amount every time. The roll comes from the run's own RNG, so a seed still replays.
public class ResourceSpreadTests
{
    private static RunState Run(int seed) =>
        new(new RunId("run"), new HealthState(30, 30),
            new RunMap([new Node(new NodeId("only"), StandardRunIds.EventNode, new EventRef(new EventId("none")))]), seed);

    [Fact]
    public void A_fixed_change_is_still_exact()
    {
        var run = Run(1);
        run.EnqueueEffect(new ChangeResourceRunEffect(StandardRunIds.Gold, 30));
        Resolve(run);
        Assert.Equal(30, run.GetResource(StandardRunIds.Gold));
    }

    [Fact]
    public void A_spread_lands_inside_its_range_and_replays_from_the_seed()
    {
        var first = Payout(seed: 4);
        var second = Payout(seed: 4);
        Assert.Equal(first, second);
        Assert.All(first, gold => Assert.InRange(gold, 25, 40));

        // Over enough fights the amount actually varies — a spread that never spreads is just a number.
        Assert.True(first.Distinct().Count() > 1, "the payouts should not all be the same");
    }

    private static List<int> Payout(int seed)
    {
        var run = Run(seed);
        var payouts = new List<int>();
        var previous = 0;
        for (var i = 0; i < 12; i++)
        {
            run.EnqueueEffect(new ChangeResourceRunEffect(StandardRunIds.Gold, 25, 40));
            Resolve(run);
            var now = run.GetResource(StandardRunIds.Gold);
            payouts.Add(now - previous);
            previous = now;
        }
        return payouts;
    }

    private static void Resolve(RunState run)
    {
        var registry = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(registry);
        new RunEffectProcessor().ResolvePending(run, registry.Build());
    }
}
