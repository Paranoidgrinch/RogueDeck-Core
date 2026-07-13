using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// Save & resume mid-run (engine gap #1): a live RunState snapshots its persistent progress to a serializable
// RunSaveData and rebuilds from it — so a run can be saved between nodes and resumed. Instance ids are regenerated
// on restore (nothing references them across a save); the faithful-restore check is a JSON round-trip that is stable
// through save → restore → save. A save is only valid at a clean interlude, so Snapshot guards against state it
// can't yet capture rather than silently dropping it.
public class RunSaveTests
{
    private static readonly RunResourceId Gold = StandardRunIds.Gold;

    private static (RunState Run, RunContentRegistry Content) BuildMidRun()
    {
        var bloodstone = StandardRelics.Bloodstone(5);
        var potion = new ConsumableDefinition(
            new ConsumableId("potion"), "Potion", Array.Empty<IRunEffectRequest>(), null);
        var content = new RunContentRegistryBuilder()
            .RegisterRelic(bloodstone)
            .RegisterConsumable(potion)
            .Build();

        var run = new RunState(new RunId("save-run"), new HealthState(18, 40), new RunMap(Array.Empty<Node>()), randomSeed: 7);
        run.SetContent(content);

        run.AddDeckCard(new CardDefinitionId("strike"));
        run.AddDeckCard(new CardDefinitionId("strike"));
        run.AddDeckCard(new CardDefinitionId("defend"));
        run.Deck[0].Upgrade();                              // strike+
        run.Deck[1].AddTag(new RunCardTagId("blessed"));
        run.Deck[2].SetMemory("plays", 3);

        run.SetResource(Gold, 55);
        run.SetFlag(new RunFlagId("beat.elite"), true);
        run.SetCounter(new RunCounterId("kills"), 4);

        var relic = new RelicInstance(content.GetRelic(bloodstone.Id));
        relic.SetEnabled(false);                            // a disabled relic must round-trip
        run.AddRelic(relic);
        run.AddConsumable(potion.Id, potion.UseEffects, potion.CombatUse);

        run.AdvanceTo(1);
        run.AdvanceToNode(new NodeId("n2"));
        run.NextRandom(6);                                  // advance the RNG position
        run.NextRandom(6);

        return (run, content);
    }

    [Fact]
    public void A_mid_run_state_round_trips_and_resumes_identically()
    {
        var (run, content) = BuildMidRun();

        var json = RunSaveJson.ToJson(run.Snapshot());
        var restored = RunState.Restore(RunSaveJson.FromJson(json), run.Map, content);

        // Faithful: a save of the restored run is byte-identical to the original save.
        Assert.Equal(json, RunSaveJson.ToJson(restored.Snapshot()));

        // Spot checks across the surface.
        Assert.Equal(18, restored.Health.Current);
        Assert.Equal(40, restored.Health.Max);
        Assert.Equal(55, restored.GetResource(Gold));
        Assert.True(restored.HasFlag(new RunFlagId("beat.elite")));
        Assert.Equal(4, restored.GetCounter(new RunCounterId("kills")));
        Assert.Equal(1, restored.Position);
        Assert.Equal(new NodeId("n2"), restored.CurrentNodeId);
        Assert.True(restored.HasVisited(new NodeId("n2")));

        Assert.Equal(1, restored.Deck[0].UpgradeLevel);
        Assert.True(restored.Deck[1].HasTag(new RunCardTagId("blessed")));
        Assert.Equal(3, restored.Deck[2].GetMemory("plays"));

        var relic = Assert.Single(restored.Relics);
        Assert.False(relic.Enabled);                        // the disabled relic came back disabled
        Assert.Single(restored.Consumables);
    }

    [Fact]
    public void Resuming_continues_the_rng_from_where_the_save_left_off()
    {
        var (run, content) = BuildMidRun();

        // Snapshot first (captures the current RNG step), then compare the original's next draw against the
        // restored run's next draw — they must match (same seed + step).
        var save = RunSaveJson.ToJson(run.Snapshot());
        var expectedNext = run.NextRandom(100);
        var restored = RunState.Restore(RunSaveJson.FromJson(save), run.Map, content);
        Assert.Equal(expectedNext, restored.NextRandom(100));
    }

    [Fact]
    public void Board_units_round_trip_with_their_live_hp()
    {
        var run = new RunState(new RunId("r"), new HealthState(30, 30), new RunMap(Array.Empty<Node>()));
        var unit = run.AddUnit(new RunUnitData("skeleton", "unit.skeleton", MaxHealth: 12));
        unit.Health.SetCurrent(7); // wounded

        var restored = RunState.Restore(
            RunSaveJson.FromJson(RunSaveJson.ToJson(run.Snapshot())), run.Map, content: null);

        var back = Assert.Single(restored.Units);
        Assert.Equal(new CombatantDefinitionId("skeleton"), back.DefinitionId);
        Assert.Equal(7, back.Health.Current);   // the wound carried across the save
        Assert.Equal(12, back.Health.Max);
    }

