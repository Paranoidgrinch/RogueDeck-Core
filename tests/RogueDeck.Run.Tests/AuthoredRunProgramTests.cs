using System.Text.Json;
using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Run programs authored as CONTENT: a lasting consequence that belongs to no relic. An event hands one out with
// fx.installProgramById, which names a body registered in the content catalog (RunBlueprint.Programs) instead of
// carrying it — that is what lets such a consequence both serialize into an exported document and survive a save.
public class AuthoredRunProgramTests
{
    private static readonly RunResourceId Gold = StandardRunIds.Gold;
    private static readonly RunProgramSourceId AuditNotice = new("audit_notice");

    // "After your next fight, lose 4 Gold per HP you lost" — one shot: it pays out and steps down again.
    private static ITriggeredRunEffectDefinition AuditNoticeBody() =>
        RunPrograms.On<CombatResolvedRunEvent>(
            RunEffectTemplates.GainResource(Gold,
                RunExpr.Negate(RunExpr.Multiply(
                    new EventIntValueExpression(RunEventFields.CombatDamageTaken), RunExpr.Const(4)))),
            RunEffectTemplates.Literal(
                new UninstallRunProgramRunEffect(new RunProgramId(AuditNotice.Value))));

    private static RunDefinitionRegistry Registry()
    {
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        return builder.Build();
    }

    private static RunState NewRun(RunContentRegistry? content = null)
    {
        var run = new RunState(new RunId("run"), new HealthState(30, 40), new RunMap(Array.Empty<Node>()));
        run.SetResource(Gold, 100);
        if (content is not null)
            run.SetContent(content);
        return run;
    }

    private static RunContentRegistry Catalog() => new RunContentRegistryBuilder()
        .RegisterProgramDefinition(AuditNotice, AuditNoticeBody())
        .Build();

    private static void Drain(RunState run, RunDefinitionRegistry registry) =>
        new RunEffectProcessor().ResolvePending(run, registry);

    [Fact]
    public void A_program_installed_by_id_fires_once_and_then_steps_down()
    {
        var registry = Registry();
        var run = NewRun(Catalog());

        run.EnqueueEffect(new InstallProgramByIdRunEffect(AuditNotice));
        Drain(run, registry);
        Assert.Equal(new RunProgramId("audit_notice"), Assert.Single(run.InstalledPrograms).Id);

        run.RaiseEvent(new CombatResolvedRunEvent(
            new NodeId("fight"), CombatResult.Victory, HeroHpRemaining: 25, DamageTaken: 5));
        Drain(run, registry);

        Assert.Equal(80, run.GetResource(Gold));   // 100 − 4 × 5
        Assert.Empty(run.InstalledPrograms);       // the notice is settled

        // The next fight is not audited a second time.
        run.RaiseEvent(new CombatResolvedRunEvent(
            new NodeId("fight2"), CombatResult.Victory, HeroHpRemaining: 20, DamageTaken: 5));
        Drain(run, registry);
        Assert.Equal(80, run.GetResource(Gold));
    }

    // A pending consequence is pending or it is not — installing it again while it stands is not a fault.
    [Fact]
    public void Installing_the_same_program_twice_leaves_one()
    {
        var registry = Registry();
        var run = NewRun(Catalog());

        run.EnqueueEffect(new InstallProgramByIdRunEffect(AuditNotice));
        run.EnqueueEffect(new InstallProgramByIdRunEffect(AuditNotice));
        Drain(run, registry);

        Assert.Single(run.InstalledPrograms);
    }

    [Fact]
    public void Installing_a_program_nobody_authored_fails_loudly()
    {
        var registry = Registry();
        var run = NewRun(Catalog());

        run.EnqueueEffect(new InstallProgramByIdRunEffect(new RunProgramSourceId("nothing_here")));
        Assert.Throws<InvalidOperationException>(() => Drain(run, registry));
    }

    // The point of installing BY ID: the program carries its source, so the run it is installed in can be saved
    // and comes back with the consequence still pending.
    [Fact]
    public void A_run_carrying_an_installed_by_id_program_saves_and_resumes()
    {
        var registry = Registry();
        var content = Catalog();
        var map = new RunMap(Array.Empty<Node>());
        var run = NewRun(content);

        run.EnqueueEffect(new InstallProgramByIdRunEffect(AuditNotice));
        Drain(run, registry);

        var restored = RunState.Restore(
            RunSaveJson.FromJson(RunSaveJson.ToJson(run.Snapshot())), map, content);
        restored.SetContent(content);

        restored.RaiseEvent(new CombatResolvedRunEvent(
            new NodeId("fight"), CombatResult.Victory, HeroHpRemaining: 27, DamageTaken: 3));
        Drain(restored, registry);

        Assert.Equal(88, restored.GetResource(Gold)); // 100 − 4 × 3
        Assert.Empty(restored.InstalledPrograms);
    }

    // The whole reason for the indirection: an event that hands out a lasting consequence stays DATA.
    [Fact]
    public void An_event_that_installs_a_program_by_id_round_trips_through_the_document()
    {
        var options = RunJson.CreateOptions();
        var blueprint = new RunBlueprint(
            [new CardDefinitionId("strike")],
            new Dictionary<string, EventScript>
            {
                ["fee_table"] = new("start",
                [
                    new EventSituation("start", "The fee table amends itself.",
                    [
                        new EventChoice("waiver",
                        [
                            new ChangeResourceRunEffect(Gold, 75),
                            new InstallProgramByIdRunEffect(AuditNotice),
                        ], TextKey: "Apply for a fee waiver"),
                    ]),
                ]),
            },
            [],
            [],
            [],
            new RunMap([new Node(new NodeId("e"), StandardRunIds.EventNode, new EventRef(new EventId("fee_table")))]))
        {
            Programs = new Dictionary<string, ITriggeredRunEffectDefinition>
            {
                [AuditNotice.Value] = AuditNoticeBody(),
            },
        };

        var json = RunJson.ToJson(blueprint, options);
        var reloaded = RunJson.BlueprintFromJson(json, options);

        Assert.Equal(json, RunJson.ToJson(reloaded, options));
        Assert.Equal([AuditNotice.Value], reloaded.Programs!.Keys);
        Assert.IsType<InstallProgramByIdRunEffect>(
            reloaded.Events["fee_table"].Situations["start"].Choices[0].Effects[1]);
    }

    // A document written before the section existed carries no "programs" key at all.
    [Fact]
    public void A_document_without_authored_programs_writes_no_section()
    {
        var options = RunJson.CreateOptions();
        var blueprint = new RunBlueprint(
            [], new Dictionary<string, EventScript>(), [], [], [], new RunMap([]));

        Assert.DoesNotContain("\"Programs\"", RunJson.ToJson(blueprint, options));
    }
}
