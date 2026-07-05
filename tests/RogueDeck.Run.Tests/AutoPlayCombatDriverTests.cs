using RogueDeck.Core.Combat;
using RogueDeck.Run;
using RogueDeck.Scenario.Authoring;
using RogueDeck.Scenario.Dsl;
using RogueDeck.Scenario.Scripting;

namespace RogueDeck.Run.Tests;

// Tests for the headless auto-play driver (R7a): a combat runs to a terminal result with no authored script,
// so a data-defined encounter can be simulated end to end.
public class AutoPlayCombatDriverTests
{
    // Builds a scriptless playthrough: a knight with a deck of 6-damage smites vs a goblin that slams for 4.
    private static Playthrough Encounter(int heroCurrent, int heroMax, int goblinHp, int smiteCopies)
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

        blueprint.Hero = new HeroBlueprint("knight") { MaxHealth = heroMax, CurrentHealth = heroCurrent };
        blueprint.Hero.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));
        for (var i = 0; i < smiteCopies; i++)
            blueprint.Hero.Deck.Add(new DeckEntry(new CardDefinitionId("smite"), 1));

        var goblin = new EnemyBlueprint("goblin") { MaxHealth = goblinHp };
        goblin.Actions.Add(new EnemyActionDefinitionId("slam"));
        blueprint.Enemies.Add(goblin);

        // No script — the driver decides how it is played.
        return new Playthrough(blueprint, new ScenarioScript().Build(), combatId: "fight");
    }

    [Fact]
    public void Auto_play_wins_a_winnable_fight_without_a_script()
    {
        var result = new AutoPlayCombatDriver().Drive(Encounter(heroCurrent: 30, heroMax: 30, goblinHp: 12, smiteCopies: 5));

        Assert.Equal(CombatResult.Victory, result.Result);
        Assert.True(result.HeroHpRemaining > 0);
    }

    [Fact]
    public void Auto_play_loses_when_the_hero_cannot_win()
    {
        // No offensive cards and a fragile hero vs a slamming goblin → the hero is ground down.
        var result = new AutoPlayCombatDriver().Drive(Encounter(heroCurrent: 6, heroMax: 6, goblinHp: 999, smiteCopies: 0));

        Assert.Equal(CombatResult.Defeat, result.Result);
        Assert.Equal(0, result.HeroHpRemaining);
    }

    [Fact]
    public void Auto_play_is_deterministic_for_a_seed()
    {
        var a = new AutoPlayCombatDriver().Drive(Encounter(30, 30, 12, 5));
        var b = new AutoPlayCombatDriver().Drive(Encounter(30, 30, 12, 5));
        Assert.Equal(a.Result, b.Result);
        Assert.Equal(a.HeroHpRemaining, b.HeroHpRemaining);
    }

    [Fact]
    public void Auto_play_drives_a_combat_node_through_the_run()
    {
        var registry = new RunDefinitionRegistryBuilder();
        new StandardRunPackage(new AutoPlayCombatDriver()).RegisterDefinitions(registry);
        var built = registry.Build();

        var run = new RunState(new RunId("run"), new HealthState(30, 30),
            new RunMap(new[] { new Node(new NodeId("fight"), StandardRunIds.CombatNode,
                new CombatNodePayload(_ => Encounter(30, 30, 12, 0))) }));
        // Deck is projected by the bridge; give the run some smites.
        for (var i = 0; i < 5; i++)
            run.AddDeckCard(new CardDefinitionId("smite"));

        new RunRunner(built, new ScriptedChoiceProvider()).Run(run);

        Assert.Equal(RunResult.Victory, run.Result);
        Assert.Contains(run.EventHistory.OfType<CombatResolvedRunEvent>(),
            e => e.Result == CombatResult.Victory);
    }

    // A fight the hero wins on turn 1 (a fragile actionless goblin) — so the hero takes NO combat damage and any HP
    // loss is attributable to a relic's combat rule firing.
    private static Playthrough OneTurnWin(RunState run)
    {
        var blueprint = new ScenarioBlueprint();
        blueprint.Cards.Add(new CardBlueprint("smite")
        {
            Program = Effects.Program(Effects.DealDamage(Targets.EventTarget, 6)),
        });
        blueprint.Hero = new HeroBlueprint("knight") { MaxHealth = run.Health.Max, CurrentHealth = run.Health.Current };
        blueprint.Hero.Resources.Add(new ResourceSpec(StandardCombatIds.EnergyResource, 3, 3));
        // Deck is projected by the bridge; the encounter itself carries none.
        var goblin = new EnemyBlueprint("goblin") { MaxHealth = 5 }; // dies to one smite, and has no actions to hit back
        blueprint.Enemies.Add(goblin);
        return new Playthrough(blueprint, new ScenarioScript().Build(), combatId: "fight");
    }

    [Fact]
    public void A_data_authored_relic_combat_rule_fires_during_a_real_fight()
    {
        // Face (b) end to end: a relic authored as data with a turn-start rule that deals 2 to the hero. The hero
        // wins on turn 1 taking no enemy damage, so the run's reconciled HP (30 → 28) is exactly the one turn-start
        // firing — proof the data-defined program is injected AND actually runs in combat.
        var relic = new RelicData
        {
            Id = "cursed",
            DisplayName = "Cursed Idol",
            CombatRules = new[]
            {
                new RelicCombatRule
                {
                    Trigger = "turnStarted",
                    Program = new EffectProgram<TurnStartedTriggeredEffectContext>(
                        new DealDamageNode<TurnStartedTriggeredEffectContext>(
                            CombatantTargetSelectors.Source,
                            new ConstantExpression<TurnStartedTriggeredEffectContext>(2))),
                    Priority = 0,
                },
            },
        }.ToDefinition();

        var registry = new RunDefinitionRegistryBuilder();
        new StandardRunPackage(new AutoPlayCombatDriver()).RegisterDefinitions(registry);

        var run = new RunState(new RunId("run"), new HealthState(30, 30),
            new RunMap(new[] { new Node(new NodeId("fight"), StandardRunIds.CombatNode, new CombatNodePayload(OneTurnWin)) }));
        run.AddRelic(new RelicInstance(relic));
        run.AddDeckCard(new CardDefinitionId("smite"));

        new RunRunner(registry.Build(), new ScriptedChoiceProvider()).Run(run);

        Assert.Equal(RunResult.Victory, run.Result);
        Assert.Equal(28, run.Health.Current); // 30 - 2 from the single turn-start firing
    }
}