    [Fact]
    public void A_resumed_run_continues_from_the_saved_position_without_redoing_done_nodes()
    {
        var counter = new RunCounterId("nodesRun");
        EventScript Bump() => new EventScriptBuilder("s")
            .Situation("s", "t", s => s.Choice("go", c => c.IncrementCounter(counter, 1)))
            .Build();
        var map = new RunMap(new[]
        {
            new Node(new NodeId("n0"), StandardRunIds.EventNode, Bump()),
            new Node(new NodeId("n1"), StandardRunIds.EventNode, Bump()),
        });

        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        var registry = builder.Build();

        // A mid-run save: node 0 is resolved (Position 0, its counter effect applied), node 1 is not.
        var run = new RunState(new RunId("r"), new HealthState(30, 30), map);
        run.AdvanceTo(0);
        run.SetCounter(counter, 1);

        var restored = RunState.Restore(RunSaveJson.FromJson(RunSaveJson.ToJson(run.Snapshot())), map, content: null);
        new RunRunner(registry, new ScriptedChoiceProvider("go")).Run(restored);

        Assert.Equal(RunResult.Victory, restored.Result);
        Assert.Equal(2, restored.GetCounter(counter)); // only node 1 ran (re-running node 0 would make it 3)
    }

    [Fact]
    public void Saving_with_uncaptured_scheduled_state_throws_rather_than_losing_it()
    {
        var run = new RunState(new RunId("r"), new HealthState(20, 30), new RunMap(Array.Empty<Node>()));
        run.AddRewardModifier(RewardModifiers.Custom((_, _) => { }), rewardCount: 1);

        Assert.Throws<InvalidOperationException>(() => run.Snapshot());
    }

    // A stateless installed program (data-authored persistent rule) saves BY REFERENCE: the snapshot stores its
    // source id, and Restore re-links the reaction body from the content catalog — then it still fires.
    [Fact]
    public void An_installed_by_reference_program_round_trips_and_still_fires_after_restore()
    {
        var source = new RunProgramSourceId("gain-gold-on-node");
        ITriggeredRunEffectDefinition reaction =
            RunPrograms.On<NodeEnteredRunEvent>(new ChangeResourceRunEffect(Gold, 5));
        var content = new RunContentRegistryBuilder()
            .RegisterProgramDefinition(source, reaction)
            .Build();

        var map = new RunMap(Array.Empty<Node>());
        var run = new RunState(new RunId("r"), new HealthState(30, 40), map);
        run.SetContent(content);
        run.InstallProgram(new InstalledRunProgram(new RunProgramId("p#1"), reaction, source));

        var json = RunSaveJson.ToJson(run.Snapshot());
        var restored = RunState.Restore(RunSaveJson.FromJson(json), map, content);

        // Faithful: a save of the restored run is byte-identical, and the program came back with both ids.
        Assert.Equal(json, RunSaveJson.ToJson(restored.Snapshot()));
        var program = Assert.Single(restored.InstalledPrograms);
        Assert.Equal(new RunProgramId("p#1"), program.Id);
        Assert.Equal(source, program.SourceId);

        // The re-linked body still reacts to its event after the round-trip.
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        var registry = builder.Build();
        restored.RaiseEvent(new NodeEnteredRunEvent(new NodeId("a"), StandardRunIds.EventNode));
        new RunEffectProcessor().ResolvePending(restored, registry);
        Assert.Equal(5, restored.GetResource(Gold));
    }

    [Fact]
    public void Saving_a_program_without_a_source_id_throws()
    {
        var run = new RunState(new RunId("r"), new HealthState(30, 40), new RunMap(Array.Empty<Node>()));
        run.InstallProgram(new InstalledRunProgram(new RunProgramId("p"),
            new TriggeredRunEffect<NodeEnteredRunEvent>((_, _) => Array.Empty<IRunEffectRequest>())));

        Assert.Throws<InvalidOperationException>(() => run.Snapshot());
    }

    [Fact]
    public void Restoring_a_program_whose_source_is_unregistered_throws_rather_than_dropping_the_rule()
    {
        var source = new RunProgramSourceId("known");
        var reaction = RunPrograms.On<NodeEnteredRunEvent>(new ChangeResourceRunEffect(Gold, 1));
        var content = new RunContentRegistryBuilder().RegisterProgramDefinition(source, reaction).Build();

        var map = new RunMap(Array.Empty<Node>());
        var run = new RunState(new RunId("r"), new HealthState(30, 40), map);
        run.SetContent(content);
        run.InstallProgram(new InstalledRunProgram(new RunProgramId("p"), reaction, source));
        var json = RunSaveJson.ToJson(run.Snapshot());

        // Restoring against content that lacks the definition fails loudly rather than silently losing the rule.
        var empty = new RunContentRegistryBuilder().Build();
        Assert.Throws<InvalidOperationException>(() => RunState.Restore(RunSaveJson.FromJson(json), map, empty));
    }
}
