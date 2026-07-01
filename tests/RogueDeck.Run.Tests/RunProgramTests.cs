using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Tests for the generalized trigger substrate: programs installed directly on the run react to events
// exactly like relic programs, can be installed/uninstalled through the effect flow, and can uninstall
// themselves after firing (the foundation the scheduler builds on in a later phase).
public class RunProgramTests
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

    private static InstalledRunProgram GainGoldOnNodeEntered(RunProgramId id, int amount) =>
        new(id, new TriggeredRunEffect<NodeEnteredRunEvent>((_, _) =>
            new IRunEffectRequest[] { new ChangeResourceRunEffect(Gold, amount) }));

    [Fact]
    public void Installed_program_reacts_to_a_matching_event()
    {
        var registry = BuildRegistry();
        var processor = new RunEffectProcessor();
        var run = NewRun();
        run.InstallProgram(GainGoldOnNodeEntered(new RunProgramId("p"), 5));

        run.RaiseEvent(new NodeEnteredRunEvent(new NodeId("a"), StandardRunIds.EventNode));
        processor.ResolvePending(run, registry);

        Assert.Equal(5, run.GetResource(Gold));
    }

    [Fact]
    public void Installing_via_effect_logs_and_raises_an_event()
    {
        var registry = BuildRegistry();
        var processor = new RunEffectProcessor();
        var run = NewRun();

        var program = GainGoldOnNodeEntered(new RunProgramId("p"), 1);
        run.EnqueueEffect(new InstallRunProgramRunEffect(program));
        processor.ResolvePending(run, registry);

        Assert.Contains(run.InstalledPrograms, p => p.Id == program.Id);
        Assert.Contains(run.EventHistory, e => e is RunProgramInstalledRunEvent);
    }

    [Fact]
    public void Uninstalling_via_effect_stops_further_reactions()
    {
        var registry = BuildRegistry();
        var processor = new RunEffectProcessor();
        var run = NewRun();
        var id = new RunProgramId("p");
        run.InstallProgram(GainGoldOnNodeEntered(id, 5));

        // The processor drains effects before events, so to sequence an uninstall between two events each
        // step is resolved in turn (in real use the uninstall is enqueued from within a reaction).
        run.RaiseEvent(new NodeEnteredRunEvent(new NodeId("a"), StandardRunIds.EventNode));
        processor.ResolvePending(run, registry); // first node fired (+5)

        run.EnqueueEffect(new UninstallRunProgramRunEffect(id));
        processor.ResolvePending(run, registry); // program removed

        run.RaiseEvent(new NodeEnteredRunEvent(new NodeId("b"), StandardRunIds.EventNode));
        processor.ResolvePending(run, registry); // no reaction left

        Assert.Equal(5, run.GetResource(Gold));
        Assert.Empty(run.InstalledPrograms);
    }

    [Fact]
    public void A_program_can_uninstall_itself_after_firing_once()
    {
        var registry = BuildRegistry();
        var processor = new RunEffectProcessor();
        var run = NewRun();

        // Mint the id first, then close over it so the reaction can remove itself.
        var id = new RunProgramId("once");
        var program = new InstalledRunProgram(id, new TriggeredRunEffect<NodeEnteredRunEvent>((_, _) =>
            new IRunEffectRequest[]
            {
                new ChangeResourceRunEffect(Gold, 5),
                new UninstallRunProgramRunEffect(id),
            }));
        run.InstallProgram(program);

        run.RaiseEvent(new NodeEnteredRunEvent(new NodeId("a"), StandardRunIds.EventNode));
        processor.ResolvePending(run, registry);
        run.RaiseEvent(new NodeEnteredRunEvent(new NodeId("b"), StandardRunIds.EventNode));
        processor.ResolvePending(run, registry);

        Assert.Equal(5, run.GetResource(Gold)); // fired exactly once
        Assert.Empty(run.InstalledPrograms);
    }

    [Fact]
    public void Relics_and_installed_programs_both_fire_for_the_same_event()
    {
        var registry = BuildRegistry();
        var processor = new RunEffectProcessor();
        var run = NewRun(current: 30, max: 40);
        run.AddRelic(new RelicInstance(StandardRelics.Bloodstone(healAmount: 5)));
        run.InstallProgram(new InstalledRunProgram(new RunProgramId("bonus"),
            new TriggeredRunEffect<CombatResolvedRunEvent>((_, _) =>
                new IRunEffectRequest[] { new ChangeResourceRunEffect(Gold, 3) })));

        run.RaiseEvent(new CombatResolvedRunEvent(new NodeId("fight"), CombatResult.Victory, 30, 0));
        processor.ResolvePending(run, registry);

        Assert.Equal(35, run.Health.Current); // relic healed
        Assert.Equal(3, run.GetResource(Gold)); // installed program paid out
    }

    [Fact]
    public void Duplicate_install_id_throws_and_uninstalling_absent_is_a_no_op()
    {
        var run = NewRun();
        var id = new RunProgramId("dup");
        run.InstallProgram(GainGoldOnNodeEntered(id, 1));

        Assert.Throws<InvalidOperationException>(() => run.InstallProgram(GainGoldOnNodeEntered(id, 2)));
        Assert.False(run.UninstallProgram(new RunProgramId("absent")));
        Assert.True(run.UninstallProgram(id));
    }
}
