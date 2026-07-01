using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Tests for choice costs (Phase D): a cost gates a choice (unaffordable choices are not offered) and is paid
// before the choice's effects. Covers resource price, HP price (survive-only), and a custom expression cost.
public class EventCostTests
{
    private static readonly RunResourceId Gold = StandardRunIds.Gold;
    private static readonly RunFlagId GotRelic = new("got-relic");

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

    // Resolve one situation, choosing by the given priority order (first available match wins).
    private static string Play(RunState run, EventScript script, params string[] choicePriority)
    {
        var registry = BuildRegistry();
        var processor = new RunEffectProcessor();
        var node = new Node(new NodeId("n"), StandardRunIds.EventNode, script);
        var context = new NodeResolveContext(run, new ScriptedChoiceProvider(choicePriority), registry, processor);
        var outcome = new EventNodeResolver().Resolve(context, node);
        processor.ResolvePending(run, registry);
        return outcome.Summary;
    }

    private static EventScript AltarScript() =>
        new EventScriptBuilder("altar")
            .Situation("altar", "t", s => s
                .Choice("blood", c => c.PayHealth(6).SetFlag(GotRelic))
                .Choice("gold", c => c.PayResource(Gold, 75).SetFlag(GotRelic))
                .Choice("leave", _ => { }))
            .Build();

    [Fact]
    public void PayResource_deducts_only_when_affordable()
    {
        var run = NewRun();
        run.SetResource(Gold, 100);

        Play(run, AltarScript(), "gold");
        Assert.Equal(25, run.GetResource(Gold)); // 100 - 75
        Assert.True(run.HasFlag(GotRelic));
    }

    [Fact]
    public void Unaffordable_resource_choice_is_not_offered()
    {
        var run = NewRun();
        run.SetResource(Gold, 40); // cannot afford 75

        // Ask for "gold" first, but it is filtered out; falls through to "leave".
        Play(run, AltarScript(), "gold", "leave");
        Assert.Equal(40, run.GetResource(Gold)); // untouched
        Assert.False(run.HasFlag(GotRelic));
    }

    [Fact]
    public void PayHealth_deducts_and_requires_survival()
    {
        var run = NewRun(current: 20, max: 40);
        Play(run, AltarScript(), "blood");
        Assert.Equal(14, run.Health.Current); // 20 - 6
        Assert.True(run.HasFlag(GotRelic));
    }

    [Fact]
    public void PayHealth_choice_is_hidden_when_it_would_be_lethal()
    {
        var run = NewRun(current: 6, max: 40); // cost 6 would drop to 0 — not allowed (must survive)
        Play(run, AltarScript(), "blood", "leave");
        Assert.Equal(6, run.Health.Current); // untouched
        Assert.False(run.HasFlag(GotRelic));
    }

    [Fact]
    public void Costs_are_paid_before_effects()
    {
        // A custom cost that spends gold, with an effect that reads gold: the effect must observe the
        // post-payment balance, proving pay runs first.
        var run = NewRun();
        run.SetResource(Gold, 100);

        var script = new EventScriptBuilder("s")
            .Situation("s", "t", s => s
                .Choice("deal", c => c
                    .Cost(RunExpr.HasResource(Gold, 30), new ChangeResourceRunEffect(Gold, -30))
                    // gain a second resource equal to remaining gold (should be 70, not 100)
                    .GainResource(new RunResourceId("tokens"), RunExpr.Resource(Gold))))
            .Build();

        Play(run, script, "deal");
        Assert.Equal(70, run.GetResource(Gold));
        Assert.Equal(70, run.GetResource(new RunResourceId("tokens")));
    }
}
