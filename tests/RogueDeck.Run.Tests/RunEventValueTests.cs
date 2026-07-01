using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Tests for the event-value catalog (R5 starter) and the data-condition schedule overload (R2): event fields
// are readable as named data blocks, and a schedule fires on a data condition over the event — no lambda.
public class RunEventValueTests
{
    private static readonly RunFlagId Ambushed = new("ambushed");

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

    private static CombatResolvedRunEvent Fight(CombatResult result, int damage) =>
        new(new NodeId("fight"), result, HeroHpRemaining: 30 - damage, DamageTaken: damage);

    [Fact]
    public void Event_values_read_the_event_in_context()
    {
        var run = NewRun();
        var context = new RunEvalContext(run, Fight(CombatResult.Victory, 8));

        Assert.Equal(8, RunEventValues.CombatDamageTaken.Evaluate(context));
        Assert.Equal(22, RunEventValues.CombatHeroHpRemaining.Evaluate(context));
        Assert.True(RunEventValues.CombatWasVictory.Evaluate(context));
        Assert.False(RunEventValues.CombatWasDefeat.Evaluate(context));
    }

    [Fact]
    public void Event_value_without_a_matching_event_throws()
    {
        var run = NewRun();
        Assert.Throws<InvalidOperationException>(() => RunEventValues.CombatDamageTaken.Evaluate(run));
    }

    [Fact]
    public void Schedule_When_fires_on_a_data_condition_over_the_event()
    {
        var registry = BuildRegistry();
        var processor = new RunEffectProcessor();
        var run = NewRun();

        // When a fight is lost (data condition, no lambda), set a flag — once.
        run.InstallProgram(RunSchedule.When<CombatResolvedRunEvent>(
            new RunProgramId("on-defeat"), RunEventValues.CombatWasDefeat, new SetFlagRunEffect(Ambushed)));

        run.RaiseEvent(Fight(CombatResult.Victory, 5));
        processor.ResolvePending(run, registry);
        Assert.False(run.HasFlag(Ambushed)); // condition false

        run.RaiseEvent(Fight(CombatResult.Defeat, 30));
        processor.ResolvePending(run, registry);
        Assert.True(run.HasFlag(Ambushed)); // fired
        Assert.Empty(run.InstalledPrograms); // one-shot
    }

    [Fact]
    public void Schedule_When_composes_event_values_with_arithmetic()
    {
        var registry = BuildRegistry();
        var processor = new RunEffectProcessor();
        var run = NewRun();

        // Fire when the hero took at least 10 damage in a fight.
        run.InstallProgram(RunSchedule.When<CombatResolvedRunEvent>(
            new RunProgramId("bloodied"),
            RunExpr.GreaterOrEqual(RunEventValues.CombatDamageTaken, RunExpr.Const(10)),
            new SetFlagRunEffect(Ambushed)));

        run.RaiseEvent(Fight(CombatResult.Victory, 6));
        processor.ResolvePending(run, registry);
        Assert.False(run.HasFlag(Ambushed));

        run.RaiseEvent(Fight(CombatResult.Victory, 12));
        processor.ResolvePending(run, registry);
        Assert.True(run.HasFlag(Ambushed));
    }
}
