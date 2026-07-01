using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Dsl;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Run.Tests;

// End-to-end tests for the first run-engine slice. They validate the two resolvers (combat + event), that a
// wounded hero carries HP into the next fight, that a relic is just a run-level triggered program, that the
// runner is purely registration-driven, and that the relic dispatch loop cannot wedge a run.
public class RunEngineTests
{
    private static readonly CardDefinitionId Smite = new("smite");

    // A small winnable fight built FROM run state: hero HP/deck come from the run, a 12-HP goblin slams for 4.
    // Two smites (6 each) kill it; the hero eats exactly one 4-damage slam in between → 4 run HP lost.
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

        // Project the run into the fight: current HP carries in via CurrentHealth, the deck is the run deck.
        blueprint.Hero = new HeroBlueprint("knight")
        {
            MaxHealth = run.Health.Max,
            CurrentHealth = run.Health.Current,
        };
        blueprint.Hero.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));
        foreach (var card in run.Deck)
            blueprint.Hero.Deck.Add(new DeckEntry(card.DefinitionId, 1));

        var goblin = new EnemyBlueprint("goblin") { MaxHealth = 12 };
        goblin.Actions.Add(new EnemyActionDefinitionId("slam"));
        blueprint.Enemies.Add(goblin);

        var script = new ScenarioScript()
            .HeroPlays("smite", "goblin")
            .HeroEndsTurn()
            .EnemyActs("goblin", "slam", "knight")
            .NextRound()
            .HeroPlays("smite", "goblin")
            .Build();

        return new Playthrough(blueprint, script, combatId: "fight");
    }

    private static Node CombatNode(string id) =>
        new(new NodeId(id), StandardRunIds.CombatNode, new CombatNodePayload(BuildGoblinFight));

    private static RunDefinitionRegistry BuildRegistry()
    {
        var builder = new RunDefinitionRegistryBuilder();
        new StandardRunPackage().RegisterDefinitions(builder);
        return builder.Build();
    }

    private static RunState NewRun(int current, int max, RunMap map, params CardDefinitionId[] deck)
    {
        var run = new RunState(new RunId("run"), new HealthState(current, max), map);
        foreach (var card in deck)
            run.AddDeckCard(card);
        return run;
    }

    [Fact]
    public void CombatNode_DrivesRealEngine_AndReconcilesHpOntoTheRun()
    {
        var registry = BuildRegistry();
        var run = NewRun(30, 30, new RunMap(new[] { CombatNode("fight-1") }), Smite, Smite, Smite);

        new RunRunner(registry, new ScriptedChoiceProvider()).Run(run);

        Assert.Equal(RunResult.Victory, run.Result);
        Assert.Equal(26, run.Health.Current); // 30 − 4 (one slam)

        var resolved = Assert.IsType<CombatResolvedRunEvent>(
            run.EventHistory.First(e => e is CombatResolvedRunEvent));
        Assert.Equal(CombatResult.Victory, resolved.Result);
        Assert.Equal(26, resolved.HeroHpRemaining);
        Assert.Equal(4, resolved.DamageTaken);
    }

    [Fact]
    public void WoundedHero_CarriesCurrentHp_IntoTheNextFight()
    {
        var registry = BuildRegistry();
        var run = NewRun(30, 30,
            new RunMap(new[] { CombatNode("fight-1"), CombatNode("fight-2") }),
            Smite, Smite, Smite);

        new RunRunner(registry, new ScriptedChoiceProvider()).Run(run);

        // 30 − 4 (first fight) − 4 (second fight) = 22. If HP did not carry, each fight would restart at 30
        // and the run would end at 26.
        Assert.Equal(22, run.Health.Current);
        Assert.Equal(2, run.EventHistory.OfType<CombatResolvedRunEvent>().Count());
    }

    [Fact]
    public void Relic_IsJustARunLevelTriggeredProgram_HealingAfterVictory()
    {
        var registry = BuildRegistry();
        var run = NewRun(20, 30, new RunMap(new[] { CombatNode("fight-1") }), Smite, Smite, Smite);
        run.AddRelic(new RelicInstance(StandardRelics.Bloodstone(healAmount: 5)));

        new RunRunner(registry, new ScriptedChoiceProvider()).Run(run);

        // 20 − 4 (slam) = 16, then the relic reacts to CombatResolved(Victory) → +5 = 21. Without the relic
        // the run would end at 16, so the +5 is unambiguously the relic's doing.
        Assert.Equal(21, run.Health.Current);
    }

    [Fact]
    public void EventNode_PresentsChoices_AndAppliesTheChosenEffects()
    {
        var registry = BuildRegistry();
        var script = new EventScript("crossroads", new[]
        {
            new EventSituation("crossroads", "event.crossroads", new[]
            {
                new EventChoice("take-gold", new IRunEffectRequest[]
                {
                    new ChangeResourceRunEffect(StandardRunIds.Gold, 10),
                }),
                new EventChoice("leave", Array.Empty<IRunEffectRequest>()),
            }),
        });

        var node = new Node(new NodeId("shrine"), StandardRunIds.EventNode, script);
        var run = NewRun(30, 30, new RunMap(new[] { node }));

        new RunRunner(registry, new ScriptedChoiceProvider("take-gold")).Run(run);

        Assert.Equal(10, run.GetResource(StandardRunIds.Gold));
        var choice = Assert.IsType<EventChoiceMadeRunEvent>(
            run.EventHistory.First(e => e is EventChoiceMadeRunEvent));
        Assert.Equal("take-gold", choice.ChoiceId);
    }

    [Fact]
    public void RunRunner_DrivesAMixedMap_PurelyByResolverRegistration()
    {
        var registry = BuildRegistry();
        var eventScript = new EventScript("s", new[]
        {
            new EventSituation("s", "event.s", new[]
            {
                new EventChoice("gold", new IRunEffectRequest[]
                {
                    new ChangeResourceRunEffect(StandardRunIds.Gold, 5),
                }),
            }),
        });

        var map = new RunMap(new[]
        {
            CombatNode("fight-1"),
            new Node(new NodeId("shrine"), StandardRunIds.EventNode, eventScript),
            CombatNode("fight-2"),
        });
        var run = NewRun(30, 30, map, Smite, Smite, Smite);

        new RunRunner(registry, new ScriptedChoiceProvider("gold")).Run(run);

        Assert.Equal(RunResult.Victory, run.Result);
        Assert.Equal(3, run.EventHistory.OfType<NodeEnteredRunEvent>().Count());
        Assert.Equal(5, run.GetResource(StandardRunIds.Gold));
        Assert.Single(run.EventHistory.OfType<RunEndedRunEvent>());
    }

    [Fact]
    public void ResolveGuard_StopsARelicFeedbackLoop_InsteadOfWedgingTheRun()
    {
        var registry = BuildRegistry();

        // A pathological relic: every resource change triggers another resource change → infinite without a
        // guard. The processor's iteration cap must stop it and log the trip.
        var loopRelic = new RelicDefinition(
            new RelicId("snowball"),
            "Snowball",
            runPrograms: new ITriggeredRunEffectDefinition[]
            {
                new TriggeredRunEffect<ResourceChangedRunEvent>((_, _) =>
                    new IRunEffectRequest[] { new ChangeResourceRunEffect(StandardRunIds.Gold, 1) }),
            });

        var eventScript = new EventScript("s", new[]
        {
            new EventSituation("s", "event.s", new[]
            {
                new EventChoice("spark", new IRunEffectRequest[]
                {
                    new ChangeResourceRunEffect(StandardRunIds.Gold, 1),
                }),
            }),
        });

        var node = new Node(new NodeId("spark"), StandardRunIds.EventNode, eventScript);
        var run = NewRun(30, 30, new RunMap(new[] { node }));
        run.AddRelic(new RelicInstance(loopRelic));

        // Small cap so the test is fast; the point is that Run returns at all.
        var runner = new RunRunner(registry, new ScriptedChoiceProvider("spark"),
            new RunEffectProcessor(maxIterations: 50));
        runner.Run(run);

        Assert.Contains(run.Log, entry => entry.Type == StandardRunLogTypes.ResolveGuardTripped);
    }
}
