using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Tests for RunEvalContext + event-reading expressions: expressions can read the triggering event when one
// is in scope, and a relic authored purely as expression data (RelicPrograms.GainResourceOn + EventValue)
// computes its reaction from that event.
public class RunEvalContextTests
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

    [Fact]
    public void RunState_converts_to_a_no_event_context_implicitly()
    {
        var run = NewRun();
        run.SetResource(Gold, 6);
        // A bare RunState still evaluates value/condition expressions (Event is null).
        Assert.Equal(6, RunExpr.Resource(Gold).Evaluate(run));
        Assert.True(RunExpr.HasResource(Gold, 5).Evaluate(run));
    }

    [Fact]
    public void EventValue_reads_the_event_when_present()
    {
        var run = NewRun();
        var evt = new CombatResolvedRunEvent(new NodeId("n"), CombatResult.Victory, HeroHpRemaining: 22, DamageTaken: 8);
        var context = new RunEvalContext(run, evt);

        var expr = RunExpr.EventValue<CombatResolvedRunEvent>(e => e.DamageTaken);
        Assert.Equal(8, expr.Evaluate(context));

        // Composes with other value expressions in the same context.
        var doubled = RunExpr.Multiply(expr, RunExpr.Const(2));
        Assert.Equal(16, doubled.Evaluate(context));
    }

    [Fact]
    public void EventValue_throws_without_a_matching_event()
    {
        var run = NewRun();
        var expr = RunExpr.EventValue<CombatResolvedRunEvent>(e => e.DamageTaken);

        // No event at all.
        Assert.Throws<InvalidOperationException>(() => expr.Evaluate(run));

        // A different event type in context.
        var wrong = new RunEvalContext(run, new RunStartedRunEvent(run.Id));
        Assert.Throws<InvalidOperationException>(() => expr.Evaluate(wrong));
    }

    [Fact]
    public void Leech_relic_gains_gold_equal_to_damage_taken()
    {
        var registry = BuildRegistry();
        var processor = new RunEffectProcessor();
        var run = NewRun();
        run.AddRelic(new RelicInstance(StandardRelics.Leech()));

        // Simulate a resolved fight in which the hero lost 7 HP; the relic reacts via the event bus.
        run.RaiseEvent(new CombatResolvedRunEvent(new NodeId("fight"), CombatResult.Victory, HeroHpRemaining: 23, DamageTaken: 7));
        processor.ResolvePending(run, registry);

        Assert.Equal(7, run.GetResource(Gold));
    }

    [Fact]
    public void Leech_relic_does_nothing_when_no_damage_was_taken()
    {
        var registry = BuildRegistry();
        var processor = new RunEffectProcessor();
        var run = NewRun();
        run.AddRelic(new RelicInstance(StandardRelics.Leech()));

        run.RaiseEvent(new CombatResolvedRunEvent(new NodeId("fight"), CombatResult.Victory, HeroHpRemaining: 30, DamageTaken: 0));
        processor.ResolvePending(run, registry);

        Assert.Equal(0, run.GetResource(Gold));
        // A zero amount enqueues no effect at all — no spurious resource-changed event.
        Assert.DoesNotContain(run.EventHistory, e => e is ResourceChangedRunEvent);
    }
}
