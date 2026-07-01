using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Tests for the Phase E vocabulary expansion: Divide/Abs/Negate expressions, computed Heal/Damage, and the
// Repeat effect that enqueues a block a computed number of times.
public class RunVocabularyExpansionTests
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

    private static void Drain(RunState run, RunDefinitionRegistry registry) =>
        new RunEffectProcessor().ResolvePending(run, registry);

    [Fact]
    public void Divide_truncates_and_rejects_zero_divisor()
    {
        var run = NewRun();
        Assert.Equal(3, RunExpr.Divide(RunExpr.Const(7), RunExpr.Const(2)).Evaluate(run));
        Assert.Throws<InvalidOperationException>(() =>
            RunExpr.Divide(RunExpr.Const(1), RunExpr.Const(0)).Evaluate(run));
    }

    [Fact]
    public void Abs_and_Negate()
    {
        var run = NewRun();
        Assert.Equal(5, RunExpr.Abs(RunExpr.Subtract(RunExpr.Const(2), RunExpr.Const(7))).Evaluate(run));
        Assert.Equal(-4, RunExpr.Negate(RunExpr.Const(4)).Evaluate(run));
    }

    [Fact]
    public void Computed_heal_and_damage_use_state()
    {
        var registry = BuildRegistry();
        var run = NewRun(current: 25, max: 40); // missing 15

        // Heal half the missing health (15 / 2 = 7).
        run.EnqueueEffect(new ComputedHealRunEffect(RunExpr.Divide(RunExpr.MissingHealth, RunExpr.Const(2))));
        Drain(run, registry);
        Assert.Equal(32, run.Health.Current);

        // Damage equal to current gold.
        run.SetResource(Gold, 10);
        run.EnqueueEffect(new ComputedDamageRunEffect(RunExpr.Resource(Gold)));
        Drain(run, registry);
        Assert.Equal(22, run.Health.Current);
    }

    [Fact]
    public void Repeat_enqueues_the_block_count_times()
    {
        var registry = BuildRegistry();
        var run = NewRun();

        run.EnqueueEffect(new RepeatRunEffect(RunExpr.Const(3), new IRunEffectRequest[]
        {
            new ChangeResourceRunEffect(Gold, 2),
        }));
        Drain(run, registry);
        Assert.Equal(6, run.GetResource(Gold));
    }

    [Fact]
    public void Repeat_with_non_positive_count_does_nothing()
    {
        var registry = BuildRegistry();
        var run = NewRun();
        run.EnqueueEffect(new RepeatRunEffect(RunExpr.Const(0), new IRunEffectRequest[]
        {
            new ChangeResourceRunEffect(Gold, 5),
        }));
        Drain(run, registry);
        Assert.Equal(0, run.GetResource(Gold));
    }

    [Fact]
    public void Repeat_count_is_evaluated_from_state()
    {
        var registry = BuildRegistry();
        var run = NewRun();
        run.AddDeckCard(new CardDefinitionId("a"));
        run.AddDeckCard(new CardDefinitionId("b"));

        // Gain 10 gold per card in the deck.
        run.EnqueueEffect(new RepeatRunEffect(RunExpr.DeckSize, new IRunEffectRequest[]
        {
            new ChangeResourceRunEffect(Gold, 10),
        }));
        Drain(run, registry);
        Assert.Equal(20, run.GetResource(Gold));
    }
}
