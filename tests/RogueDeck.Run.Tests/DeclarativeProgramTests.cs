using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Tests for declarative triggered programs (R1): relics and installed programs authored as data — event +
// optional condition + effect templates — with condition and effects reading the event at dispatch.
public class DeclarativeProgramTests
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

    private static CombatResolvedRunEvent Fight(CombatResult result, int damage) =>
        new(new NodeId("fight"), result, HeroHpRemaining: 30 - damage, DamageTaken: damage);

    private static void Play(RunState run, RunDefinitionRegistry registry, IRunEvent evt)
    {
        run.RaiseEvent(evt);
        new RunEffectProcessor().ResolvePending(run, registry);
    }

    [Fact]
    public void Bloodstone_heals_on_victory_only()
    {
        var registry = BuildRegistry();

        var won = NewRun(current: 30);
        won.AddRelic(new RelicInstance(StandardRelics.Bloodstone(5)));
        Play(won, registry, Fight(CombatResult.Victory, 0));
        Assert.Equal(35, won.Health.Current); // condition (victory) held

        var lost = NewRun(current: 30);
        lost.AddRelic(new RelicInstance(StandardRelics.Bloodstone(5)));
        Play(lost, registry, Fight(CombatResult.Defeat, 0));
        Assert.Equal(30, lost.Health.Current); // condition false -> no heal
    }

    [Fact]
    public void Leech_gains_gold_equal_to_damage_via_template()
    {
        var registry = BuildRegistry();
        var run = NewRun();
        run.AddRelic(new RelicInstance(StandardRelics.Leech()));

        Play(run, registry, Fight(CombatResult.Victory, 7));
        Assert.Equal(7, run.GetResource(Gold)); // effect value read from the event at dispatch
    }

    [Fact]
    public void A_declarative_program_works_installed_directly_too()
    {
        var registry = BuildRegistry();
        var run = NewRun();
        // Same substrate as a relic: an installed program that gains gold equal to combat damage taken.
        run.InstallProgram(new InstalledRunProgram(
            new RunProgramId("scavenger"),
            RunPrograms.On<CombatResolvedRunEvent>(
                RunEffectTemplates.GainResource(Gold, RunEventValues.CombatDamageTaken))));

        Play(run, registry, Fight(CombatResult.Victory, 4));
        Assert.Equal(4, run.GetResource(Gold));
    }

    // The counter twin: "record half of what that fight cost you" is the same shape as gaining gold from the
    // event, and a counter is where a run keeps a promise it has to pay later.
    [Fact]
    public void A_counter_template_reads_the_event_at_dispatch()
    {
        var registry = BuildRegistry();
        var debt = new RunCounterId("debt");
        var run = NewRun();
        run.InstallProgram(new InstalledRunProgram(
            new RunProgramId("tally"),
            RunPrograms.On<CombatResolvedRunEvent>(
                RunEffectTemplates.ChangeCounter(debt, RunEventValues.CombatDamageTaken))));

        Play(run, registry, Fight(CombatResult.Victory, 9));
        Assert.Equal(9, run.GetCounter(debt));
    }

    // ★ WHY THE TEMPLATES EXIST, stated as the failure they prevent. A trigger is evaluated in two moments: its
    // condition at DISPATCH, with the event in scope, and its plain effects afterwards, when the queue drains
    // and the event is gone. So an effect that computes its own amount from the event — rather than a template
    // that computed it at dispatch — asks a question nobody is left to answer, and throws.
    //
    // This is a trap without a shape: the program reads correctly and serializes correctly, and only dies on
    // the run where the relic carrying it is actually worn. Eight of Bureaucrats & Broomsticks' Shop relics
    // carried it undetected, because nothing in that game could sell a Shop relic.
    [Fact]
    public void A_queued_effect_can_no_longer_read_the_event()
    {
        var registry = BuildRegistry();
        var run = NewRun();
        run.InstallProgram(new InstalledRunProgram(
            new RunProgramId("late-payer"),
            RunPrograms.On<CombatResolvedRunEvent>(
                new ComputedResourceRunEffect(Gold, RunEventValues.CombatDamageTaken))));

        var thrown = Assert.Throws<InvalidOperationException>(
            () => Play(run, registry, Fight(CombatResult.Victory, 4)));
        Assert.Contains("without a matching event", thrown.Message, StringComparison.Ordinal);
        Assert.Equal(0, run.GetResource(Gold));
    }

    [Fact]
    public void When_condition_gates_the_effects()
    {
        var registry = BuildRegistry();
        var run = NewRun();
        // Gain 100 gold only when a fight cost at least 10 HP.
        run.InstallProgram(new InstalledRunProgram(
            new RunProgramId("hazard-pay"),
            RunPrograms.When<CombatResolvedRunEvent>(
                RunExpr.GreaterOrEqual(RunEventValues.CombatDamageTaken, RunExpr.Const(10)),
                new ChangeResourceRunEffect(Gold, 100))));

        Play(run, registry, Fight(CombatResult.Victory, 6));
        Assert.Equal(0, run.GetResource(Gold)); // condition false

        Play(run, registry, Fight(CombatResult.Victory, 15));
        Assert.Equal(100, run.GetResource(Gold)); // condition true
    }
}
