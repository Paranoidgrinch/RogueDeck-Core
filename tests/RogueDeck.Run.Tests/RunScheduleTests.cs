using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Tests for scheduled consequences (Phase C). A schedule is an installed program that counts occurrences or
// waits for a condition, fires once, and uninstalls itself — so these pin the countdown/condition timing and
// the one-shot behaviour, plus authoring a delayed consequence straight from an event choice.
public class RunScheduleTests
{
    private static readonly RunCounterId Debt = new("debt");
    private static readonly RunFlagId CollectorComes = new("collector-comes");
    private static readonly RunFlagId TrapSprung = new("trap-sprung");

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

    private static CombatResolvedRunEvent Fight() =>
        new(new NodeId("fight"), CombatResult.Victory, HeroHpRemaining: 30, DamageTaken: 0);

    [Fact]
    public void AfterCombats_fires_on_the_nth_occurrence_then_uninstalls()
    {
        var registry = BuildRegistry();
        var processor = new RunEffectProcessor();
        var run = NewRun(current: 30);
        run.InstallProgram(RunSchedule.AfterCombats(
            new RunProgramId("debt-collector"), 2, new ApplyRunDamageRunEffect(3)));

        run.RaiseEvent(Fight());
        processor.ResolvePending(run, registry);
        Assert.Equal(30, run.Health.Current); // first combat: not due yet
        Assert.Single(run.InstalledPrograms);

        run.RaiseEvent(Fight());
        processor.ResolvePending(run, registry);
        Assert.Equal(27, run.Health.Current); // second combat: fires
        Assert.Empty(run.InstalledPrograms);   // one-shot: removed

        run.RaiseEvent(Fight());
        processor.ResolvePending(run, registry);
        Assert.Equal(27, run.Health.Current); // gone: no further effect
    }

    [Fact]
    public void AfterNodes_counts_node_entries()
    {
        var registry = BuildRegistry();
        var processor = new RunEffectProcessor();
        var run = NewRun();
        run.InstallProgram(RunSchedule.AfterNodes(
            new RunProgramId("reveal"), 3, new ChangeResourceRunEffect(StandardRunIds.Gold, 50)));

        for (var i = 0; i < 2; i++)
        {
            run.RaiseEvent(new NodeEnteredRunEvent(new NodeId($"n{i}"), StandardRunIds.EventNode));
            processor.ResolvePending(run, registry);
        }
        Assert.Equal(0, run.GetResource(StandardRunIds.Gold)); // not due after 2

        run.RaiseEvent(new NodeEnteredRunEvent(new NodeId("n2"), StandardRunIds.EventNode));
        processor.ResolvePending(run, registry);
        Assert.Equal(50, run.GetResource(StandardRunIds.Gold)); // due on the 3rd
    }

    [Fact]
    public void WhenCounterAtLeast_fires_once_when_threshold_is_crossed()
    {
        var registry = BuildRegistry();
        var processor = new RunEffectProcessor();
        var run = NewRun();
        run.InstallProgram(RunSchedule.WhenCounterAtLeast(
            new RunProgramId("collector"), Debt, 10, new SetFlagRunEffect(CollectorComes)));

        run.EnqueueEffect(new IncrementCounterRunEffect(Debt, 5));
        processor.ResolvePending(run, registry);
        Assert.False(run.HasFlag(CollectorComes)); // below threshold

        run.EnqueueEffect(new IncrementCounterRunEffect(Debt, 7)); // now 12
        processor.ResolvePending(run, registry);
        Assert.True(run.HasFlag(CollectorComes)); // crossed 10
        Assert.Empty(run.InstalledPrograms);       // one-shot

        // Further increases do not re-fire (program is gone) — clear the flag and confirm it stays clear.
        run.SetFlag(CollectorComes, false);
        run.EnqueueEffect(new IncrementCounterRunEffect(Debt, 5));
        processor.ResolvePending(run, registry);
        Assert.False(run.HasFlag(CollectorComes));
    }

    [Fact]
    public void Occurrences_must_be_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RunSchedule.AfterCombats(new RunProgramId("x"), 0));
    }

    [Fact]
    public void An_event_choice_can_schedule_a_delayed_consequence()
    {
        var registry = BuildRegistry();
        var processor = new RunEffectProcessor();
        var run = NewRun();

        // "Set a trap": after the next 3 combats, a flag is sprung — authored straight on the choice.
        var script = new EventScriptBuilder("trap")
            .Situation("trap", "t", s => s
                .Choice("set", c => c.Schedule(RunSchedule.AfterCombats(
                    new RunProgramId("ambush"), 3, new SetFlagRunEffect(TrapSprung)))))
            .Build();

        var node = new Node(new NodeId("n"), StandardRunIds.EventNode, script);
        var context = new NodeResolveContext(run, new ScriptedChoiceProvider("set"), registry, processor);
        new EventNodeResolver().Resolve(context, node);
        processor.ResolvePending(run, registry);

        Assert.Single(run.InstalledPrograms); // the trap is armed

        for (var i = 0; i < 3; i++)
        {
            run.RaiseEvent(Fight());
            processor.ResolvePending(run, registry);
        }

        Assert.True(run.HasFlag(TrapSprung));
        Assert.Empty(run.InstalledPrograms);
    }
}
