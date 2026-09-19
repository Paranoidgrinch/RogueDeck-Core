using RogueDeck.Core.Combat;
using RogueDeck.Run;

namespace RogueDeck.Run.Tests;

// A run is ONE walk through its acts in order. The deck, the relics and the purse have to cross every boundary
// and a save is a save of the whole run, so an act is a segment of one walk rather than a separate game — and
// the boundary is a real moment content can act on, which until now it had to fake by watching for a boss node.
public class ActTests
{
    private static readonly RunResourceId Gold = StandardRunIds.Gold;

    // Every blueprint written before acts existed is a one-act run, walked exactly as it always was.
    [Fact]
    public void A_blueprint_that_names_no_acts_is_one_act()
    {
        var run = Walk(OneMap("a"));

        Assert.Single(run.Acts);
        Assert.Equal(1, run.ActNumber);
        Assert.Equal(10, run.GetResource(Gold)); // the single node paid out
    }

    [Fact]
    public void The_walk_crosses_from_one_act_into_the_next()
    {
        var run = Walk(TwoActs());

        Assert.Equal(2, run.ActNumber);
        Assert.Equal(20, run.GetResource(Gold)); // one node in each act paid out
    }

    // The first act is announced too: a rule about "each act" should need no special case for the act the run
    // happens to open on.
    [Fact]
    public void Every_act_announces_itself_and_its_ending()
    {
        var run = Walk(TwoActs());

        Assert.Equal([1, 2], run.EventHistory.OfType<ActStartedRunEvent>().Select(e => e.ActNumber));
        Assert.Equal([1, 2], run.EventHistory.OfType<ActCompletedRunEvent>().Select(e => e.ActNumber));
        Assert.Equal(["one", "two"], run.EventHistory.OfType<ActStartedRunEvent>().Select(e => e.ActId));
    }

    // "The first time each Act" is a promise nothing could keep while the flag outlived the act.
    [Fact]
    public void An_acts_flags_are_forgotten_at_its_boundary_and_the_runs_are_not()
    {
        var run = NewRun(TwoActs());
        var registry = Registry();
        run.SetActFlag(new RunFlagId("used-this-act"), true);
        run.SetFlag(new RunFlagId("kept"), true);

        new RunRunner(registry, new ScriptedChoiceProvider("gold")).Run(run);

        Assert.False(run.HasActFlag(new RunFlagId("used-this-act")));
        Assert.True(run.HasFlag(new RunFlagId("kept")));
    }

    // …and everything that belongs to the RUN crosses untouched.
    [Fact]
    public void What_belongs_to_the_run_crosses_the_boundary()
    {
        var run = NewRun(TwoActs());
        run.AddDeckCard(new CardDefinitionId("strike"));
        run.SetResource(Gold, 5);

        new RunRunner(Registry(), new ScriptedChoiceProvider("gold")).Run(run);

        Assert.Single(run.Deck);
        Assert.Equal(25, run.GetResource(Gold)); // 5 carried + 10 in each act
    }

    [Fact]
    public void Content_can_ask_which_act_it_is_in()
    {
        var run = NewRun(TwoActs());
        Assert.Equal(1, RunExpr.Act.Evaluate(run));

        run.BeginNextAct();
        Assert.Equal(2, RunExpr.Act.Evaluate(run));
    }

    // Two acts that share ONE generation spec are still two different maps: each act draws from its own seed,
    // or the run would walk the same act over again under a new name.
    [Fact]
    public void Two_acts_on_one_spec_are_still_two_different_maps()
    {
        var spec = new MapGenerationSpec
        {
            Rows = 8,
            MinWidth = 2,
            MaxWidth = 4,
            MinEnemiesPerPath = 4,
            // Fights only: a non-combat role would need a NodeRefs entry, and what is under test is the seed.
            KindWeights = new Dictionary<MapNodeKind, int> { [MapNodeKind.Combat] = 1 },
            Encounters = new EncounterDistribution
            {
                ByRole = new Dictionary<MapNodeKind, IReadOnlyList<EncounterPoolEntry>>
                {
                    [MapNodeKind.Combat] = Pool("fight.", 60),
                    [MapNodeKind.Boss] = Pool("boss.", 5),
                },
            },
        };
        var blueprint = new RunBlueprint([], new Dictionary<string, EventScript>(), [], [], [], OneMap("a"))
        {
            MapGeneration = spec,
            Acts = [new RunAct("one"), new RunAct("two")],
        };

        var plan = blueprint.BuildActPlan(seed: 1, startingLoadout: 0);

        Assert.Equal(["one", "two"], plan.Select(act => act.Id));
        Assert.NotEqual(
            plan[0].Map.Nodes.Select(n => n.Payload.ToString()).ToList(),
            plan[1].Map.Nodes.Select(n => n.Payload.ToString()).ToList());
    }

    // …and the same seed gives the same acts back, which is what a resumed run depends on.
    [Fact]
    public void The_same_seed_lays_out_the_same_acts()
    {
        var blueprint = new RunBlueprint([], new Dictionary<string, EventScript>(), [], [], [], OneMap("a"))
        {
            Acts = [new RunAct("one", Map: OneMap("a")), new RunAct("two", Map: OneMap("b"))],
        };

        Assert.Equal(
            blueprint.BuildActPlan(1, 0).Select(a => a.Id),
            blueprint.BuildActPlan(1, 0).Select(a => a.Id));
    }

    private static IReadOnlyList<EncounterPoolEntry> Pool(string prefix, int count) =>
        Enumerable.Range(0, count).Select(i => new EncounterPoolEntry(new EncounterId($"{prefix}{i}"))).ToArray();

