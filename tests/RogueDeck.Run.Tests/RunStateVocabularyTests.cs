using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Tests for the flag/counter state vocabulary: the effects mutate and raise events (only on real change),
// and the expressions read them back — the memory layer that condition-driven events build on.
public class RunStateVocabularyTests
{
    private static readonly RunFlagId StoleFromMerchant = new("stole-from-merchant");
    private static readonly RunCounterId Debt = new("debt");

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

    [Fact]
    public void SetFlag_sets_reads_back_and_raises_only_on_change()
    {
        var registry = BuildRegistry();
        var run = NewRun();

        Assert.False(RunExpr.Flag(StoleFromMerchant).Evaluate(run));

        run.EnqueueEffect(new SetFlagRunEffect(StoleFromMerchant));
        run.EnqueueEffect(new SetFlagRunEffect(StoleFromMerchant)); // no-op, already set
        Drain(run, registry);

        Assert.True(RunExpr.Flag(StoleFromMerchant).Evaluate(run));
        Assert.Single(run.EventHistory.OfType<RunFlagChangedRunEvent>()); // only the real change raised
    }

    [Fact]
    public void UnsetFlag_clears_it()
    {
        var registry = BuildRegistry();
        var run = NewRun();
        run.SetFlag(StoleFromMerchant, true);

        run.EnqueueEffect(new SetFlagRunEffect(StoleFromMerchant, false));
        Drain(run, registry);

        Assert.False(run.HasFlag(StoleFromMerchant));
    }

    [Fact]
    public void Counter_defaults_to_zero_and_increments()
    {
        var registry = BuildRegistry();
        var run = NewRun();

        Assert.Equal(0, RunExpr.Counter(Debt).Evaluate(run));

        run.EnqueueEffect(new IncrementCounterRunEffect(Debt, 5));
        run.EnqueueEffect(new IncrementCounterRunEffect(Debt, 3));
        Drain(run, registry);

        Assert.Equal(8, RunExpr.Counter(Debt).Evaluate(run));
        Assert.Equal(2, run.EventHistory.OfType<RunCounterChangedRunEvent>().Count());
    }

    [Fact]
    public void SetCounter_overwrites_and_zero_delta_increment_is_a_no_op()
    {
        var registry = BuildRegistry();
        var run = NewRun();
        run.SetCounter(Debt, 4);

        run.EnqueueEffect(new IncrementCounterRunEffect(Debt, 0)); // no-op, no event
        run.EnqueueEffect(new SetCounterRunEffect(Debt, 10));
        Drain(run, registry);

        Assert.Equal(10, run.GetCounter(Debt));
        Assert.Single(run.EventHistory.OfType<RunCounterChangedRunEvent>()); // only the SetCounter change
    }

    [Fact]
    public void Counter_and_flag_drive_a_condition()
    {
        var run = NewRun();
        run.SetCounter(Debt, 12);
        run.SetFlag(StoleFromMerchant, true);

        // Debt >= 10 AND stole from the merchant.
        var collectorComes = RunExpr.And(
            RunExpr.GreaterOrEqual(RunExpr.Counter(Debt), RunExpr.Const(10)),
            RunExpr.Flag(StoleFromMerchant));

        Assert.True(collectorComes.Evaluate(run));
    }

    [Fact]
    public void Builder_sugar_sets_flag_and_increments_counter()
    {
        var registry = BuildRegistry();
        var processor = new RunEffectProcessor();
        var run = NewRun();

        var script = new EventScriptBuilder("s")
            .Situation("s", "t", s => s
                .Choice("steal", c => c
                    .SetFlag(StoleFromMerchant)
                    .IncrementCounter(Debt, 5)))
            .Build();

        var node = new Node(new NodeId("n"), StandardRunIds.EventNode, script);
        var context = new NodeResolveContext(run, new ScriptedChoiceProvider("steal"), registry, processor);
        new EventNodeResolver().Resolve(context, node);
        processor.ResolvePending(run, registry);

        Assert.True(run.HasFlag(StoleFromMerchant));
        Assert.Equal(5, run.GetCounter(Debt));
    }
}
