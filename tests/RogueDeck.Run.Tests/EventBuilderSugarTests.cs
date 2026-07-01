using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Tests for the ChoiceBuilder effect sugar. The sugar adds no semantics — it just appends the same effects —
// so these run a one-situation event end to end through the resolver and assert the resulting run state.
public class EventBuilderSugarTests
{
    private static readonly RunResourceId Gold = StandardRunIds.Gold;

    private static RunDefinitionRegistry BuildRegistry()
    {
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        return builder.Build();
    }

    private static RunState NewRun(int seed = 1, int current = 30, int max = 40)
    {
        var map = new RunMap(Array.Empty<Node>());
        return new RunState(new RunId("run"), new HealthState(current, max), map, randomSeed: seed);
    }

    // Resolve a single-situation event with the given choice id, draining effects afterwards.
    private static void Play(RunState run, EventScript script, string choiceId)
    {
        var registry = BuildRegistry();
        var processor = new RunEffectProcessor();
        var resolver = new EventNodeResolver();
        var node = new Node(new NodeId("n"), StandardRunIds.EventNode, script);
        var context = new NodeResolveContext(run, new ScriptedChoiceProvider(choiceId), registry, processor);
        resolver.Resolve(context, node);
        processor.ResolvePending(run, registry);
    }

    [Fact]
    public void GainResource_and_SpendResource()
    {
        var run = NewRun();
        run.SetResource(Gold, 10);

        var script = new EventScriptBuilder("s")
            .Situation("s", "t", s => s
                .Choice("gain", c => c.GainResource(Gold, 5))
                .Choice("spend", c => c.SpendResource(Gold, 4)))
            .Build();

        Play(run, script, "gain");
        Assert.Equal(15, run.GetResource(Gold));
    }

    [Fact]
    public void SpendResource_rejects_negative_amount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new EventScriptBuilder("s")
                .Situation("s", "t", s => s.Choice("x", c => c.SpendResource(Gold, -1))));
    }

    [Fact]
    public void Heal_and_Damage()
    {
        var healRun = NewRun(current: 20, max: 40);
        var healScript = new EventScriptBuilder("s")
            .Situation("s", "t", s => s.Choice("h", c => c.Heal(15)))
            .Build();
        Play(healRun, healScript, "h");
        Assert.Equal(35, healRun.Health.Current);

        var hurtRun = NewRun(current: 20, max: 40);
        var hurtScript = new EventScriptBuilder("s")
            .Situation("s", "t", s => s.Choice("d", c => c.Damage(8)))
            .Build();
        Play(hurtRun, hurtScript, "d");
        Assert.Equal(12, hurtRun.Health.Current);
    }

    [Fact]
    public void GainResource_with_expression_computes_from_state()
    {
        var run = NewRun(current: 25, max: 40); // missing 15
        var script = new EventScriptBuilder("s")
            .Situation("s", "t", s => s
                .Choice("loot", c => c.GainResource(Gold, RunExpr.MissingHealth)))
            .Build();

        Play(run, script, "loot");
        Assert.Equal(15, run.GetResource(Gold));
    }

    [Fact]
    public void Conditional_sugar_branches_on_state()
    {
        var run = NewRun(current: 10, max: 40); // below half
        var script = new EventScriptBuilder("s")
            .Situation("s", "t", s => s
                .Choice("gamble", c => c.Conditional(
                    RunExpr.LessThan(RunExpr.Multiply(RunExpr.CurrentHealth, RunExpr.Const(2)), RunExpr.MaxHealth),
                    whenTrue: new IRunEffectRequest[] { new HealRunEffect(8) },
                    whenFalse: new IRunEffectRequest[] { new ChangeResourceRunEffect(Gold, 5) })))
            .Build();

        Play(run, script, "gamble");
        Assert.Equal(18, run.Health.Current);
        Assert.Equal(0, run.GetResource(Gold));
    }

    [Fact]
    public void DrawEffects_sugar_picks_one_bundle()
    {
        var run = NewRun(seed: 42);
        var pool = RunPool.Uniform<IReadOnlyList<IRunEffectRequest>>(
            new IRunEffectRequest[] { new ChangeResourceRunEffect(Gold, 3) },
            new IRunEffectRequest[] { new ChangeResourceRunEffect(Gold, 7) });

        var script = new EventScriptBuilder("s")
            .Situation("s", "t", s => s.Choice("open", c => c.DrawEffects(pool)))
            .Build();

        Play(run, script, "open");
        Assert.Contains(run.GetResource(Gold), new[] { 3, 7 });
    }
}
