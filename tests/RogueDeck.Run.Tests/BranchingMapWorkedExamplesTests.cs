using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Dsl;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Run.Tests;

// B4 (branching run-map, worked examples): end-to-end validation of the whole branching stack (B0 data + B1
// traversal + B2 validation + B3 authoring) driving REAL run content — combat + event nodes — through RunRunner.
// Proves the genre shapes work: a Slay-the-Spire act where the player's route changes which nodes resolve, a
// reconverging diamond, a linear spine with an optional detour, determinism, and that an unchosen branch is never
// forced. The ScriptedChoiceProvider routes path picks AND in-event choices from one id queue, so a scripted run
// lists node ids and choice ids in encounter order.
public class BranchingMapWorkedExamplesTests
{
    private static readonly CardDefinitionId Smite = new("smite");

    private static RunDefinitionRegistry BuildRegistry()
    {
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        return builder.Build();
    }

    private static RunState NewRun(int current, int max, RunMap map)
    {
        var run = new RunState(new RunId("run"), new HealthState(current, max), map);
        run.AddDeckCard(Smite);
        run.AddDeckCard(Smite);
        run.AddDeckCard(Smite);
        return run;
    }

    // A small winnable fight (the shared goblin fight from the other run tests): a 12-HP goblin slams once for 4
    // before two 6-damage smites kill it, so each combat node costs the hero exactly 4 run HP.
    private static Playthrough BuildGoblinFight(RunState run)
    {
        var blueprint = new ScenarioBlueprint();
        blueprint.Cards.Add(new CardBlueprint("smite")
        {
            Program = Effects.Program(Effects.DealDamage(Targets.EventTarget, 6)),
        });
        blueprint.EnemyActions.Add(new EnemyActionBlueprint("slam", new ActionIntent("Slam", IntentKind.Attack))
        {
            Program = new EffectProgram<EnemyActionContext>(new DealDamageNode<EnemyActionContext>(
                CombatantTargetSelectors.EventTarget, new ConstantExpression<EnemyActionContext>(4))),
        });
        blueprint.Hero = new HeroBlueprint("knight") { MaxHealth = run.Health.Max, CurrentHealth = run.Health.Current };
        blueprint.Hero.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));
        var goblin = new EnemyBlueprint("goblin") { MaxHealth = 12 };
        goblin.Actions.Add(new EnemyActionDefinitionId("slam"));
        blueprint.Enemies.Add(goblin);

        var script = new ScenarioScript()
            .HeroPlays("smite", "goblin").HeroEndsTurn()
            .EnemyActs("goblin", "slam", "knight").NextRound()
            .HeroPlays("smite", "goblin")
            .Build();
        return new Playthrough(blueprint, script, combatId: "fight");
    }

    private static RunMapBuilder Combat(RunMapBuilder b, string id) =>
        b.AddNode(new NodeId(id), StandardRunIds.CombatNode, new CombatNodePayload(BuildGoblinFight));

    private static RunMapBuilder Event(RunMapBuilder b, string id, EventScript script) =>
        b.AddNode(new NodeId(id), StandardRunIds.EventNode, script);

    private static EventScript GoldChest(int gold) =>
        StandardEvents.Treasure(new RewardId("chest"), new ChangeResourceRunEffect(StandardRunIds.Gold, gold));

    // ── Example 1: a Slay-the-Spire act — the player's route decides which room resolves ─────────────────────
    //
    //   start(combat) ─┬─> treasure(+50 gold) ─┐
    //                  └─> camp(rest +8 hp)  ───┴─> boss(combat)     (a diamond reconverging at the boss)

    private static RunMap StsAct()
    {
        var b = new RunMapBuilder();
        Combat(b, "start");
        Event(b, "treasure", GoldChest(50));
        Event(b, "camp", StandardEvents.Rest(healAmount: 8));
        Combat(b, "boss");
        return b
            .Connect("start", "treasure").Connect("start", "camp")
            .Connect("treasure", "boss").Connect("camp", "boss")
            .Entry("start")
            .Build();
    }

    [Fact]
    public void The_act_map_is_a_valid_dag()
    {
        Assert.Empty(RunMapValidator.Validate(StsAct()));
    }

    [Fact]
    public void Taking_the_treasure_route_grants_gold_and_never_visits_the_camp()
    {
        var run = NewRun(20, 30, StsAct());

        new RunRunner(BuildRegistry(), new ScriptedChoiceProvider("treasure", "take")).Run(run);

        Assert.Equal(RunResult.Victory, run.Result);
        Assert.Equal(50, run.GetResource(StandardRunIds.Gold));       // the treasure resolved
        Assert.Equal(12, run.Health.Current);                          // 20 − 4 (start) − 4 (boss); no rest heal
        Assert.DoesNotContain(new NodeId("camp"), run.VisitedNodes);   // the unchosen branch is never forced
        Assert.Contains(new NodeId("boss"), run.VisitedNodes);         // both routes reconverge at the boss
    }

    [Fact]
    public void Taking_the_camp_route_heals_and_never_visits_the_treasure()
    {
        var run = NewRun(20, 30, StsAct());

        new RunRunner(BuildRegistry(), new ScriptedChoiceProvider("camp", "rest")).Run(run);

        Assert.Equal(RunResult.Victory, run.Result);
        Assert.Equal(0, run.GetResource(StandardRunIds.Gold));         // no treasure
        Assert.Equal(20, run.Health.Current);                          // 20 − 4 (start) + 8 (rest) − 4 (boss)
        Assert.DoesNotContain(new NodeId("treasure"), run.VisitedNodes);
        Assert.Contains(new NodeId("boss"), run.VisitedNodes);
    }

    [Fact]
    public void The_same_map_and_route_is_deterministic()
    {
        var a = NewRun(20, 30, StsAct());
        var b = NewRun(20, 30, StsAct());

        new RunRunner(BuildRegistry(), new ScriptedChoiceProvider("treasure", "take")).Run(a);
        new RunRunner(BuildRegistry(), new ScriptedChoiceProvider("treasure", "take")).Run(b);

        Assert.Equal(a.Health.Current, b.Health.Current);
        Assert.Equal(a.GetResource(StandardRunIds.Gold), b.GetResource(StandardRunIds.Gold));
        Assert.Equal(
            a.EventHistory.OfType<NodeChosenRunEvent>().Select(e => e.NodeId.Value),
            b.EventHistory.OfType<NodeChosenRunEvent>().Select(e => e.NodeId.Value));
    }

    // ── Example 2: a linear spine with one optional detour (Monster-Train "take the side room or push on") ───
    //
    //   s0 ─> s1 ─┬─> s2 ─> boss           (push straight on)
    //             └─> vault(+30 gold) ─> s2 (detour, reconverges)

    private static RunMap SpineWithDetour()
    {
        var b = new RunMapBuilder();
        Combat(b, "s0");
        Combat(b, "s1");
        Event(b, "vault", GoldChest(30));
        Combat(b, "s2");
        Combat(b, "boss");
        return b
            .Connect("s0", "s1")
            .Connect("s1", "s2").Connect("s1", "vault")
            .Connect("vault", "s2")
            .Connect("s2", "boss")
            .Entry("s0")
            .Build();
    }

    [Fact]
    public void Pushing_straight_on_skips_the_detour()
    {
        var run = NewRun(40, 40, SpineWithDetour());

        // At the only fork (s1) the player heads straight to s2.
        new RunRunner(BuildRegistry(), new ScriptedChoiceProvider("s2")).Run(run);

        Assert.Equal(RunResult.Victory, run.Result);
        Assert.Equal(0, run.GetResource(StandardRunIds.Gold));
        Assert.DoesNotContain(new NodeId("vault"), run.VisitedNodes);
        Assert.Equal(24, run.Health.Current); // 40 − 4 × 4 combats (s0, s1, s2, boss)
    }

    [Fact]
    public void Taking_the_detour_collects_the_side_reward_and_still_reaches_the_boss()
    {
        var run = NewRun(40, 40, SpineWithDetour());

        new RunRunner(BuildRegistry(), new ScriptedChoiceProvider("vault", "take")).Run(run);

        Assert.Equal(RunResult.Victory, run.Result);
        Assert.Equal(30, run.GetResource(StandardRunIds.Gold));
        Assert.Contains(new NodeId("vault"), run.VisitedNodes);
        Assert.Contains(new NodeId("boss"), run.VisitedNodes);
    }

    // ── Example 3: a generated act is walkable end-to-end with real combat content ───────────────────────────

    [Fact]
    public void A_generated_act_of_real_fights_is_walkable_and_deterministic()
    {
        // Every generated node is a winnable fight; the default provider takes the first fork at each step.
        var map = LayeredMapGenerator.Generate(new LayeredMapSpec(Rows: 2, MinWidth: 2, MaxWidth: 3), seed: 7,
            (kind, coord) => new NodeContent(StandardRunIds.CombatNode, new CombatNodePayload(BuildGoblinFight)));

        Assert.Empty(RunMapValidator.Validate(map));

        var first = NewRun(40, 40, map);
        var second = NewRun(40, 40, map);
        new RunRunner(BuildRegistry(), new ScriptedChoiceProvider()).Run(first);
        new RunRunner(BuildRegistry(), new ScriptedChoiceProvider()).Run(second);

        Assert.Equal(RunResult.Victory, first.Result);
        Assert.Equal(new NodeId("r2c0"), first.CurrentNodeId); // walked to the boss
        Assert.Equal(first.VisitedNodes, second.VisitedNodes); // same seed + policy ⇒ same path
        Assert.Equal(first.Health.Current, second.Health.Current);
    }
}