    // A save taken in act two must not come back in act one's map.
    [Fact]
    public void A_run_resumes_in_the_act_it_was_saved_in()
    {
        var run = NewRun(TwoActs());
        run.BeginNextAct();
        run.SetActFlag(new RunFlagId("spent"), true);

        var save = RunSaveJson.FromJson(RunSaveJson.ToJson(run.Snapshot()));
        var restored = RunState.Restore(save, run.Map, null);
        restored.SetActPlan(run.Acts, save.ActIndex);

        Assert.Equal(2, restored.ActNumber);
        Assert.Equal("two", restored.CurrentActId);
        Assert.True(restored.HasActFlag(new RunFlagId("spent")));
    }

    // A one-act run's save says nothing about acts, so every save written before acts existed round-trips
    // byte-identically.
    [Fact]
    public void A_one_act_save_writes_nothing_about_acts()
    {
        var json = RunSaveJson.ToJson(NewRun(OneMap("a")).Snapshot());

        Assert.DoesNotContain("ActIndex", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ActFlags", json, StringComparison.Ordinal);
    }

    // ── WHAT AN ACT DOES AT ITS OWN GATES (RunAct.Opening) ───────────────────────────────────────────────
    // A rule of the PLACE rather than a thing the player carries. The one the content uses it for is putting
    // the body back together, and the reason is not only kindness: health is the single number that ties one
    // act's difficulty to the previous act's luck, and cutting it at the boundary is what makes a run
    // decomposable — what crosses is then only the deck, the relics and the purse.

    [Fact]
    public void An_act_resolves_its_own_opening_when_it_begins()
    {
        var run = Walk([
            new RunActPlan("one", OneMap("a")),
            new RunActPlan("two", OneMap("b"), [new ChangeResourceRunEffect(Gold, 500)]),
        ]);

        // Two nodes paid 10 each, and act two's gates paid 500.
        Assert.Equal(520, run.GetResource(Gold));
    }

    // ⚠ THE ACT THE RUN OPENS ON IS AN ACT. A rule about "each act" that skipped the first would be a rule
    // about "each act but the first", and every content author would have to remember which.
    [Fact]
    public void The_act_a_run_opens_on_has_its_gates_too()
    {
        var run = Walk([new RunActPlan("one", OneMap("a"), [new ChangeResourceRunEffect(Gold, 500)])]);

        Assert.Equal(510, run.GetResource(Gold));
    }

    // ⚠ AN OPENING IS NOT A REWARD FOR FINISHING THE ACT BEFORE IT. The difference shows on a run that does
    // not live to arrive: nothing at act two's gates fires for a run that died in act one.
    [Fact]
    public void An_act_that_is_never_reached_never_opens()
    {
        var run = NewRun([
            new RunActPlan("one", OneMap("a")),
            new RunActPlan("two", OneMap("b"), [new ChangeResourceRunEffect(Gold, 500)]),
        ]);
        run.SetResult(RunResult.Defeat);
        new RunRunner(Registry(), new ScriptedChoiceProvider("gold")).Run(run);

        // Act one's node paid its 10 and the walk then stopped where the run did. The 500 at act two's gates
        // is the whole assertion: it is not waiting to be collected, it simply never happens.
        Assert.Equal(10, run.GetResource(Gold));
        Assert.Equal(1, run.ActNumber);
    }

    // What the content actually installs: heal whatever is missing. A body that walks in hurt walks on whole,
    // and a body that walks in whole is not handed anything.
    [Fact]
    public void The_gates_of_an_act_put_a_hurt_body_back_together()
    {
        var acts = new RunActPlan[]
        {
            new("one", OneMap("a")),
            new("two", OneMap("b"), [new ComputedHealRunEffect(RunExpr.MissingHealth)]),
        };
        var run = new RunState(new RunId("run"), new HealthState(30, 40), acts[0].Map);
        run.SetActPlan(acts);
        run.Health.SetCurrent(7);

        new RunRunner(Registry(), new ScriptedChoiceProvider("gold")).Run(run);

        Assert.Equal(40, run.Health.Current);
    }

    // ── harness ────────────────────────────────────────────────────────────────

    // One node that pays 10 Gold when its only choice is taken.
    private static RunMap OneMap(string id) =>
        new([new Node(new NodeId(id), StandardRunIds.EventNode, Payout())]);

    private static EventScript Payout() =>
        new("payout",
        [
            new EventSituation("payout", "event.payout",
                [new EventChoice("gold", [new ChangeResourceRunEffect(Gold, 10)])]),
        ]);

    private static IReadOnlyList<RunActPlan> TwoActs() =>
        [new RunActPlan("one", OneMap("a")), new RunActPlan("two", OneMap("b"))];

    private static RunDefinitionRegistry Registry()
    {
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        return builder.Build();
    }

    private static RunState NewRun(RunMap map)
    {
        var run = new RunState(new RunId("run"), new HealthState(30, 40), map);
        run.SetActPlan([new RunActPlan(string.Empty, map)]);
        return run;
    }

    private static RunState NewRun(IReadOnlyList<RunActPlan> acts)
    {
        var run = new RunState(new RunId("run"), new HealthState(30, 40), acts[0].Map);
        run.SetActPlan(acts);
        return run;
    }

    private static RunState Walk(RunMap map)
    {
        var run = NewRun(map);
        new RunRunner(Registry(), new ScriptedChoiceProvider("gold")).Run(run);
        return run;
    }

    private static RunState Walk(IReadOnlyList<RunActPlan> acts)
    {
        var run = NewRun(acts);
        new RunRunner(Registry(), new ScriptedChoiceProvider("gold")).Run(run);
        return run;
    }
}
